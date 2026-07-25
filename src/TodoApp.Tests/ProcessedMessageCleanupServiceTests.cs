using Xunit;
using TodoApp.WorkerService.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the daily sweep clock: the delay computed before each sweep lands on the next
/// occurrence of the configured off-peak hour, whether that occurrence is later today or tomorrow.
/// </summary>
public class ProcessedMessageCleanupServiceTests
{
    private static readonly TimeSpan TwoAm = TimeSpan.FromHours(2);

    [Fact]
    public void Waits_until_today_when_target_hour_is_still_ahead()
    {
        var now = new DateTime(2026, 7, 25, 0, 30, 0, DateTimeKind.Utc);

        var delay = ProcessedMessageCleanupService.DelayUntilNextSweep(now, TwoAm);

        Assert.Equal(TimeSpan.FromMinutes(90), delay);
    }

    [Fact]
    public void Rolls_to_tomorrow_when_target_hour_already_passed_today()
    {
        var now = new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc);

        var delay = ProcessedMessageCleanupService.DelayUntilNextSweep(now, TwoAm);

        Assert.Equal(TimeSpan.FromHours(23), delay);
    }

    [Fact]
    public void Rolls_a_full_day_when_now_is_exactly_the_target_hour()
    {
        var now = new DateTime(2026, 7, 25, 2, 0, 0, DateTimeKind.Utc);

        var delay = ProcessedMessageCleanupService.DelayUntilNextSweep(now, TwoAm);

        Assert.Equal(TimeSpan.FromHours(24), delay);
    }
}
