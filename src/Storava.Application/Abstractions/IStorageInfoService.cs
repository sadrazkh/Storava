using Storava.Application.Common;

namespace Storava.Application.Abstractions;

/// <summary>Read-only view over the machine's drives. Never mutates the file system.</summary>
public interface IStorageInfoService
{
    IReadOnlyList<DriveSnapshot> GetDrives();
}
