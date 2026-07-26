using Storava.Application.Abstractions;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Infrastructure.Tests;

/// <summary>
/// The execution log is the user's record of every change Storava made to their disk, and the
/// crash-recovery path reads it back. Both depend on a step row being written mid-operation and
/// surviving verbatim, which is what these tests pin down.
/// </summary>
public class PlanExecutionRepositoryTests
{
    private const string SessionId = "session-run";

    [Fact]
    public async Task Run_SurvivesAReloadWithEveryStepField()
    {
        using var host = new TestHost();
        var repository = host.Get<IPlanExecutionRepository>();

        var execution = NewExecution();
        var step = NewStep(execution.Id, order: 1);
        step.DestinationPath = @"E:\moved\node_modules";
        step.Status = ExecutionStatus.Completed;
        step.MeasuredBytes = 1_000_000;
        step.BytesFreed = 1_000_000;
        step.RecycledPath = @"D:\dev\node_modules";
        step.LinkPath = @"D:\dev\node_modules";
        step.ErrorCode = "exec.link_failed";
        step.ErrorMessage = "The link could not be created.";
        execution.Add(step);

        await repository.SaveAsync(execution);

        var reloaded = await repository.GetAsync(execution.Id);

        Assert.NotNull(reloaded);
        var loaded = Assert.Single(reloaded!.Steps);
        Assert.Equal(@"E:\moved\node_modules", loaded.DestinationPath);
        Assert.Equal(ExecutionStatus.Completed, loaded.Status);
        Assert.Equal(1_000_000, loaded.BytesFreed);
        Assert.Equal(@"D:\dev\node_modules", loaded.RecycledPath);
        Assert.Equal(@"D:\dev\node_modules", loaded.LinkPath);
        Assert.Equal("exec.link_failed", loaded.ErrorCode);
        Assert.Equal(1_000_000, reloaded.TotalBytesFreed);
    }

    [Fact]
    public async Task SaveStep_UpdatesOneRowWithoutRewritingTheRun()
    {
        using var host = new TestHost();
        var repository = host.Get<IPlanExecutionRepository>();

        var execution = NewExecution();
        var first = NewStep(execution.Id, order: 1);
        var second = NewStep(execution.Id, order: 2, path: @"D:\dev\.gradle");
        execution.Add(first);
        execution.Add(second);
        await repository.SaveAsync(execution);

        // This is the write that happens in the middle of a file operation.
        first.Status = ExecutionStatus.Running;
        await repository.SaveStepAsync(first);

        var reloaded = await repository.GetAsync(execution.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(ExecutionStatus.Running, reloaded!.Steps[0].Status);
        Assert.Equal(ExecutionStatus.Pending, reloaded.Steps[1].Status);
        // A row left running is exactly what the recovery path keys off after a crash.
        Assert.Same(reloaded.Steps[0], reloaded.StepNeedingRecovery);
    }

    [Fact]
    public async Task LatestForSession_ReturnsTheMostRecentRun()
    {
        using var host = new TestHost();
        var repository = host.Get<IPlanExecutionRepository>();

        var older = NewExecution(startedAt: DateTimeOffset.Now.AddHours(-2));
        older.Add(NewStep(older.Id, order: 1));
        await repository.SaveAsync(older);

        var newer = NewExecution();
        newer.Add(NewStep(newer.Id, order: 1));
        await repository.SaveAsync(newer);

        var latest = await repository.GetLatestForSessionAsync(SessionId);

        Assert.NotNull(latest);
        Assert.Equal(newer.Id, latest!.Id);
    }

    [Fact]
    public async Task Steps_ComeBackInPlanOrder()
    {
        using var host = new TestHost();
        var repository = host.Get<IPlanExecutionRepository>();

        var execution = NewExecution();
        // Added out of order on purpose: the plan's safest-first ordering must be what survives.
        execution.Add(NewStep(execution.Id, order: 3, path: @"D:\c"));
        execution.Add(NewStep(execution.Id, order: 1, path: @"D:\a"));
        execution.Add(NewStep(execution.Id, order: 2, path: @"D:\b"));
        await repository.SaveAsync(execution);

        var reloaded = await repository.GetAsync(execution.Id);

        Assert.NotNull(reloaded);
        Assert.Equal([@"D:\a", @"D:\b", @"D:\c"], reloaded!.Steps.Select(s => s.SourcePath));
    }

    private static PlanExecution NewExecution(DateTimeOffset? startedAt = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        PlanId = "plan",
        SessionId = SessionId,
        StartedAt = startedAt ?? DateTimeOffset.Now
    };

    private static PlanExecutionStep NewStep(string executionId, int order, string path = @"D:\dev\node_modules") => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        ExecutionId = executionId,
        PlanEntryId = Guid.NewGuid().ToString("n"),
        ScanItemId = Guid.NewGuid().ToString("n"),
        SourcePath = path,
        Title = "Node packages",
        Action = SuggestedAction.Move,
        Method = MigrationMethod.Junction,
        Order = order
    };
}
