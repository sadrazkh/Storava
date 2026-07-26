using Microsoft.Extensions.Logging.Abstractions;
using Storava.AI.Validation;
using Storava.Contracts.Ai;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.AI.Tests;

/// <summary>
/// These tests define the boundary between the model and the rest of Storava. Everything the AI
/// returns is untrusted; only suggestions that survive every check may reach the user.
/// </summary>
public class AiResponseValidatorTests
{
    private const string SessionId = "session-1";

    private static AiResponseValidator Create(FakeScanQueryService query) =>
        new(query, new FakeProtectedPaths(), NullLogger<AiResponseValidator>.Instance);

    private static AiRecommendation Recommendation(
        string? id = "item-1",
        string? action = "Move",
        string? title = "Move the NuGet cache",
        string? reason = "It is large and rebuilt on demand.",
        double confidence = 0.9,
        double estimatedGb = 4) => new()
        {
            ScanItemId = id,
            ActionSuggestion = action,
            Title = title,
            Reason = reason,
            Risk = "Low",
            Confidence = confidence,
            EstimatedSpaceGb = estimatedGb
        };

    private static AiResponse ResponseWith(params AiRecommendation[] recommendations) => new()
    {
        Summary = "The disk is mostly developer caches.",
        MainCause = "Package caches",
        Recommendations = [.. recommendations]
    };

