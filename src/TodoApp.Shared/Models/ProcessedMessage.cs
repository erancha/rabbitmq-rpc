namespace TodoApp.Shared.Models;

// One record per processed write, keyed by its idempotency key, holding the reply that write produced.
public class ProcessedMessage
{
    // The idempotency key identifying the write; primary key.
    public string Key { get; set; } = string.Empty;

    // Hash of the request (message type and body).
    public string RequestHash { get; set; } = string.Empty;

    // The reply produced when the write was first processed; not null once the row is committed.
    public string? ResponseJson { get; set; }

    // When the write was processed, in UTC.
    public DateTime CreatedAt { get; set; }
}
