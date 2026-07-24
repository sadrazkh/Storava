namespace Storava.Domain.Enums;

/// <summary>High-level classification of what is consuming disk space.</summary>
public enum StorageCategory
{
    Unknown = 0,
    PersonalFiles,
    Applications,
    WindowsSystem,
    TemporaryFiles,
    DeveloperTools,
    PackageCaches,
    BuildArtifacts,
    Docker,
    Wsl,
    VirtualMachines,
    AiModels,
    Sdks,
    IdeCaches,
    BrowserCaches,
    GameLibraries,
    Downloads,
    Media,
    Backups,
    Archives,
    Logs
}
