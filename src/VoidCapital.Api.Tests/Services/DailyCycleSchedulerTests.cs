using VoidCapital.Api.Services;

namespace VoidCapital.Api.Tests.Services;

/// <summary>
/// Scheduler slot math for DailyCycleService (catch-up behavior).
/// The background loop itself is not exercised; the pure helpers that decide
/// whether a slot was missed are.
/// </summary>
public class DailyCycleSchedulerTests
{
    [Fact]
    public void LastScheduledSlotUtc_IsTodayWhenPastSlot()
    {
        var now = new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc); // after 12:30
        var slot = DailyCycleService.LastScheduledSlotUtc(now);
        Assert.Equal(new DateTime(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc), slot);
    }

    [Fact]
    public void LastScheduledSlotUtc_IsYesterdayWhenBeforeSlot()
    {
        var now = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc); // before 12:30
        var slot = DailyCycleService.LastScheduledSlotUtc(now);
        Assert.Equal(new DateTime(2026, 8, 4, 12, 30, 0, DateTimeKind.Utc), slot);
    }

    [Fact]
    public void LastScheduledSlotUtc_IsTodayExactlyAtSlot()
    {
        var now = new DateTime(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc);
        var slot = DailyCycleService.LastScheduledSlotUtc(now);
        Assert.Equal(now, slot);
    }

    [Fact]
    public void NeedsCatchUp_TrueWhenNeverRan()
    {
        var slot = DailyCycleService.LastScheduledSlotUtc(new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc));
        Assert.True(DailyCycleService.NeedsCatchUp(slot, lastFinishedAtUtc: null));
    }

    [Fact]
    public void NeedsCatchUp_TrueWhenPreviousRunCrashedMidFlight()
    {
        var slot = DailyCycleService.LastScheduledSlotUtc(new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc));
        // A run row exists but never reached the finally block -> no FinishedAt.
        Assert.True(DailyCycleService.NeedsCatchUp(slot, lastFinishedAtUtc: null));
    }

    [Fact]
    public void NeedsCatchUp_TrueWhenLastRunPredatesSlot()
    {
        var slot = new DateTime(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc);
        var lastFinished = new DateTime(2026, 8, 4, 12, 31, 0, DateTimeKind.Utc);
        Assert.True(DailyCycleService.NeedsCatchUp(slot, lastFinished));
    }

    [Fact]
    public void NeedsCatchUp_FalseWhenLastRunServedTheSlot()
    {
        var slot = new DateTime(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc);
        var lastFinished = new DateTime(2026, 8, 5, 12, 32, 0, DateTimeKind.Utc);
        Assert.False(DailyCycleService.NeedsCatchUp(slot, lastFinished));
    }
}
