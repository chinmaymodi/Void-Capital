using FluentMigrator.Runner;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VoidCapital.Api.Data;
using VoidCapital.Api.Middleware;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Shared.Repositories;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.WithProperty("Application", "VoidCapital")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

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

    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

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

    // Services
    builder.Services.AddScoped<IPortfolioService, PortfolioService>();
    builder.Services.AddScoped<IMarketDataService, MarketDataService>();

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
