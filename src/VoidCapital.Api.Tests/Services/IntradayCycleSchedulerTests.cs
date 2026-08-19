using VoidCapital.Api.Services;

namespace VoidCapital.Api.Tests.Services;

/// <summary>
/// Scheduler math for IntradayCycleService (D19 fallback refresher).
/// The background loop itself is not exercised; the pure helpers that decide
/// market-hours windows are.
/// </summary>
public class IntradayCycleSchedulerTests
{
    [Theory]
    [InlineData(3, 45)]   // 09:15 IST open, inclusive
    [InlineData(6, 0)]    // midday
    [InlineData(9, 44)]   // one minute before old close, still inside
    [InlineData(9, 59)]   // one minute before close
    public void IsMarketHours_TrueInsideWindow(int hour, int minute)
    {
        var now = new DateTime(2026, 8, 13, hour, minute, 0, DateTimeKind.Utc);
        Assert.True(IntradayCycleService.IsMarketHours(now));
    }

    [Theory]
    [InlineData(3, 44)]   // one minute before open
    [InlineData(10, 0)]   // exactly at close, exclusive
    [InlineData(12, 0)]   // afternoon
    [InlineData(0, 0)]    // midnight
    public void IsMarketHours_FalseOutsideWindow(int hour, int minute)
    {
        var now = new DateTime(2026, 8, 13, hour, minute, 0, DateTimeKind.Utc);
        Assert.False(IntradayCycleService.IsMarketHours(now));
    }

    [Theory]
    [InlineData(2026, 8, 15, 6, 0)]   // Saturday 11:30 IST - inside window, weekend
    [InlineData(2026, 8, 16, 6, 0)]   // Sunday 11:30 IST - inside window, weekend
    [InlineData(2026, 8, 15, 3, 45)]  // Saturday exactly at open
    [InlineData(2026, 8, 16, 9, 59)]  // Sunday one minute before close
    public void IsMarketHours_FalseOnWeekends(int year, int month, int day, int hour, int minute)
    {
        var now = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
        Assert.False(IntradayCycleService.IsMarketHours(now));
    }

    [Fact]
    public void NextMarketOpen_SameDayWhenBeforeOpen()
    {
        var now = new DateTime(2026, 8, 13, 2, 0, 0, DateTimeKind.Utc);
        var next = IntradayCycleService.NextMarketOpen(now);
        Assert.Equal(new DateTime(2026, 8, 13, 3, 45, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextMarketOpen_NextDayWhenAfterOpen()
    {
        var now = new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        var next = IntradayCycleService.NextMarketOpen(now);
        Assert.Equal(new DateTime(2026, 8, 14, 3, 45, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextMarketOpen_ExactlyAtOpenReturnsNextDay()
    {
        // At exactly open the market is already open, so the next open is
        // tomorrow (this is the sleep target used outside market hours).
        var now = new DateTime(2026, 8, 13, 3, 45, 0, DateTimeKind.Utc);
        var next = IntradayCycleService.NextMarketOpen(now);
        Assert.Equal(new DateTime(2026, 8, 14, 3, 45, 0, DateTimeKind.Utc), next);
    }

    // ---------- F15: dual-feed freshness (equities + options) ----------

    private static readonly DateTime Now = new(2026, 8, 13, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsStale_FalseWhenBothFeedsFresh()
    {
        var fresh = Now.AddMinutes(-1);
        Assert.False(IntradayCycleService.IsStale(fresh, fresh, Now));
    }

    [Fact]
    public void IsStale_TrueWhenEquityFeedMissing()
    {
        var fresh = Now.AddMinutes(-1);
        Assert.True(IntradayCycleService.IsStale(null, fresh, Now));
    }

    [Fact]
    public void IsStale_TrueWhenOptionsFeedMissing()
    {
        // F15: the options snapshot table is empty - a silent
        // options-collection failure must trip the stale path even though
        // the equity bars are fresh.
        var fresh = Now.AddMinutes(-1);
        Assert.True(IntradayCycleService.IsStale(fresh, null, Now));
    }

    [Fact]
    public void IsStale_TrueWhenEquityFeedStale()
    {
        var fresh = Now.AddMinutes(-1);
        var stale = Now.AddMinutes(-10); // > 5-minute threshold
        Assert.True(IntradayCycleService.IsStale(stale, fresh, Now));
    }

    [Fact]
    public void IsStale_TrueWhenOptionsFeedStale()
    {
        // F15: equity bars fresh but options snapshots frozen - the IV leg
        // of avg3 would silently compute on stale Greeks.
        var fresh = Now.AddMinutes(-1);
        var stale = Now.AddMinutes(-10);
        Assert.True(IntradayCycleService.IsStale(fresh, stale, Now));
    }

    [Fact]
    public void IsStale_TrueWhenBothFeedsMissing()
    {
        Assert.True(IntradayCycleService.IsStale(null, null, Now));
    }
}