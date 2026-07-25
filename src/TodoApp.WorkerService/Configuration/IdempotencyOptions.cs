namespace TodoApp.WorkerService.Configuration;

/// <summary>
/// Retention policy for the ProcessedMessages idempotency markers, bound from the "Idempotency"
/// configuration section.
/// </summary>
public class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    // The values below are fallbacks; the operational settings come from the "Idempotency"
    // configuration section (appsettings.json or Idempotency__* environment variables).

    // Markers older than this are deleted by the sweep.
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(1);

    // Time of day, in UTC, at which the once-daily retention sweep runs. Anchored to an off-peak
    // hour so the bulk delete does not compete with request processing during busy periods.
    public TimeSpan DailySweepAtUtc { get; set; } = TimeSpan.FromHours(2);
}
