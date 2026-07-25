using Storava.Domain.Enums;

namespace Storava.Application.Scanning;

/// <summary>Aggregated disk usage for one category within a scan.</summary>
public sealed record CategoryUsage(StorageCategory Category, long TotalSize, int ItemCount);
