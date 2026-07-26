using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Storava.Application.Settings;
using Storava.Contracts.Ai;
using Storava.Domain.Common;

namespace Storava.AI.OpenRouter;

/// <summary>
/// Talks to the OpenRouter chat-completions API. The API key is passed per call and is never
/// stored, logged or echoed back — not even in error messages.
/// </summary>
public sealed class OpenRouterProvider : IAiProvider
{
    public const string HttpClientName = "storava-openrouter";

    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenRouterProvider> _logger;

    public OpenRouterProvider(IHttpClientFactory httpClientFactory, ILogger<OpenRouterProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Id => "openrouter";

    public async Task<Result<AiResponse>> CompleteAsync(
        AiRequestPayload payload,
        AiSettings settings,
        string apiKey,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(apiKey))
            return Result.Failure<AiResponse>(new Error("ai.no_key", "No API key has been set."));

        var request = new
        {
            model = settings.ModelName,
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = PromptBuilder.BuildSystemPrompt(language, settings.AllowReportGeneration) },
                new { role = "user", content = PromptBuilder.BuildUserPrompt(payload) }
            }
        };

        int attempts = Math.Max(1, settings.RetryCount + 1);
        Error lastError = new("ai.unavailable", "The AI service could not be reached.");

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await SendAsync(request, settings, apiKey, cancellationToken).ConfigureAwait(false);
            if (outcome.IsSuccess)
                return outcome;

            lastError = outcome.Error;
            if (!IsRetryable(lastError) || attempt == attempts)
                break;

            // Exponential backoff, capped so a retry storm cannot stall the UI.
            var delay = TimeSpan.FromMilliseconds(Math.Min(4000, 400 * Math.Pow(2, attempt - 1)));
            _logger.LogWarning("AI request failed ({Code}); retrying in {Delay}ms.", lastError.Code, delay.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return Result.Failure<AiResponse>(lastError);
    }

    private async Task<Result<AiResponse>> SendAsync(
        object request,
        AiSettings settings,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Respect the configured timeout; the bounds only reject absurd values.
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 600)));

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.BaseAddress ??= new Uri(NormalizeBaseUrl(settings.BaseUrl));

            using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(request)
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            // OpenRouter uses these for attribution; neither carries user data.
            message.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/storava");
            message.Headers.TryAddWithoutValidation("X-Title", "Storava");

            using var response = await client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<AiResponse>(MapStatus(response.StatusCode));

            var completion = await response.Content
                .ReadFromJsonAsync<ChatCompletionResponse>(ResponseOptions, timeoutSource.Token)
                .ConfigureAwait(false);

            string? content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return Result.Failure<AiResponse>(new Error("ai.empty_response", "The model returned no content."));

            return ParseContent(content);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<AiResponse>(new Error("ai.timeout", "The AI request timed out."));
        }
        catch (HttpRequestException ex)
        {
            // The message may contain the endpoint but never the key, which lives in a header.
            _logger.LogWarning(ex, "AI transport failure.");
            return Result.Failure<AiResponse>(new Error("ai.network", "The AI service could not be reached."));
        }
        catch (JsonException)
        {
            return Result.Failure<AiResponse>(new Error("ai.malformed", "The model's reply was not valid JSON."));
        }
    }

    private static Result<AiResponse> ParseContent(string content)
    {
        // Models sometimes wrap JSON in a fenced block despite being told not to.
        string json = StripCodeFence(content).Trim();

        try
        {
            var parsed = JsonSerializer.Deserialize<AiResponse>(json, ResponseOptions);
            return parsed is null
                ? Result.Failure<AiResponse>(new Error("ai.malformed", "The model's reply was empty."))
                : Result.Success(parsed);
        }
        catch (JsonException)
        {
            return Result.Failure<AiResponse>(new Error("ai.malformed", "The model's reply was not valid JSON."));
        }
    }

    private static string StripCodeFence(string content)
    {
        string trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        int firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
            return trimmed;

        string body = trimmed[(firstNewline + 1)..];
        int closing = body.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? body[..closing] : body;
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        string value = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://openrouter.ai/api/v1"
            : baseUrl.Trim();
        return value.EndsWith('/') ? value : value + "/";
    }

    private static Error MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new Error("ai.unauthorized", "The API key was rejected."),
        HttpStatusCode.TooManyRequests =>
            new Error("ai.rate_limited", "The AI service is rate limiting requests."),
        HttpStatusCode.NotFound =>
            new Error("ai.model_not_found", "The configured model was not found."),
        HttpStatusCode.RequestEntityTooLarge =>
            new Error("ai.payload_too_large", "The summary was too large for this model."),
        >= HttpStatusCode.InternalServerError =>
            new Error("ai.server_error", "The AI service reported an error."),
        _ => new Error("ai.request_failed", $"The AI service returned {(int)status}.")
    };

    private static bool IsRetryable(Error error) => error.Code is
        "ai.rate_limited" or "ai.server_error" or "ai.network" or "ai.timeout";

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }

        internal sealed class Choice
        {
            public ChoiceMessage? Message { get; set; }
        }

        internal sealed class ChoiceMessage
        {
            public string? Content { get; set; }
        }
    }
}
