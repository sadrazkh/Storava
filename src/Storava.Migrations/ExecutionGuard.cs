using Storava.Application.Abstractions;
using Storava.Domain.Common;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Migrations;

/// <summary>
/// The security boundary for anything that touches the disk. Every check here runs again
/// immediately before the step executes, not only when the plan was drafted: a path can become
/// protected, a folder can be replaced by a junction, and a destination can fill up in the minutes
/// between planning and acting.
/// <para>
/// The guard is deliberately paranoid and deliberately dumb — it answers one question, "may this
/// exact step run right now", and it has no way to perform the step itself.
/// </para>
/// </summary>
public sealed class ExecutionGuard
{
    /// <summary>
    /// Spare room demanded at the destination on top of the folder's own size. A copy that fills
    /// the target volume to the last byte leaves the machine in a worse state than it started.
    /// </summary>
    private const double FreeSpaceHeadroom = 0.05;

    /// <summary>
    /// What has to be typed to approve a step.
    /// <para>
    /// This used to be the source folder's own name. The reasoning was that typing the name proves
    /// you read which folder it is — but in practice the names are long, and a gate people cannot
    /// get through is not a safety feature, it is a wall. What actually stops a reflexive click is
    /// having to type anything at all into an empty box, and that works with a short word.
    /// </para>
    /// <para>
    /// English on purpose, in both languages: it is a fixed token rather than prose, and a
    /// translated one would mean the same approval reads differently depending on a setting. What
    /// is being confirmed is still bound to the step by <see cref="StepConfirmation.Fingerprint"/>,
    /// which is the check that stops an approval being reused for a different act — that has not
    /// been relaxed and is the one that matters.
    /// </para>
    /// </summary>
    public const string ApprovalWord = "APPROVE";

    private readonly IProtectedPathService _protectedPaths;
    private readonly IFileSystemActions _fileSystem;

    public ExecutionGuard(IProtectedPathService protectedPaths, IFileSystemActions fileSystem)
    {
        _protectedPaths = protectedPaths;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Checks the source alone. Split out because it is also what preflight runs before a
    /// destination has been chosen.
    /// </summary>
    public Result ValidateSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Result.Failure(ExecutionErrors.SourceMissing);

        // A volume root has no name to type back and nothing above it; treat it as protected.
        if (IsVolumeRoot(sourcePath) || _protectedPaths.IsProtected(sourcePath))
            return Result.Failure(ExecutionErrors.ProtectedPath);

        // Either kind: what the user picked out of a scan can be a single large file just as
        // easily as a folder, and refusing files here is what made half a disk unactionable.
        if (!_fileSystem.Exists(sourcePath))
            return Result.Failure(ExecutionErrors.SourceMissing);

        // Acting on a link would either free nothing (delete) or copy someone else's data (move).
        if (_fileSystem.IsReparsePoint(sourcePath))
            return Result.Failure(ExecutionErrors.SourceIsLink);

        return Result.Success();
    }

    /// <summary>
    /// Checks the destination of a move, given how much the source actually measures now.
    /// <paramref name="measuredBytes"/> comes from a fresh walk, never from the scan record.
    /// </summary>
    public Result ValidateDestination(string sourcePath, string? destinationPath, long measuredBytes)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return Result.Failure(ExecutionErrors.DestinationRequired);

        if (_protectedPaths.IsProtected(destinationPath))
            return Result.Failure(ExecutionErrors.ProtectedPath);

        // Writing the copy into the folder being copied would recurse forever.
        if (IsSameOrUnder(destinationPath, sourcePath))
            return Result.Failure(ExecutionErrors.DestinationInsideSource);

        var sourceVolume = _fileSystem.GetVolumeRoot(sourcePath);
        var destinationVolume = _fileSystem.GetVolumeRoot(destinationPath);

        if (sourceVolume.IsFailure || destinationVolume.IsFailure)
            return Result.Failure(ExecutionErrors.DestinationInvalid);

        // The whole point of a move is to free space on the source drive.
        if (string.Equals(sourceVolume.Value, destinationVolume.Value, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(ExecutionErrors.DestinationSameVolume);

        // Merging into a folder that already holds data would mix two trees together and make the
        // "copy matches the original" check meaningless. A destination that is already a file is
        // refused for the same reason: writing over it would destroy something.
        if (_fileSystem.DirectoryExists(destinationPath) && !_fileSystem.IsEmptyDirectory(destinationPath))
            return Result.Failure(ExecutionErrors.DestinationNotEmpty);

        if (!_fileSystem.DirectoryExists(destinationPath) && _fileSystem.Exists(destinationPath))
            return Result.Failure(ExecutionErrors.DestinationNotEmpty);

        var freeSpace = _fileSystem.GetAvailableFreeSpace(destinationPath);
        if (freeSpace.IsFailure)
            return Result.Failure(ExecutionErrors.DestinationInvalid);

        long required = measuredBytes + (long)(measuredBytes * FreeSpaceHeadroom);
        if (freeSpace.Value < required)
            return Result.Failure(ExecutionErrors.NotEnoughSpace);

        return Result.Success();
    }

    /// <summary>
    /// The final gate, called immediately before the first byte moves. It repeats the source and
    /// destination checks on purpose: preflight may have run minutes ago.
    /// </summary>
    public Result ValidateForExecution(PlanExecutionStep step, StepConfirmation? confirmation, long measuredBytes)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Action is not (SuggestedAction.Move or SuggestedAction.Delete))
            return Result.Failure(ExecutionErrors.ActionNotPermitted);

        if (confirmation is null || !string.Equals(confirmation.StepId, step.Id, StringComparison.Ordinal))
            return Result.Failure(ExecutionErrors.NotConfirmed);

        // Bound to what was on screen: a changed destination produces a different fingerprint.
        if (!string.Equals(confirmation.Fingerprint, StepConfirmation.Compute(step), StringComparison.Ordinal))
            return Result.Failure(ExecutionErrors.ConfirmationStale);

        if (!string.Equals(confirmation.TypedName?.Trim(), ApprovalWord, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(ExecutionErrors.NotConfirmed);

        var source = ValidateSource(step.SourcePath);
        if (source.IsFailure)
            return source;

        if (step.Action == SuggestedAction.Move)
        {
            var destination = ValidateDestination(step.SourcePath, step.DestinationPath, measuredBytes);
            if (destination.IsFailure)
                return destination;
        }

        return Result.Success();
    }

    /// <summary>The exact text the user has to type to confirm a step.</summary>
    public static string GetLeafName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.TrimEnd('\\', '/');
        int separator = trimmed.LastIndexOfAny(['\\', '/']);
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }

    private static bool IsVolumeRoot(string path)
    {
        var trimmed = path.Trim().TrimEnd('\\', '/');
        // "C:" once the trailing separator is gone, or a bare UNC server share.
        return trimmed.Length <= 2 || trimmed.EndsWith(':');
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="ancestor"/> or sits inside it.</summary>
    private static bool IsSameOrUnder(string candidate, string ancestor)
    {
        var a = candidate.Trim().TrimEnd('\\', '/');
        var b = ancestor.Trim().TrimEnd('\\', '/');

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!a.StartsWith(b, StringComparison.OrdinalIgnoreCase))
            return false;

        // "C:\data" must not count as an ancestor of "C:\database".
        return a.Length > b.Length && (a[b.Length] is '\\' or '/');
    }
}
