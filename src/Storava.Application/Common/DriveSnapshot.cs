using Storava.Domain.ValueObjects;

namespace Storava.Application.Common;

/// <summary>A point-in-time view of a logical drive's capacity.</summary>
public sealed record DriveSnapshot(
    string Name,
    string? VolumeLabel,
    string DriveFormat,
    ByteSize TotalSize,
    ByteSize FreeSpace,
    bool IsReady)
{
    public ByteSize UsedSpace => new(Math.Max(0, TotalSize.Bytes - FreeSpace.Bytes));

    public double UsedFraction => TotalSize.Bytes == 0
        ? 0
        : (double)UsedSpace.Bytes / TotalSize.Bytes;
}
