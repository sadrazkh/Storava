using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Storava.AI.OpenRouter;
using Storava.Application.Settings;
using Storava.Contracts.Ai;

namespace Storava.AI.Tests;

public class OpenRouterProviderTests
{
    private static AiRequestPayload Payload() => new()
    {
        System = new AiSystemInfo
        {
            Os = "Windows 11",
            SelectedLanguage = "en",
            Drive = "<Drive-C>",
            CapacityGb = 500,
            FreeGb = 20
        },
        UserGoal = new AiUserGoal { TargetFreeSpaceGb = 50, AllowDelete = false, AllowMove = true }
    };

    private static AiSettings Settings(int retries = 0) => new()
    {
        Enabled = true,
        ModelName = "openrouter/free",
        BaseUrl = "https://openrouter.ai/api/v1",
        TimeoutSeconds = 10,
        RetryCount = retries
    };

    private static OpenRouterProvider Create(StubHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<OpenRouterProvider>.Instance);

    private static string CompletionJson(string content)
    {
        // Serialize handles the quoting/escaping of the embedded model reply.
        string escaped = System.Text.Json.JsonSerializer.Serialize(content);
        return "{\"choices\":[{\"message\":{\"content\":" + escaped + "}}]}";
    }

    [Fact]
    public async Task ParsesAWellFormedCompletion()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CompletionJson(
            """{"summary":"All caches.","recommendations":[{"scanItemId":"a","title":"t","actionSuggestion":"Move","reason":"r","confidence":0.8,"estimatedSpaceGb":1}]}""")));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(), "key", "en");

        Assert.True(result.IsSuccess);
        Assert.Equal("All caches.", result.Value.Summary);
        Assert.Single(result.Value.Recommendations);
    }

    [Fact]
    public async Task AcceptsJsonWrappedInACodeFence()
    {
        // Models often ignore "no markdown" and fence the JSON anyway.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CompletionJson(
            "```json\n{\"summary\":\"Fenced\"}\n```")));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(), "key", "en");

        Assert.True(result.IsSuccess);
        Assert.Equal("Fenced", result.Value.Summary);
    }

    [Fact]
    public async Task SendsTheApiKeyAsABearerTokenOnly()
    {
        string? scheme = null;
        string? parameter = null;
        string body = string.Empty;

        // The request is disposed once the call returns, so capture what we need in place.
        var handler = new StubHandler(async (request, token) =>
        {
            scheme = request.Headers.Authorization?.Scheme;
            parameter = request.Headers.Authorization?.Parameter;
            body = await request.Content!.ReadAsStringAsync(token);
            return Json(HttpStatusCode.OK, CompletionJson("""{"summary":"ok"}"""));
        });

        await Create(handler).CompleteAsync(Payload(), Settings(), "secret-key-value", "en");

        Assert.Equal("Bearer", scheme);
        Assert.Equal("secret-key-value", parameter);

        // The key must never appear in the request body.
        Assert.DoesNotContain("secret-key-value", body);
    }

    [Fact]
    public async Task RefusesToSendWithoutAKey()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CompletionJson("{}")));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(), "   ", "en");

        Assert.True(result.IsFailure);
        Assert.Equal("ai.no_key", result.Error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "ai.unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "ai.unauthorized")]
    [InlineData(HttpStatusCode.NotFound, "ai.model_not_found")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "ai.payload_too_large")]
    public async Task MapsClientErrorsToStableCodes(HttpStatusCode status, string expectedCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(), "key", "en");

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public async Task DoesNotRetryAnUnauthorizedKey()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Create(handler).CompleteAsync(Payload(), Settings(retries: 3), "key", "en");

        // Retrying a rejected key only wastes time and can trip rate limits.
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RetriesServerErrorsThenSucceeds()
    {
        int call = 0;
        var handler = new StubHandler(_ =>
        {
            call++;
            return call < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, CompletionJson("""{"summary":"recovered"}"""));
        });

        var result = await Create(handler).CompleteAsync(Payload(), Settings(retries: 3), "key", "en");

        Assert.True(result.IsSuccess);
        Assert.Equal("recovered", result.Value.Summary);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredRetries()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(retries: 2), "key", "en");

        Assert.True(result.IsFailure);
        Assert.Equal("ai.rate_limited", result.Error.Code);
        Assert.Equal(3, handler.CallCount); // one attempt plus two retries
    }

    [Fact]
    public async Task ReportsMalformedJsonInsteadOfThrowing()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CompletionJson("this is not json")));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(), "key", "en");

        Assert.True(result.IsFailure);
        Assert.Equal("ai.malformed", result.Error.Code);
    }

    [Fact]
    public async Task ReportsAnEmptyCompletion()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"choices":[]}"""));

        var result = await Create(handler).CompleteAsync(Payload(), Settings(), "key", "en");

        Assert.True(result.IsFailure);
        Assert.Equal("ai.empty_response", result.Error.Code);
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(5000, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cts = new CancellationTokenSource();
        var task = Create(handler).CompleteAsync(Payload(), Settings(), "key", "en", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task TimesOutSlowResponses()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(3000, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var settings = Settings();
        settings.TimeoutSeconds = 1;
        var result = await Create(handler).CompleteAsync(Payload(), settings, "key", "en");

        Assert.True(result.IsFailure);
        Assert.Equal("ai.timeout", result.Error.Code);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly StubHandler _handler;

        public StubHttpClientFactory(StubHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
            => _responder = responder;

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            // Materialise the content now so assertions can read it after the call.
            if (request.Content is not null)
                await request.Content.LoadIntoBufferAsync(cancellationToken);

            return await _responder(request, cancellationToken);
        }
    }
}
