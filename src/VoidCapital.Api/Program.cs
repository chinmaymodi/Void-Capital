using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using VoidCapital.Api.Data;
using VoidCapital.Api.Middleware;
using VoidCapital.Api.Modules.Auth.Models;
using VoidCapital.Api.Modules.Auth.Services;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Services;
using VoidCapital.Api.Shared.Repositories;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    // File sink so the Windows service (which has no console) leaves a
    // diagnosable trail. Rolling daily, 14 days retained, next to the DLL.
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "voidcapital-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .Enrich.WithProperty("Application", "VoidCapital")
    .CreateLogger();

try
{
    // Pin the content root to the assembly directory: Windows services (and
    // bare `dotnet VoidCapital.Api.dll` launches) run with the current
    // directory set to System32, which would otherwise hide appsettings.json
    // and break connection-string discovery.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Host.UseSerilog();
    // Register with the Service Control Manager when launched as a Windows
    // service (sc start). Without this, SCM kills the process after 30s
    // (error 1053). No-op when run as a normal console app.
    builder.Host.UseWindowsService();

    var postgresConnection = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

    // Database migrations (FluentMigrator)
    builder.Services.AddFluentMigratorCore()
        .ConfigureRunner(rb => rb
            .AddPostgres()
            .WithGlobalConnectionString(postgresConnection)
            .ScanIn(typeof(Program).Assembly).For.Migrations());

    builder.Services.AddControllers();
    builder.Services.AddHealthChecks()
        .AddNpgSql(postgresConnection)
        .AddRedis(redisConnection);

    // API-key auth (A1-class review fixes). Keys live in auth.keys.json
    // (gitignored, generated once, copied to output). The admin key grants the
    // Admin role; per-user keys grant access to that user's own data.
    var authKeysPath = Path.Combine(AppContext.BaseDirectory, "auth.keys.json");
    builder.Services.AddSingleton(AuthKeys.Load(authKeysPath));
    builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.SchemeName, null);
    builder.Services.AddAuthorization();

    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(
                    builder.Configuration.GetSection("Cors:AllowedOrigins")
                        .Get<string[]>() ?? Array.Empty<string>())
                .AllowAnyMethod().AllowAnyHeader()));

    // Redis distributed cache (Cache-Aside pattern for market data)
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
    });

    // EF Core data access (repos create short-lived contexts via the factory)
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseNpgsql(postgresConnection));

    // Repositories (DIP: services depend on interfaces, not EF/Npgsql)
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IHoldingRepository, HoldingRepository>();
    builder.Services.AddScoped<ITradeRepository, TradeRepository>();
    builder.Services.AddScoped<IPnlRepository, PnlRepository>();
    builder.Services.AddScoped<ISettingsRepository, SettingsRepository>();
    builder.Services.AddScoped<IMarketDataRepository, MarketDataRepository>();
    builder.Services.AddScoped<ISignalRepository, SignalRepository>();
    builder.Services.AddScoped<ISignalPerformanceRepository, SignalPerformanceRepository>();

    // Services
    builder.Services.AddScoped<IPortfolioService, PortfolioService>();
    builder.Services.AddScoped<IMarketDataService, MarketDataService>();
    builder.Services.AddScoped<ISignalService, SignalService>();
    builder.Services.AddScoped<SignalPerformanceService>();
    builder.Services.Configure<PythonSettings>(
        builder.Configuration.GetSection(PythonSettings.SectionName));
    builder.Services.AddScoped<IProcessRunner, ProcessRunner>();
    builder.Services.AddScoped<IPythonBridge, PythonBridge>();
    builder.Services.AddScoped<ISignalIntegrationService, SignalIntegrationService>();
    builder.Services.AddScoped<ICycleRunRepository, CycleRunRepository>();
    builder.Services.AddScoped<IDailyCycleRunner, DailyCycleRunner>();
    builder.Services.AddScoped<IAdminService, AdminService>();
    // DS1: cross-instance cycle lock. Needs the raw connection string, so it
    // is registered with a factory rather than resolved from config.
    builder.Services.AddScoped<ICycleLock>(_ => new PostgresCycleLock(postgresConnection));
    builder.Services.AddSingleton<ISignalJobService, SignalJobService>();
    builder.Services.AddHostedService<DailyCycleService>();
    builder.Services.AddHostedService<IntradayCycleService>();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        try
        {
            runner.MigrateUp();
            Log.Information("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            // Local resilience: the API still boots so /api/health can report
            // the failure. Integration tests (D6) will provide a dedicated
            // test PostgreSQL; until then do not crash on startup.
            Log.Warning(ex, "Database migrations failed. Check that PostgreSQL is running.");
        }
    }

    app.UseMiddleware<ExceptionMiddleware>();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/api/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
