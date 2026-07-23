using TodoApp.Shared.Messages;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Base for exceptions whose message and kind are authored as safe, client-facing text.
/// Messages of exceptions outside this hierarchy are internal detail and must never reach clients.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>One of the RpcErrorKind constants.</summary>
    public string Kind { get; }

    protected DomainException(string kind, string message)
        : base(message)
    {
        Kind = kind;
    }
}

/// <summary>Requested entity does not exist.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message)
        : base(RpcErrorKind.NOT_FOUND, message) { }
}

/// <summary>Request payload or state is invalid.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message)
        : base(RpcErrorKind.VALIDATION, message) { }
}
