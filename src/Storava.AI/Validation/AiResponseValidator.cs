using Microsoft.Extensions.Logging;
using Storava.Application.Abstractions;
using Storava.Contracts.Ai;
using Storava.Domain.Entities;
using Storava.Domain.Enums;

namespace Storava.AI.Validation;

/// <summary>
/// The security boundary between the AI and the rest of Storava.
/// <para>
/// Everything the model returns is treated as untrusted text. A suggestion is only kept when it
/// names a scan item that was actually in the payload, that item is not protected, and the
/// proposed action is one the local rules already permit for it. The real path is re-read from
/// the local database — any path the model wrote is ignored outright, so it cannot point Storava
/// at a location of its choosing. Accepted advice is still only advice: it is stored with
/// <see cref="SuggestedAction.NoAction"/> and carries no authority to change anything.
/// </para>
/// </summary>
public sealed class AiResponseValidator
{
    private const long OneGigabyte = 1024L * 1024 * 1024;

    private readonly IScanQueryService _query;
    private readonly IProtectedPathService _protectedPaths;
    private readonly ILogger<AiResponseValidator> _logger;

    public AiResponseValidator(
        IScanQueryService query,
        IProtectedPathService protectedPaths,
        ILogger<AiResponseValidator> logger)
    {
        _query = query;
        _protectedPaths = protectedPaths;
        _logger = logger;
    }

    /// <param name="includeReport">
    /// False when the user turned the narrative report off. The prompt already asks for it to be
    /// omitted; this drops it even if the model sends one anyway.
    /// </param>
    public async Task<AiValidationResult> ValidateAsync(
        string sessionId,
        AiResponse response,
        IReadOnlyDictionary<string, string> payloadItemIds,
        bool includeReport = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(payloadItemIds);

        var accepted = new List<Recommendation>();
        var rejected = new List<RejectedRecommendation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in response.Recommendations)
        {
            var outcome = await ValidateOneAsync(sessionId, candidate, payloadItemIds, seen, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Recommendation is not null)
            {
                accepted.Add(outcome.Recommendation);
                seen.Add(outcome.Recommendation.ScanItemId);
            }
            else
            {
                rejected.Add(new RejectedRecommendation(candidate.ScanItemId, candidate.Title, outcome.Reason));
            }
        }

        if (rejected.Count > 0)
        {
            // Log the reasons, never the model's raw text or any path.
            foreach (var group in rejected.GroupBy(r => r.Reason))
                _logger.LogWarning("Discarded {Count} AI suggestion(s): {Reason}.", group.Count(), group.Key);
        }

        var report = includeReport ? response.Report : null;

        return new AiValidationResult(
            accepted,
            rejected,
            Clean(response.Summary),
            Clean(response.MainCause),
            Clean(report?.Overview),
            CleanList(report?.Findings),
            CleanList(report?.NextSteps));
    }

    private async Task<(Recommendation? Recommendation, RejectionReason Reason)> ValidateOneAsync(
        string sessionId,
        AiRecommendation candidate,
        IReadOnlyDictionary<string, string> payloadItemIds,
        IReadOnlySet<string> seen,
        CancellationToken cancellationToken)
    {
        // 1. It must name an item that we actually sent.
        if (string.IsNullOrWhiteSpace(candidate.ScanItemId) || !payloadItemIds.ContainsKey(candidate.ScanItemId))
            return (null, RejectionReason.UnknownScanItem);

        if (seen.Contains(candidate.ScanItemId))
            return (null, RejectionReason.Duplicate);

        // 2. The item must still exist in this scan, and the path comes from our database.
        var item = await _query.GetByIdAsync(sessionId, candidate.ScanItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return (null, RejectionReason.UnknownScanItem);

        // 3. Protected locations are off limits, whatever the model says.
        if (item.IsProtected || item.RiskLevel == RiskLevel.Protected || _protectedPaths.IsProtected(item.Path))
            return (null, RejectionReason.ProtectedPath);

        // 4. The action must be one we recognise.
        if (!TryParseAction(candidate.ActionSuggestion, out var action))
            return (null, RejectionReason.UnknownAction);

        // 5. And one the local rules already allow for this item.
        if (action == SuggestedAction.Delete && !item.CanDelete)
            return (null, RejectionReason.ActionNotPermitted);
        if (action == SuggestedAction.Move && !item.CanMove)
            return (null, RejectionReason.ActionNotPermitted);

        // 6. The text must actually say something, and the numbers must be sane.
        string? reason = Clean(candidate.Reason);
        string? title = Clean(candidate.Title);
        if (reason is null || title is null)
            return (null, RejectionReason.InconsistentData);

        if (candidate.Confidence is < 0 or > 1)
            return (null, RejectionReason.InconsistentData);

        // A claim of freeing more than the item holds is not credible.
        double claimedBytes = candidate.EstimatedSpaceGb * OneGigabyte;
        if (claimedBytes < 0 || claimedBytes > item.Size * 1.05)
            return (null, RejectionReason.InconsistentData);

        var recommendation = new Recommendation
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            ScanItemId = item.Id,
            // Always the stored path, never anything the model produced.
            Path = item.Path,
            Title = title,
            Reason = reason,
            // Advice only: the user still chooses the action later.
            SuggestedAction = SuggestedAction.NoAction,
            RiskLevel = item.RiskLevel,
            Category = item.Category,
            Technology = item.DetectedTechnology,
            RuleId = item.KnownRuleId,
            EstimatedSpace = item.Size,
            Confidence = candidate.Confidence,
            Score = 0,
            CanDelete = item.CanDelete,
            CanMove = item.CanMove,
            CanRegenerate = item.CanRegenerate,
            Warning = candidate.Warnings.Count > 0 ? Clean(string.Join(" ", candidate.Warnings)) : null,
            Source = RecommendationSource.Ai
        };

        return (recommendation, default);
    }

    private static bool TryParseAction(string? value, out SuggestedAction action)
    {
        action = SuggestedAction.NoAction;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value.Trim(), ignoreCase: true, out action)
            && Enum.IsDefined(action);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Collapse whitespace and cap length so a runaway response cannot flood the UI.
        string cleaned = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length > 2000 ? cleaned[..2000] : cleaned;
    }

    private static IReadOnlyList<string> CleanList(List<string>? values)
    {
        if (values is null || values.Count == 0)
            return [];

        return values
            .Select(Clean)
            .Where(v => v is not null)
            .Select(v => v!)
            .Take(20)
            .ToArray();
    }
}
