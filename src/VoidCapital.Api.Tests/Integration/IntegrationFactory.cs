using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoidCapital.Api.Data;

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
    }

    public async Task<AppDbContext> CreateDbAsync() =>
        await DbFactory.CreateDbContextAsync();
}

/// <summary>
/// All integration tests share one factory (one migrated test DB) and run
/// serially. Tests isolate themselves with unique users/symbols rather than
/// truncating shared tables.
/// </summary>
[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationFactory>;
