namespace TodoApp.Shared.Messages;

/// <summary>
/// Payload of a successful creation reply: the id the worker assigned. CreatedId is required so
/// a success reply that omits it fails deserialization loudly instead of defaulting to zero.
/// </summary>
public class CreatedResponse
{
    public required int CreatedId { get; init; }
}
