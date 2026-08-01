using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Migrations.Tests;

/// <summary>
/// The guard is the one thing standing between a plan and the user's disk, so each refusal it can
/// issue gets its own test. Anything it lets through will actually happen.
/// </summary>
public class ExecutionGuardTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeProtectedPaths _protected = new();
    private readonly ExecutionGuard _guard;

    public ExecutionGuardTests()
    {
        _guard = new ExecutionGuard(_protected, _fs);
        _fs.AddDirectory(@"D:\dev\node_modules", bytes: 1_000_000);
    }

    [Fact]
    public void ValidateSource_AcceptsAnOrdinaryFolder() =>
        Assert.True(_guard.ValidateSource(@"D:\dev\node_modules").IsSuccess);

    [Fact]
    public void ValidateSource_RefusesAProtectedPath()
    {
        _fs.AddDirectory(@"C:\Windows\System32", bytes: 1);
        var result = _guard.ValidateSource(@"C:\Windows\System32");

        Assert.True(result.IsFailure);
        Assert.Equal(ExecutionErrors.ProtectedPath.Code, result.Error.Code);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:")]
    public void ValidateSource_RefusesAVolumeRoot(string path)
    {
        // A drive root has no name to type back, and nothing about it is ever safe to act on.
        _fs.AddDirectory(path, bytes: 1);
        Assert.Equal(ExecutionErrors.ProtectedPath.Code, _guard.ValidateSource(path).Error.Code);
    }

    /// <summary>
    /// A refusal has to say what was concrete about it.
    /// <para>
    /// "This is a protected system location" is true of every such refusal and answers nothing: it
    /// does not say which item, nor how far the protection reaches. The complaint that arrived was
    /// exactly this — that a block did not say why — so the specifics are pinned here rather than
    /// left to drift back into the general sentence.
    /// </para>
    /// </summary>
    [Fact]
    public void ValidateSource_NamesTheProtectedRootItMatched()
    {
        var error = _guard.ValidateSource(@"C:\Windows\System32\drivers").Error;

        Assert.Contains(@"C:\Windows", error.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"System32", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSource_NamesThePathThatIsGone()
    {
        Assert.Contains(@"D:\gone", _guard.ValidateSource(@"D:\gone").Error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The two numbers are the point: how much is wanted, and how much there is.</summary>
    [Fact]
    public void ValidateDestination_SaysHowMuchRoomIsShort()
    {
        _fs.FreeSpaceByRoot[@"E:\"] = 1_000_000;

        var error = _guard.ValidateDestination(@"D:\dev\node_modules", @"E:\moved", 900_000_000).Error;

        Assert.Equal(ExecutionErrors.NotEnoughSpace.Code, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Detail));
        Assert.Contains("needed", error.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("free", error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The detail is part of the error, so it must be visible in its own string form.</summary>
    [Fact]
    public void AnErrorPrintsItsDetail()
    {
        var error = ExecutionErrors.NotEnoughSpace.With("12 MB needed, 3 MB free.");

        Assert.Contains("12 MB needed", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSource_RefusesAMissingFolder() =>
        Assert.Equal(ExecutionErrors.SourceMissing.Code, _guard.ValidateSource(@"D:\gone").Error.Code);

    [Fact]
    public void ValidateSource_RefusesAJunction()
    {
        // Acting on a link frees nothing and would copy whatever it points at.
        _fs.AddDirectory(@"D:\link", bytes: 5);
        _fs.ReparsePoints.Add(@"D:\link");

        Assert.Equal(ExecutionErrors.SourceIsLink.Code, _guard.ValidateSource(@"D:\link").Error.Code);
    }

    [Fact]
    public void ValidateDestination_AcceptsAnEmptyFolderOnAnotherDrive()
    {
        var result = _guard.ValidateDestination(@"D:\dev\node_modules", @"E:\moved\node_modules", 1_000_000);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateDestination_RefusesADestinationOnTheSameDrive()
    {
        var result = _guard.ValidateDestination(@"D:\dev\node_modules", @"D:\elsewhere\node_modules", 1_000);
        Assert.Equal(ExecutionErrors.DestinationSameVolume.Code, result.Error.Code);
    }

    [Fact]
    public void ValidateDestination_RefusesADestinationInsideTheSource()
    {
        var result = _guard.ValidateDestination(@"D:\dev", @"D:\dev\inner", 1_000);
        Assert.Equal(ExecutionErrors.DestinationInsideSource.Code, result.Error.Code);
    }

    [Fact]
    public void ValidateDestination_TreatsASiblingWithASharedPrefixAsSeparate()
    {
        // "D:\database" merely starts with "D:\data"; it is not inside it.
        var result = _guard.ValidateDestination(@"D:\data", @"E:\database", 1_000);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateDestination_RefusesAFolderThatAlreadyHasContent()
    {
        _fs.AddDirectory(@"E:\moved\node_modules", bytes: 10, files: 3);

        var result = _guard.ValidateDestination(@"D:\dev\node_modules", @"E:\moved\node_modules", 1_000);
        Assert.Equal(ExecutionErrors.DestinationNotEmpty.Code, result.Error.Code);
    }

    [Fact]
    public void ValidateDestination_DemandsHeadroomBeyondTheFolderSize()
    {
        // Exactly enough is not enough: filling the target volume is not an improvement.
        _fs.FreeSpaceByRoot[@"E:\"] = 1_000_000;

        var result = _guard.ValidateDestination(@"D:\dev\node_modules", @"E:\moved", 1_000_000);
        Assert.Equal(ExecutionErrors.NotEnoughSpace.Code, result.Error.Code);
    }

    [Fact]
    public void ValidateForExecution_RefusesWithoutAConfirmation()
    {
        var step = DeleteStep();
        Assert.Equal(ExecutionErrors.NotConfirmed.Code, _guard.ValidateForExecution(step, null, 1_000).Error.Code);
    }

    [Fact]
    public void ValidateForExecution_RefusesAnythingButTheApprovalWord()
    {
        var step = DeleteStep();
        var confirmation = new StepConfirmation
        {
            StepId = step.Id,
            Fingerprint = StepConfirmation.Compute(step),
            TypedName = "something else"
        };

        Assert.Equal(ExecutionErrors.NotConfirmed.Code, _guard.ValidateForExecution(step, confirmation, 1_000).Error.Code);
    }

    [Fact]
    public void ValidateForExecution_AcceptsTheApprovalWordRegardlessOfCase()
    {
        var step = DeleteStep();
        var confirmation = new StepConfirmation
        {
            StepId = step.Id,
            Fingerprint = StepConfirmation.Compute(step),
            TypedName = "approve"
        };

        Assert.True(_guard.ValidateForExecution(step, confirmation, 1_000).IsSuccess);
    }

    [Fact]
    public void ValidateForExecution_RefusesAConfirmationGivenForADifferentDestination()
    {
        var step = MoveStep(@"E:\moved\node_modules");
        var confirmation = new StepConfirmation
        {
            StepId = step.Id,
            Fingerprint = StepConfirmation.Compute(step),
            TypedName = ExecutionGuard.ApprovalWord
        };

        // The user approved one destination and then the step was pointed somewhere else.
        step.DestinationPath = @"F:\elsewhere\node_modules";

        Assert.Equal(ExecutionErrors.ConfirmationStale.Code, _guard.ValidateForExecution(step, confirmation, 1_000).Error.Code);
    }

    [Fact]
    public void ValidateForExecution_RefusesAConfirmationMintedForAnotherStep()
    {
        var step = DeleteStep();
        var other = DeleteStep();

        var confirmation = new StepConfirmation
        {
            StepId = other.Id,
            Fingerprint = StepConfirmation.Compute(other),
            TypedName = ExecutionGuard.ApprovalWord
        };

        Assert.Equal(ExecutionErrors.NotConfirmed.Code, _guard.ValidateForExecution(step, confirmation, 1_000).Error.Code);
    }

    [Theory]
    [InlineData(@"C:\dev\node_modules", "node_modules")]
    [InlineData(@"C:\dev\node_modules\", "node_modules")]
    [InlineData(@"C:\dev", "dev")]
    public void GetLeafName_ReturnsWhatTheUserHasToType(string path, string expected) =>
        Assert.Equal(expected, ExecutionGuard.GetLeafName(path));

    private static PlanExecutionStep DeleteStep() => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        ExecutionId = "run",
        PlanEntryId = "entry",
        ScanItemId = "item",
        SourcePath = @"D:\dev\node_modules",
        Title = "Node packages",
        Action = SuggestedAction.Delete
    };

    private static PlanExecutionStep MoveStep(string destination) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        ExecutionId = "run",
        PlanEntryId = "entry",
        ScanItemId = "item",
        SourcePath = @"D:\dev\node_modules",
        Title = "Node packages",
        Action = SuggestedAction.Move,
        Method = MigrationMethod.Junction,
        DestinationPath = destination
    };
}
