namespace Storava.Domain.Enums;

public enum ScanMode
{
    /// <summary>Fast pass: sizes and metadata only.</summary>
    Quick = 0,
    /// <summary>Deep pass: hashing, duplicate detection and full classification.</summary>
    Deep = 1
}

public enum ScanStatus
{
    Created = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}
