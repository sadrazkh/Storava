using Storava.Application.Abstractions;
using Storava.Application.Common;
using Storava.Domain.ValueObjects;

namespace Storava.Platform.Storage;

/// <summary>Read-only drive enumeration backed by <see cref="DriveInfo"/>.</summary>
public sealed class SystemStorageService : IStorageInfoService
{
    public IReadOnlyList<DriveSnapshot> GetDrives()
    {
        var result = new List<DriveSnapshot>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    continue;

                if (!drive.IsReady)
                {
                    result.Add(new DriveSnapshot(drive.Name, null, string.Empty, ByteSize.Zero, ByteSize.Zero, false));
                    continue;
                }

                result.Add(new DriveSnapshot(
                    drive.Name,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                    drive.DriveFormat,
                    new ByteSize(drive.TotalSize),
                    new ByteSize(drive.TotalFreeSpace),
                    true));
            }
            catch (IOException)
            {
                // A transient or unreadable drive should never break enumeration.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return result;
    }
}
