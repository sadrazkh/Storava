using Storava.Application.Settings;
using Storava.Contracts.Ai;
using Storava.Domain.Common;

namespace Storava.AI;

/// <summary>
/// A chat-completion provider. Abstracted so OpenRouter is not the only possible backend and so
/// tests can run the whole pipeline without touching the network.
/// </summary>
public interface IAiProvider
{
    string Id { get; }

    /// <summary>
    /// Sends the sanitized payload and returns the parsed response. Failures come back as a
    /// failed <see cref="Result{T}"/> rather than an exception, since a provider being
    /// unavailable is an expected condition, not a bug.
    /// </summary>
    Task<Result<AiResponse>> CompleteAsync(
        AiRequestPayload payload,
        AiSettings settings,
        string apiKey,
        string language,
        CancellationToken cancellationToken = default);
}
