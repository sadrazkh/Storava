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
        Assert.Equal(ExecutionErrors.ProtectedPath, result.Error);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:")]
    public void ValidateSource_RefusesAVolumeRoot(string path)
    {
        // A drive root has no name to type back, and nothing about it is ever safe to act on.
        _fs.AddDirectory(path, bytes: 1);
        Assert.Equal(ExecutionErrors.ProtectedPath, _guard.ValidateSource(path).Error);
    }

    [Fact]
    public void ValidateSource_RefusesAMissingFolder() =>
        Assert.Equal(ExecutionErrors.SourceMissing, _guard.ValidateSource(@"D:\gone").Error);

    [Fact]
    public void ValidateSource_RefusesAJunction()
    {
        // Acting on a link frees nothing and would copy whatever it points at.
        _fs.AddDirectory(@"D:\link", bytes: 5);
        _fs.ReparsePoints.Add(@"D:\link");

        Assert.Equal(ExecutionErrors.SourceIsLink, _guard.ValidateSource(@"D:\link").Error);
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
        Assert.Equal(ExecutionErrors.DestinationSameVolume, result.Error);
    }

    [Fact]
    public void ValidateDestination_RefusesADestinationInsideTheSource()
    {
        var result = _guard.ValidateDestination(@"D:\dev", @"D:\dev\inner", 1_000);
        Assert.Equal(ExecutionErrors.DestinationInsideSource, result.Error);
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
        Assert.Equal(ExecutionErrors.DestinationNotEmpty, result.Error);
    }

    [Fact]
    public void ValidateDestination_DemandsHeadroomBeyondTheFolderSize()
    {
        // Exactly enough is not enough: filling the target volume is not an improvement.
        _fs.FreeSpaceByRoot[@"E:\"] = 1_000_000;

        var result = _guard.ValidateDestination(@"D:\dev\node_modules", @"E:\moved", 1_000_000);
        Assert.Equal(ExecutionErrors.NotEnoughSpace, result.Error);
    }

    [Fact]
    public void ValidateForExecution_RefusesWithoutAConfirmation()
    {
        var step = DeleteStep();
        Assert.Equal(ExecutionErrors.NotConfirmed, _guard.ValidateForExecution(step, null, 1_000).Error);
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

        Assert.Equal(ExecutionErrors.NotConfirmed, _guard.ValidateForExecution(step, confirmation, 1_000).Error);
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

        Assert.Equal(ExecutionErrors.ConfirmationStale, _guard.ValidateForExecution(step, confirmation, 1_000).Error);
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

        Assert.Equal(ExecutionErrors.NotConfirmed, _guard.ValidateForExecution(step, confirmation, 1_000).Error);
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
