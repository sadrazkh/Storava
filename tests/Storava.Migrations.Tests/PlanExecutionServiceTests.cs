using Microsoft.Extensions.Logging.Abstractions;
using Storava.Application.Migration;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Migrations.Tests;

/// <summary>
/// Covers the order of operations, which is what makes an interrupted run survivable. The move
/// tests in particular assert that the original is only ever removed *after* a verified copy
/// exists — if that inverts, a crash in the middle destroys the user's data.
/// </summary>
public class PlanExecutionServiceTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly FakeProtectedPaths _protected = new();
    private readonly InMemoryExecutionRepository _repository = new();
    private readonly PlanExecutionService _service;

    public PlanExecutionServiceTests()
    {
        _service = new PlanExecutionService(
            new ExecutionGuard(_protected, _fs),
            _fs,
            _repository,
            NullLogger<PlanExecutionService>.Instance);

        _fs.AddDirectory(@"D:\dev\node_modules", bytes: 1_000_000, files: 40);
    }

    [Fact]
    public async Task Preflight_ReportsMeasuredSizeRatherThanTheScannedOne()
    {
        // The plan says 500 KB; the folder is 1 MB now. The number the user acts on must be current.
        var plan = PlanWith(Entry(SuggestedAction.Delete, estimated: 500_000));

        var report = await _service.PreflightAsync(plan);

        Assert.Equal(1_000_000, report.ReclaimableBytes);
        Assert.Contains(report.Steps[0].Warnings, w => w.Code == "preflight.grew");
    }

    [Fact]
    public async Task Preflight_BlocksAStepWhoseFolderIsGone()
    {
        var plan = PlanWith(Entry(SuggestedAction.Delete, path: @"D:\vanished"));

        var report = await _service.PreflightAsync(plan);

        Assert.False(report.Steps[0].CanRun);
        Assert.Equal(ExecutionErrors.SourceMissing, report.Steps[0].Blocker);
        Assert.False(report.HasAnythingToDo);
    }

    [Fact]
    public async Task CreateExecution_RecordsBlockedStepsAsSkippedRatherThanDroppingThem()
    {
        var plan = PlanWith(
            Entry(SuggestedAction.Delete),
            Entry(SuggestedAction.Delete, path: @"D:\vanished"));

        var report = await _service.PreflightAsync(plan);
        var execution = await _service.CreateExecutionAsync(plan, report);

        Assert.Equal(2, execution.Steps.Count);
        Assert.Equal(1, execution.SkippedCount);
        Assert.Single(execution.Steps.Where(s => s.Status == ExecutionStatus.Pending));
    }

    [Fact]
    public async Task Delete_SendsTheFolderToTheRecycleBinAndNeverDeletesOutright()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Delete));

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutionStatus.Completed, step.Status);
        Assert.Equal(1_000_000, step.BytesFreed);
        Assert.Equal([$"recycle:{step.SourcePath}"], _fs.Operations);
        Assert.Contains(step.SourcePath, _fs.Recycled);
    }

    [Fact]
    public async Task Delete_LeavesTheFolderAloneWhenTheRecycleBinRefuses()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Delete));
        _fs.RecycleFailures.Add(step.SourcePath);

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        Assert.True(result.IsFailure);
        Assert.Equal(ExecutionStatus.Failed, step.Status);
        Assert.Equal(0, step.BytesFreed);
        Assert.True(_fs.DirectoryExists(step.SourcePath));
        // Nothing may claim the folder was recycled when it was not.
        Assert.Null(step.RecycledPath);
    }

    [Fact]
    public async Task Move_CopiesAndVerifiesBeforeTheOriginalIsTouched()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutionStatus.Completed, step.Status);

        // The order is the guarantee: copy, then recycle the original, then leave the link.
        Assert.Equal(
            [
                @"copy:D:\dev\node_modules->E:\moved\node_modules",
                @"recycle:D:\dev\node_modules",
                @"link:D:\dev\node_modules->E:\moved\node_modules"
            ],
            _fs.Operations);

        Assert.Equal(1_000_000, step.BytesFreed);
        Assert.Equal(@"D:\dev\node_modules", step.LinkPath);
    }

    [Fact]
    public async Task Move_DiscardsTheCopyAndKeepsTheOriginalWhenVerificationFails()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";

        // The copy lands short — the exact case that must never lead to a deletion.
        _fs.CopyResultOverride = new DirectoryFacts(900_000, 38, 0);

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        Assert.True(result.IsFailure);
        Assert.Equal(ExecutionErrors.VerificationFailed, result.Error);
        Assert.Equal(ExecutionStatus.RolledBack, step.Status);
        Assert.True(_fs.DirectoryExists(@"D:\dev\node_modules"));
        Assert.Contains(@"E:\moved\node_modules", _fs.Recycled);
        Assert.Equal(0, step.BytesFreed);
    }

    [Fact]
    public async Task Move_PutsEverythingBackWhenTheOriginalCannotBeRecycled()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";
        _fs.RecycleFailures.Add(@"D:\dev\node_modules");

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        Assert.Equal(ExecutionStatus.RolledBack, step.Status);
        Assert.True(_fs.DirectoryExists(@"D:\dev\node_modules"));
        Assert.False(_fs.DirectoryExists(@"E:\moved\node_modules"));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Move_StaysCompletedWhenOnlyTheLinkFails()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";
        _fs.FailLink = true;

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        // The space was freed and the data is safe, so calling this a failure would misinform.
        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutionStatus.Completed, step.Status);
        Assert.Equal(1_000_000, step.BytesFreed);
        Assert.Equal("exec.link_failed", step.ErrorCode);
        Assert.Null(step.LinkPath);
    }

    [Fact]
    public async Task Move_KeepsTheOriginalWhenTheCopyFails()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";
        _fs.FailCopy = true;

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        Assert.True(result.IsFailure);
        Assert.Equal(ExecutionStatus.RolledBack, step.Status);
        Assert.True(_fs.DirectoryExists(@"D:\dev\node_modules"));
    }

    [Fact]
    public async Task Move_CleansUpThePartialCopyWhenTheUserStopsIt()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";
        _fs.CancelDuringCopy = true;

        var result = await _service.ExecuteStepAsync(execution, step, Confirm(step));

        // A cancelled copy leaves half a tree behind; leaving it there would waste the space the
        // user was trying to reclaim, and it must never be mistaken for a completed move.
        Assert.Equal(ExecutionErrors.Cancelled, result.Error);
        Assert.Equal(ExecutionStatus.RolledBack, step.Status);
        Assert.Contains(@"E:\moved\node_modules", _fs.Recycled);
        Assert.True(_fs.DirectoryExists(@"D:\dev\node_modules"));
    }

    [Fact]
    public async Task ExecuteStep_PersistsTheRunningRowBeforeTouchingTheDisk()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Delete));

        await _service.ExecuteStepAsync(execution, step, Confirm(step));

        // Without this write first, a crash mid-operation would leave no trace to recover from.
        Assert.Equal(ExecutionStatus.Running, _repository.StepWrites[0].Status);
        Assert.Equal(ExecutionStatus.Completed, _repository.StepWrites[^1].Status);
    }

    [Fact]
    public async Task ExecuteStep_LeavesAnUnconfirmedStepPendingSoItCanBeRetried()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Delete));

        var result = await _service.ExecuteStepAsync(execution, step, confirmation: new StepConfirmation
        {
            StepId = step.Id,
            Fingerprint = StepConfirmation.Compute(step),
            TypedName = "wrong"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ExecutionStatus.Pending, step.Status);
        Assert.Empty(_fs.Operations);
    }

    [Fact]
    public async Task Begin_RefusesASecondStepWhileOneIsStillRunning()
    {
        var plan = PlanWith(Entry(SuggestedAction.Delete), Entry(SuggestedAction.Delete, path: @"D:\other"));
        _fs.AddDirectory(@"D:\other", bytes: 5);

        var report = await _service.PreflightAsync(plan);
        var execution = await _service.CreateExecutionAsync(plan, report);

        var first = execution.Steps[0];
        Assert.True(execution.Begin(first).IsSuccess);

        var second = execution.Steps[1];
        Assert.Equal(ExecutionErrors.AnotherStepRunning, execution.Begin(second).Error);
    }

    [Fact]
    public async Task Recover_TreatsAMoveWithBothPathsPresentAsNeverHavingFinished()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";
        _fs.AddDirectory(@"E:\moved\node_modules", bytes: 400_000);
        execution.Begin(step);

        await _service.RecoverAsync(execution, step);

        // Both present means the original was never removed, so the copy is the leftover.
        Assert.Equal(ExecutionStatus.RolledBack, step.Status);
        Assert.Contains(@"E:\moved\node_modules", _fs.Recycled);
        Assert.True(_fs.DirectoryExists(@"D:\dev\node_modules"));
    }

    [Fact]
    public async Task Recover_TreatsAMoveWithOnlyTheDestinationPresentAsDone()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Move));
        step.DestinationPath = @"E:\moved\node_modules";
        step.MeasuredBytes = 1_000_000;
        execution.Begin(step);

        _fs.Directories.Remove(@"D:\dev\node_modules");
        _fs.AddDirectory(@"E:\moved\node_modules", bytes: 1_000_000);

        await _service.RecoverAsync(execution, step);

        // The source is only ever removed after verification, so this move did complete.
        Assert.Equal(ExecutionStatus.Completed, step.Status);
        Assert.Equal(1_000_000, step.BytesFreed);
    }

    [Fact]
    public async Task Recover_TreatsADeleteWithTheFolderGoneAsDone()
    {
        var (execution, step) = await StartAsync(Entry(SuggestedAction.Delete));
        step.MeasuredBytes = 1_000_000;
        execution.Begin(step);
        _fs.Directories.Remove(@"D:\dev\node_modules");

        await _service.RecoverAsync(execution, step);

        Assert.Equal(ExecutionStatus.Completed, step.Status);
        Assert.Equal(1_000_000, step.BytesFreed);
    }

    private async Task<(PlanExecution Execution, PlanExecutionStep Step)> StartAsync(StoragePlanEntry entry)
    {
        var plan = PlanWith(entry);
        var report = await _service.PreflightAsync(plan);
        var execution = await _service.CreateExecutionAsync(plan, report);
        return (execution, execution.Steps[0]);
    }

    private static StepConfirmation Confirm(PlanExecutionStep step) => new()
    {
        StepId = step.Id,
        Fingerprint = StepConfirmation.Compute(step),
        TypedName = ExecutionGuard.GetLeafName(step.SourcePath)
    };

    private static StoragePlan PlanWith(params StoragePlanEntry[] entries)
    {
        var plan = new StoragePlan { Id = "plan", SessionId = "session" };
        plan.Load(entries);
        return plan;
    }

    private static StoragePlanEntry Entry(
        SuggestedAction action,
        string path = @"D:\dev\node_modules",
        long estimated = 1_000_000) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        PlanId = "plan",
        RecommendationId = "rec",
        ScanItemId = Guid.NewGuid().ToString("n"),
        Path = path,
        Title = "Node packages",
        Action = action,
        EstimatedSpace = estimated,
        RiskLevel = RiskLevel.Low,
        Method = action == SuggestedAction.Move ? MigrationMethod.Junction : MigrationMethod.None
    };
}
