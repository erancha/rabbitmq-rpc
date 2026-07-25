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

    // How often the retention sweep runs.
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
}
