using Storava.Agent.Scanning;
using Storava.Contracts.Agent;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.Agent.Tests;

/// <summary>
/// What a move leaves at the old location is the user's choice, not the Agent's.
/// <para>
/// The Agent used to always create a junction. That keeps every path pointing at the folder
/// working — a build that hard-codes it, a launcher, an old config file — which is usually what
/// somebody wants and sometimes exactly what they do not. The desktop edition has offered both for
/// a while; this is the same choice reaching the Agent.
/// </para>
/// </summary>
public class AgentMoveMethodTests
{
    [Fact]
    public void AskingForAJunctionLeavesOneBehind()
    {
        var method = AgentActionService.ResolveMoveMethod(AgentMoveMethods.Junction, SuggestedAction.Move);

        Assert.True(method.IsSuccess);
        Assert.Equal(MigrationMethod.Junction, method.Value);
    }

    [Fact]
    public void AskingForAPlainMoveLeavesNothingBehind()
    {
        var method = AgentActionService.ResolveMoveMethod(AgentMoveMethods.Copy, SuggestedAction.Move);

        Assert.True(method.IsSuccess);
        Assert.Equal(MigrationMethod.None, method.Value);
    }

    /// <summary>
    /// A page written before this was a choice sends nothing, and must keep behaving as it did.
    /// Silently switching those callers to a plain move would break paths on machines where
    /// nothing about the request changed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SayingNothingStillMeansAJunction(string? requested)
    {
        var method = AgentActionService.ResolveMoveMethod(requested, SuggestedAction.Move);

        Assert.True(method.IsSuccess);
        Assert.Equal(MigrationMethod.Junction, method.Value);
    }

    /// <summary>
    /// Refused rather than guessed. The two differ in whether every path to that folder keeps
    /// working, which is not something to decide for someone on the strength of a typo.
    /// </summary>
    [Theory]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    [InlineData("Junctionn")]
    public void AnUnrecognisedMethodIsRefused(string requested)
    {
        var method = AgentActionService.ResolveMoveMethod(requested, SuggestedAction.Move);

        Assert.True(method.IsFailure);
        Assert.Equal("unknown_move_method", method.Error.Code);
    }

    [Theory]
    [InlineData("junction")]
    [InlineData("JUNCTION")]
    [InlineData("Copy")]
    public void TheNameIsReadWithoutRegardToCase(string requested)
    {
        Assert.True(AgentActionService.ResolveMoveMethod(requested, SuggestedAction.Move).IsSuccess);
    }

    /// <summary>A delete has no old location to leave anything at, whatever was asked for.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("junction")]
    [InlineData("nonsense")]
    public void ADeleteIgnoresTheMoveMethodEntirely(string? requested)
    {
        var method = AgentActionService.ResolveMoveMethod(requested, SuggestedAction.Delete);

        Assert.True(method.IsSuccess);
        Assert.Equal(MigrationMethod.None, method.Value);
    }

    /// <summary>
    /// The approval a user gives is bound to what they were shown, and the method is part of that.
    /// Without this, an approval for "move it and leave a junction" could be spent on a move that
    /// leaves nothing — the same folder, the same destination, a different outcome.
    /// </summary>
    [Fact]
    public void TheApprovalFingerprintChangesWithTheMethod()
    {
        Assert.NotEqual(
            Storava.Migrations.StepConfirmation.Compute(Step(MigrationMethod.Junction)),
            Storava.Migrations.StepConfirmation.Compute(Step(MigrationMethod.None)));
    }

    private static PlanExecutionStep Step(MigrationMethod method) => new()
    {
        Id = "step-1",
        ExecutionId = "run-1",
        PlanEntryId = "agent",
        ScanItemId = "item-1",
        SourcePath = @"C:\Users\someone\packages",
        Title = "packages",
        Action = SuggestedAction.Move,
        Method = method,
        DestinationPath = @"D:\moved\packages"
    };
}
