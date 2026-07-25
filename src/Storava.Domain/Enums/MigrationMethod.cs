namespace Storava.Domain.Enums;

/// <summary>
/// How a folder can be relocated. Official tool support is always preferred; a
/// junction/symbolic link is only a fallback when no official mechanism exists.
/// </summary>
public enum MigrationMethod
{
    /// <summary>Relocation is not supported for this item.</summary>
    None = 0,

    /// <summary>The tool provides a setting or environment variable for its storage location.</summary>
    OfficialSetting = 1,

    /// <summary>Move the folder and leave an NTFS junction behind.</summary>
    Junction = 2,

    /// <summary>Move the folder and leave a symbolic link behind.</summary>
    SymbolicLink = 3
}