    [Fact]
    public async Task Accepts_AWellFormedSuggestion()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = @"C:\Users\a\.nuget\packages" };

        var result = await Create(query).ValidateAsync(SessionId, ResponseWith(Recommendation()), payloadIds);

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal("item-1", accepted.ScanItemId);
        Assert.Equal(RecommendationSource.Ai, accepted.Source);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public async Task AcceptedAdvice_IsStillOnlyAdvice()
    {
        // The AI proposing "Delete" must never translate into a pre-selected action.
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(action: "Delete")), payloadIds);

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal(SuggestedAction.NoAction, accepted.SuggestedAction);
    }

    [Fact]
    public async Task Rejects_AnIdThatWasNeverSent()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(id: "item-999")), payloadIds);

        Assert.Empty(result.Accepted);
        Assert.Equal(RejectionReason.UnknownScanItem, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_AMissingId()
    {
        var query = new FakeScanQueryService();
        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(id: null)), new Dictionary<string, string>());

        Assert.Equal(RejectionReason.UnknownScanItem, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_AProtectedLocation()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Windows\System32", isProtected: true, risk: RiskLevel.Protected);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(SessionId, ResponseWith(Recommendation()), payloadIds);

        Assert.Empty(result.Accepted);
        Assert.Equal(RejectionReason.ProtectedPath, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_AProtectedPathEvenIfTheScanRowLooksActionable()
    {
        // Defence in depth: the path service has the final say, not the stored flags.
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Program Files\App", isProtected: false, risk: RiskLevel.Low);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(SessionId, ResponseWith(Recommendation()), payloadIds);

        Assert.Equal(RejectionReason.ProtectedPath, Assert.Single(result.Rejected).Reason);
    }

    [Theory]
    [InlineData("Format")]
    [InlineData("RunCommand")]
    [InlineData("rm -rf")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Rejects_AnUnknownAction(string? action)
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(action: action)), payloadIds);

        Assert.Equal(RejectionReason.UnknownAction, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_DeleteWhenTheRulesDoNotAllowIt()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"D:\src\proj\.git", canDelete: false, canMove: false);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(action: "Delete")), payloadIds);

        Assert.Equal(RejectionReason.ActionNotPermitted, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_MoveWhenTheRulesDoNotAllowIt()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"D:\src\proj\bin", canMove: false);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(action: "Move")), payloadIds);

        Assert.Equal(RejectionReason.ActionNotPermitted, Assert.Single(result.Rejected).Reason);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public async Task Rejects_ImpossibleConfidence(double confidence)
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(confidence: confidence)), payloadIds);

        Assert.Equal(RejectionReason.InconsistentData, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_AnInflatedSpaceClaim()
    {
        // The item holds 5 GB; claiming 500 GB is not credible.
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages", size: 5L * 1024 * 1024 * 1024);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(estimatedGb: 500)), payloadIds);

        Assert.Equal(RejectionReason.InconsistentData, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_EmptyText()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(reason: "   ")), payloadIds);

        Assert.Equal(RejectionReason.InconsistentData, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task Rejects_DuplicateSuggestionsForTheSameItem()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(), Recommendation()), payloadIds);

        Assert.Single(result.Accepted);
        Assert.Equal(RejectionReason.Duplicate, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public async Task UsesTheStoredPath_NotAnythingTheModelWrote()
    {
        // The model cannot point Storava at a path of its choosing: paths come from the scan.
        var query = new FakeScanQueryService();
        query.Add("item-1", @"D:\projects\app\node_modules");
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(SessionId, ResponseWith(Recommendation()), payloadIds);

        Assert.Equal(@"D:\projects\app\node_modules", Assert.Single(result.Accepted).Path);
    }

    [Fact]
    public async Task UsesTheStoredSize_NotTheModelsEstimate()
    {
        var query = new FakeScanQueryService();
        var item = query.Add("item-1", @"C:\Users\a\.nuget\packages", size: 3L * 1024 * 1024 * 1024);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x" };

        var result = await Create(query).ValidateAsync(
            SessionId, ResponseWith(Recommendation(estimatedGb: 2.5)), payloadIds);

        Assert.Equal(item.Size, Assert.Single(result.Accepted).EstimatedSpace);
    }

    [Fact]
    public async Task KeepsGoodSuggestionsWhenOthersAreRejected()
    {
        var query = new FakeScanQueryService();
        query.Add("item-1", @"C:\Users\a\.nuget\packages");
        query.Add("item-2", @"C:\Windows\System32", isProtected: true);
        var payloadIds = new Dictionary<string, string> { ["item-1"] = "x", ["item-2"] = "y" };

        var result = await Create(query).ValidateAsync(
            SessionId,
            ResponseWith(
                Recommendation(id: "item-2"),
                Recommendation(id: "item-1"),
                Recommendation(id: "ghost")),
            payloadIds);

        Assert.Single(result.Accepted);
        Assert.Equal(2, result.Rejected.Count);
    }

    [Fact]
    public async Task CarriesNarrativeSectionsThrough()
    {
        var query = new FakeScanQueryService();
        var response = new AiResponse
        {
            Summary = "  Mostly caches.  ",
            MainCause = "Build artifacts",
            Report = new AiReportSection
            {
                Overview = "Overview text",
                Findings = ["Finding one", "  ", "Finding two"],
                NextSteps = ["Step one"]
            }
        };

        var result = await Create(query).ValidateAsync(SessionId, response, new Dictionary<string, string>());

        Assert.Equal("Mostly caches.", result.Summary);
        Assert.Equal("Build artifacts", result.MainCause);
        Assert.Equal(["Finding one", "Finding two"], result.Findings);
        Assert.Equal(["Step one"], result.NextSteps);
    }

    [Fact]
    public async Task DropsTheNarrativeSection_WhenTheUserTurnedReportsOff()
    {
        var query = new FakeScanQueryService();
        var response = new AiResponse
        {
            Summary = "Mostly caches.",
            // The prompt asks for no report, but a model may send one anyway.
            Report = new AiReportSection
            {
                Overview = "Overview text",
                Findings = ["Finding one"],
                NextSteps = ["Step one"]
            }
        };

        var result = await Create(query)
            .ValidateAsync(SessionId, response, new Dictionary<string, string>(), includeReport: false);

        Assert.Null(result.Overview);
        Assert.Empty(result.Findings);
        Assert.Empty(result.NextSteps);
        Assert.Equal("Mostly caches.", result.Summary);
    }

    [Fact]
    public async Task HandlesAnEmptyResponseGracefully()
    {
        var query = new FakeScanQueryService();

        var result = await Create(query).ValidateAsync(SessionId, new AiResponse(), new Dictionary<string, string>());

        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
        Assert.False(result.HasContent);
    }
}
