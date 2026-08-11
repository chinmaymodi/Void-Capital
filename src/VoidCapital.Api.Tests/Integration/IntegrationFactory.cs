using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Signals.Services;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// WebApplicationFactory backed by disposable Testcontainers: a fresh
/// Postgres container (schema created by FluentMigrator on app boot) and a
/// Redis container. Connection strings come from the live containers, so the
/// suite runs identically anywhere Docker is available -- no hand-configured
/// local database, no dependence on whatever happens to be running.
/// </summary>
public class IntegrationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("void_capital_test")
        .WithUsername("vc_user")
        .WithPassword("vc_pass")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .Build();

    public IDbContextFactory<AppDbContext> DbFactory =>
        Services.GetRequiredService<IDbContextFactory<AppDbContext>>();

    /// <summary>Start the dependency containers before any test touches the host.</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
    }

    /// <summary>Tear down the host first, then the containers.</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
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
        public Task<PythonRunResult> RunSignalGeneration(int userId, bool noGate, CancellationToken ct = default) =>
            Task.FromResult(new PythonRunResult(true, "0", ""));

        public Task<PythonRunResult> RunDataRefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(new PythonRunResult(true, "", ""));
    }
}

/// <summary>
/// All integration tests share one factory (one set of containers, one
/// migrated test DB) and run serially. Tests isolate themselves with unique
/// users/symbols rather than truncating shared tables.
/// </summary>
[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationFactory>;
