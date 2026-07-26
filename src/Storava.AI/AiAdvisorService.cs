using Microsoft.Extensions.Logging;
using Storava.AI.Validation;
using Storava.Application.Abstractions;
using Storava.Contracts.Ai;
using Storava.Domain.Common;

namespace Storava.AI;

/// <summary>
/// Coordinates one advisory round: build the sanitized payload, let the user approve exactly
/// what would leave the machine, send it, then validate whatever comes back.
/// <para>
/// Nothing is transmitted without an approval token obtained from the payload the user actually
/// saw, and the token is bound to that payload's content — changing the settings or the scan
/// invalidates it.
/// </para>
/// </summary>
public sealed class AiAdvisorService
{
    private readonly AiPayloadBuilder _payloadBuilder;
    private readonly IAiProvider _provider;
    private readonly AiResponseValidator _validator;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ILogger<AiAdvisorService> _logger;

    public AiAdvisorService(
        AiPayloadBuilder payloadBuilder,
        IAiProvider provider,
        AiResponseValidator validator,
        ISettingsService settings,
        ISecretStore secrets,
        ILogger<AiAdvisorService> logger)
    {
        _payloadBuilder = payloadBuilder;
        _provider = provider;
        _validator = validator;
        _settings = settings;
        _secrets = secrets;
        _logger = logger;
    }

    public bool IsConfigured =>
        _settings.Current.Ai.Enabled && _secrets.Has(SecretNames.OpenRouterApiKey);

    /// <summary>
    /// Prepares the payload for review. Nothing has been sent at this point.
    /// </summary>
    public async Task<AiPreview> PrepareAsync(
        string sessionId,
        string language,
        double targetFreeSpaceGb,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current.Ai;
        var result = await _payloadBuilder
            .BuildAsync(sessionId, settings, language, targetFreeSpaceGb, cancellationToken)
            .ConfigureAwait(false);

        string rendered = PromptBuilder.RenderPayloadForPreview(result.Payload);

        // Last line of defence: if anything personal survived sanitisation, do not offer to send.
        bool leaksPersonalData = result.Sanitizer.ContainsPersonalData(rendered);
        if (leaksPersonalData)
            _logger.LogError("The prepared AI payload still contained personal data and was blocked.");

        return new AiPreview(
            sessionId,
            result.Payload,
            rendered,
            result.LocalPaths,
            IsSafeToSend: !leaksPersonalData);
    }

    /// <summary>
    /// Sends a payload the user has explicitly approved and validates the reply.
    /// </summary>
    /// <param name="approval">
    /// Must have been created from the same preview via <see cref="AiPreview.Approve"/>.
    /// </param>
    public async Task<Result<AiValidationResult>> AnalyzeAsync(
        AiApproval approval,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);

        var settings = _settings.Current.Ai;
        if (!settings.Enabled)
            return Result.Failure<AiValidationResult>(new Error("ai.disabled", "The AI advisor is turned off."));

        if (!approval.Preview.IsSafeToSend)
            return Result.Failure<AiValidationResult>(new Error("ai.unsafe_payload", "The payload was blocked by the privacy check."));

        if (!approval.Matches(approval.Preview))
            return Result.Failure<AiValidationResult>(new Error("ai.stale_approval", "The approved payload is no longer current."));

        string? apiKey = _secrets.Get(SecretNames.OpenRouterApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return Result.Failure<AiValidationResult>(new Error("ai.no_key", "No API key has been set."));

        _logger.LogInformation(
            "Sending AI request: model {Model}, {Candidates} candidates, {Unknown} unknown items.",
            settings.ModelName,
            approval.Preview.Payload.TopCandidates.Count,
            approval.Preview.Payload.UnknownItems.Count);

        var completion = await _provider
            .CompleteAsync(approval.Preview.Payload, settings, apiKey, language, cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
            return Result.Failure<AiValidationResult>(completion.Error);

        var validated = await _validator
            .ValidateAsync(
                approval.Preview.SessionId,
                completion.Value,
                approval.Preview.LocalPaths,
                settings.AllowReportGeneration,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "AI reply validated: {Accepted} accepted, {Rejected} rejected.",
            validated.Accepted.Count, validated.Rejected.Count);

        return Result.Success(validated);
    }
}

/// <summary>
/// The exact content that would be sent, ready for the user to inspect.
/// <see cref="LocalPaths"/> is local-only and is never part of the request.
/// </summary>
public sealed record AiPreview(
    string SessionId,
    AiRequestPayload Payload,
    string RenderedJson,
    IReadOnlyDictionary<string, string> LocalPaths,
    bool IsSafeToSend)
{
    /// <summary>Creates the approval token for this exact payload.</summary>
    public AiApproval Approve() => new(this, Fingerprint());

    internal string Fingerprint()
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(RenderedJson));
        return Convert.ToHexString(hash);
    }
}

/// <summary>
/// Proof that the user saw and approved a specific payload. It cannot be constructed without a
/// preview, so no code path can send data the user never reviewed.
/// </summary>
public sealed class AiApproval
{
    internal AiApproval(AiPreview preview, string fingerprint)
    {
        Preview = preview;
        Fingerprint = fingerprint;
        ApprovedAt = DateTimeOffset.UtcNow;
    }

    public AiPreview Preview { get; }

    public string Fingerprint { get; }

    public DateTimeOffset ApprovedAt { get; }

    /// <summary>True when this approval still matches the given preview's content.</summary>
    public bool Matches(AiPreview preview) =>
        preview is not null && string.Equals(Fingerprint, preview.Fingerprint(), StringComparison.Ordinal);
}
