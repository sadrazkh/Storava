namespace Storava.Application.Migration;

/// <summary>
/// What a folder actually contains right now, measured immediately before or after a copy.
/// The plan's stored size came from the scan and may be hours old; these numbers are the ones a
/// move is verified against.
/// </summary>
/// <param name="LinkCount">
/// Junctions and symbolic links found inside the tree. They are never followed and never
/// recreated, so a non-zero count is something the user is told about before a move.
/// </param>
public readonly record struct DirectoryFacts(long Bytes, long FileCount, long FolderCount, long LinkCount = 0)
{
    public static readonly DirectoryFacts Empty = new(0, 0, 0);

    /// <summary>
    /// Whether a copy can be treated as faithful. File count and total bytes must both match:
    /// bytes alone would accept a truncated file paired with an extra one, and counts alone would
    /// accept files that copied as zero bytes.
    /// </summary>
    public bool Matches(DirectoryFacts other) =>
        Bytes == other.Bytes && FileCount == other.FileCount;
}

/// <summary>Progress of a long copy, reported often enough to keep the UI honest.</summary>
public readonly record struct CopyProgress(long BytesCopied, long TotalBytes, string CurrentPath)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesCopied / TotalBytes, 0, 1);
}
