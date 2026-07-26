using System.ComponentModel.DataAnnotations;

namespace TodoApp.Shared.Validation;

/// <summary>
/// Declares an optional string field that must contain non-whitespace text when supplied.
/// Complements [Required] for partial-update payloads: an omitted (null) field is a legal
/// "leave unchanged" state, but a present-yet-blank value is rejected.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NotWhitespaceAttribute : ValidationAttribute
{
    public NotWhitespaceAttribute()
        : base("The {0} field cannot be blank when provided.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && !string.IsNullOrWhiteSpace(text));
}
