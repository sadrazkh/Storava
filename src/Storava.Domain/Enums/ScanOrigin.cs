namespace Storava.Domain.Enums;

/// <summary>Where a scan session's data came from.</summary>
public enum ScanOrigin
{
    /// <summary>Produced by scanning this machine.</summary>
    Local = 0,

    /// <summary>Restored from a .storava archive, possibly from another machine.</summary>
    Imported = 1
}
