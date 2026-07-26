using Storava.Contracts.Workspace;
using Storava.Domain.Common;

namespace Storava.Application.Abstractions;

/// <summary>
/// Writes and reads portable <c>.storava</c> archives, so a scan can be kept, moved to another
/// machine, or reopened later to continue the analysis. Archives contain scan data only — never
/// settings and never the AI API key.
/// </summary>
public interface IWorkspaceArchiveService
{
    /// <summary>Writes one scan session, its items and its recommendations to <paramref name="filePath"/>.</summary>
    Task<Result<StoravaArchiveManifest>> ExportAsync(
        string sessionId,
        string filePath,
        string culture,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the manifest without importing, so the UI can describe the file first.</summary>
    Task<Result<StoravaArchiveManifest>> InspectAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archive into the local database. Re-importing the same archive replaces the
    /// previously imported copy rather than creating a duplicate.
    /// </summary>
    Task<Result<ArchiveImportResult>> ImportAsync(
        string filePath,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Progress while writing or reading an archive.</summary>
public readonly record struct ArchiveProgress(string Stage, long ItemsProcessed);

public sealed record ArchiveImportResult(
    string SessionId,
    string RootPath,
    int ItemCount,
    int RecommendationCount,
    DateTimeOffset ScanDate);

/// <summary>Stable error codes for archive problems, mapped to localized text in the UI.</summary>
public static class ArchiveErrors
{
    public static readonly Error NotFound = new("archive.not_found", "The archive was not found.");
    public static readonly Error NotAnArchive = new("archive.invalid", "That file is not a Storava archive.");
    public static readonly Error MissingEntry = new("archive.incomplete", "The archive is missing required data.");
    public static readonly Error HashMismatch = new("archive.tampered", "The archive failed its integrity check.");
    public static readonly Error UnsupportedVersion = new("archive.unsupported_version", "The archive was written by a newer version of Storava.");
    public static readonly Error SessionNotFound = new("archive.session_missing", "The scan to export was not found.");
    public static readonly Error WriteFailed = new("archive.write_failed", "The archive could not be written.");
    public static readonly Error ReadFailed = new("archive.read_failed", "The archive could not be read.");
}
