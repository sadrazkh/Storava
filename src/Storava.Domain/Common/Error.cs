namespace Storava.Domain.Common;

/// <summary>
/// Represents a domain or operation error with a stable code and a human message.
/// The <see cref="Code"/> is culture-neutral and safe for logs; <see cref="Message"/>
/// may be localized by upper layers.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>
    /// The specifics of this one failure: which path, how much room was short, which rule matched.
    /// <para>
    /// The <see cref="Code"/> says what kind of failure it is and the upper layer turns that into a
    /// sentence in the user's language. That sentence can only ever be general — "there is not
    /// enough free space" is true of every such failure and answers nothing. This carries the part
    /// that differs, so the general sentence can be followed by the fact.
    /// </para>
    /// <para>
    /// Not localized, and deliberately so: it is paths and numbers, which read the same in every
    /// language, and inventing a translation channel for them would put a second message system
    /// beside the resource dictionaries.
    /// </para>
    /// </summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The same error, carrying what was concrete about this occurrence.</summary>
    public Error With(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? this : this with { Detail = detail.Trim() };

    public static Error Unexpected(string message) => new("error.unexpected", message);
    public static Error Validation(string message) => new("error.validation", message);
    public static Error NotFound(string message) => new("error.not_found", message);
    public static Error Conflict(string message) => new("error.conflict", message);

    // The detail is part of what this error is, so it belongs in the string form. Without it two
    // errors that differ only by their specifics print identically, and an assertion failure reads
    // "Expected X, Actual X" — which is how this comment came to be written.
    public override string ToString()
    {
        string head = string.IsNullOrEmpty(Code) ? Message : $"{Code}: {Message}";
        return string.IsNullOrEmpty(Detail) ? head : $"{head} ({Detail})";
    }
}
