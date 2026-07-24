namespace Storava.Domain.Common;

/// <summary>
/// Represents a domain or operation error with a stable code and a human message.
/// The <see cref="Code"/> is culture-neutral and safe for logs; <see cref="Message"/>
/// may be localized by upper layers.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Unexpected(string message) => new("error.unexpected", message);
    public static Error Validation(string message) => new("error.validation", message);
    public static Error NotFound(string message) => new("error.not_found", message);
    public static Error Conflict(string message) => new("error.conflict", message);

    public override string ToString() => string.IsNullOrEmpty(Code) ? Message : $"{Code}: {Message}";
}
