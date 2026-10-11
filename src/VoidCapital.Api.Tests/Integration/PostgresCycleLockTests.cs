using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoidCapital.Api.Services;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// PostgresCycleLock against real PostgreSQL: the advisory lock is exclusive
/// (a second acquire returns null), and disposing the lease releases it so a
/// later acquire succeeds.
/// </summary>
[Collection("integration")]
public class PostgresCycleLockTests
{
    private readonly IntegrationFactory _factory;

    public PostgresCycleLockTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    private string ConnectionString =>
        _factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Postgres"]!;

    /// <summary>
    /// The app's DailyCycleService fires a catch-up cycle on startup, which
    /// holds the advisory lock for the duration of the run. Poll until the
    /// lock is free so these tests are deterministic regardless of when the
    /// background service finishes.
    /// </summary>
    private static async Task<ICycleLease?> AcquireWithRetryAsync(PostgresCycleLock lockService)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var lease = await lockService.TryAcquireAsync();
            if (lease is not null)
                return lease;
            await Task.Delay(100);
        }
        return null;
    }

    [Fact]
    public async Task TryAcquire_AcquiresLock()
    {
        var lockService = new PostgresCycleLock(ConnectionString);

        var lease = await AcquireWithRetryAsync(lockService);

        lease.Should().NotBeNull();
        await lease!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquire_SecondAcquireWhileHeld_ReturnsNull()
    {
        var lockService = new PostgresCycleLock(ConnectionString);
        var first = await AcquireWithRetryAsync(lockService);
        try
        {
            var second = await lockService.TryAcquireAsync();
            second.Should().BeNull();
        }
        finally
        {
            await first!.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAcquire_AfterLeaseDisposed_AcquiresAgain()
    {
        var lockService = new PostgresCycleLock(ConnectionString);
        var first = await AcquireWithRetryAsync(lockService);
        await first!.DisposeAsync();

        var second = await AcquireWithRetryAsync(lockService);

        second.Should().NotBeNull();
        await second!.DisposeAsync();
    }
}