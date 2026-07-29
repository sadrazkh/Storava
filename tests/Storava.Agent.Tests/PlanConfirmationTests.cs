using Storava.Domain.Entities;
using Storava.Domain.Enums;
using Storava.Migrations;

namespace Storava.Agent.Tests;

/// <summary>
/// One approval covering several folders has to be bound to exactly those folders.
/// <para>
/// A single step is approved by typing the folder's own name, which works because there is one
/// folder and its name is on screen. For a plan that gate does nothing — typing one name out of
/// twelve says nothing about the other eleven — so the plan is approved by a code derived from
/// every step in it. Everything below is about that code being impossible to spend on a plan other
/// than the one that was read.
/// </para>
/// </summary>
public class PlanConfirmationTests
{
    [Fact]
    public void TheSamePlanAlwaysProducesTheSameCode()
    {
        var first = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one"), Step("b", @"C:\two")]);
        var second = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one"), Step("b", @"C:\two")]);

        Assert.Equal(first, second);
        Assert.Equal(PlanConfirmation.ComputePhrase(first), PlanConfirmation.ComputePhrase(second));
    }

    [Fact]
    public void AddingAFolderChangesTheCode()
    {
        var before = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one")]);
        var after = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one"), Step("b", @"C:\two")]);

        Assert.NotEqual(PlanConfirmation.ComputePhrase(before), PlanConfirmation.ComputePhrase(after));
    }

    [Fact]
    public void RemovingAFolderChangesTheCode()
    {
        var before = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one"), Step("b", @"C:\two")]);
        var after = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one")]);

        Assert.NotEqual(PlanConfirmation.ComputePhrase(before), PlanConfirmation.ComputePhrase(after));
    }

    [Fact]
    public void ChangingADestinationChangesTheCode()
    {
        var before = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one", destination: @"D:\here")]);
        var after = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one", destination: @"E:\there")]);

        Assert.NotEqual(PlanConfirmation.ComputePhrase(before), PlanConfirmation.ComputePhrase(after));
    }

    /// <summary>
    /// Switching a move between a junction and a plain one is not a detail: one keeps every path
    /// pointing at the folder working and the other breaks them all. An approval read against one
    /// cannot be spent on the other.
    /// </summary>
    [Fact]
    public void ChangingHowAMoveIsDoneChangesTheCode()
    {
        var junction = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one", method: MigrationMethod.Junction)]);
        var plain = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one", method: MigrationMethod.None)]);

        Assert.NotEqual(PlanConfirmation.ComputePhrase(junction), PlanConfirmation.ComputePhrase(plain));
    }

    /// <summary>
    /// Order counts. A move that frees a drive before another move fills it is not the same plan as
    /// the reverse, so approving one is not approving the other.
    /// </summary>
    [Fact]
    public void ReorderingThePlanChangesTheCode()
    {
        var forwards = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one"), Step("b", @"C:\two")]);
        var backwards = PlanConfirmation.ComputeFingerprint([Step("b", @"C:\two"), Step("a", @"C:\one")]);

        Assert.NotEqual(forwards, backwards);
    }

    [Fact]
    public void TheCodeApprovesItsOwnPlanAndNothingElse()
    {
        var mine = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one")]);
        var other = PlanConfirmation.ComputeFingerprint([Step("b", @"C:\two")]);

        Assert.True(PlanConfirmation.Matches(mine, PlanConfirmation.ComputePhrase(mine)));
        Assert.False(PlanConfirmation.Matches(mine, PlanConfirmation.ComputePhrase(other)));
    }

    /// <summary>Read off a screen and retyped, so a shift key or a stray space is not a refusal.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CaseAndSurroundingSpaceAreForgiven(bool lower, bool padded)
    {
        var fingerprint = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one")]);
        var typed = PlanConfirmation.ComputePhrase(fingerprint);

        if (lower) typed = typed.ToLowerInvariant();
        if (padded) typed = $"  {typed} ";

        Assert.True(PlanConfirmation.Matches(fingerprint, typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEF")]
    public void NothingElseApprovesAPlan(string? typed)
    {
        var fingerprint = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one")]);

        // The last case can in principle be the real code; if it ever is, this catches it as a
        // failure rather than passing by luck.
        if (typed == PlanConfirmation.ComputePhrase(fingerprint))
            return;

        Assert.False(PlanConfirmation.Matches(fingerprint, typed));
    }

    /// <summary>
    /// The code is copied by eye, and characters that look alike in a sans-serif font turn a
    /// mistyped code into what reads as a refused approval — which invites trying harder rather
    /// than looking for the real problem.
    /// </summary>
    [Fact]
    public void TheCodeAvoidsCharactersThatLookAlike()
    {
        var seen = new HashSet<char>();

        for (var index = 0; index < 400; index++)
        {
            var fingerprint = PlanConfirmation.ComputeFingerprint([Step($"item-{index}", $@"C:\folder-{index}")]);
            foreach (var character in PlanConfirmation.ComputePhrase(fingerprint))
                seen.Add(character);
        }

        Assert.DoesNotContain('O', seen);
        Assert.DoesNotContain('0', seen);
        Assert.DoesNotContain('I', seen);
        Assert.DoesNotContain('l', seen);
        Assert.DoesNotContain('1', seen);
        Assert.DoesNotContain('S', seen);
        Assert.DoesNotContain('5', seen);
        Assert.DoesNotContain('Z', seen);
        Assert.DoesNotContain('2', seen);
    }

    [Fact]
    public void TheCodeIsShortEnoughToCopyByEye()
    {
        var fingerprint = PlanConfirmation.ComputeFingerprint([Step("a", @"C:\one")]);

        Assert.Equal(6, PlanConfirmation.ComputePhrase(fingerprint).Length);
    }

    private static PlanExecutionStep Step(
        string id,
        string path,
        string? destination = @"D:\moved",
        MigrationMethod method = MigrationMethod.Junction) => new()
    {
        Id = id,
        ExecutionId = "run-1",
        PlanEntryId = "agent-plan",
        ScanItemId = $"item-{id}",
        SourcePath = path,
        Title = path,
        Action = SuggestedAction.Move,
        Method = method,
        DestinationPath = destination
    };
}
