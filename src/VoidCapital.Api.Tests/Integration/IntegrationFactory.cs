using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Signals.Services;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// WebApplicationFactory pointed at the dedicated integration test database
/// (void_capital_test on the local Docker Postgres). FluentMigrator runs on
/// app boot, so the schema is created and migrations 001-003 applied before
/// the first test hits an endpoint.
/// </summary>
public class IntegrationFactory : WebApplicationFactory<Program>
{
    private const string TestPostgres =
        "Host=localhost;Port=5432;Database=void_capital_test;Username=vc_user;Password=vc_pass";
    private const string TestRedis = "localhost:6379";

    public IDbContextFactory<AppDbContext> DbFactory =>
        Services.GetRequiredService<IDbContextFactory<AppDbContext>>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", TestPostgres);
        builder.UseSetting("ConnectionStrings:Redis", TestRedis);
        builder.ConfigureServices(services =>
        {
            // Replace the real Python bridge. Integration tests verify HTTP
            // wiring, per-user iteration and response contracts -- spawning
            // the actual interpreter (walk-forward gates + retry backoff
            // against the dev DB) turned one admin test into minutes.
            // Real Python execution is covered by PythonBridgeTests with a
            // mocked IProcessRunner and by the manual generate_signals smoke.
            var bridge = services.SingleOrDefault(d => d.ServiceType == typeof(IPythonBridge));
            if (bridge is not null)
                services.Remove(bridge);
            services.AddScoped<IPythonBridge>(_ => new StubPythonBridge());
        });
    }

    public async Task<AppDbContext> CreateDbAsync() =>
        await DbFactory.CreateDbContextAsync();

    /// <summary>Always-succeed bridge: instant, environment-independent.</summary>
    private sealed class StubPythonBridge : IPythonBridge
    {
        public Task<PythonRunResult> RunSignalGeneration(int userId, bool noGate) =>
            Task.FromResult(new PythonRunResult(true, "0", ""));
    }
}

/// <summary>
/// All integration tests share one factory (one migrated test DB) and run
/// serially. Tests isolate themselves with unique users/symbols rather than
/// truncating shared tables.
/// </summary>
[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationFactory>;
