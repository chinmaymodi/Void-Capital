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
}