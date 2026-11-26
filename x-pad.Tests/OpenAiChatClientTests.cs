using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using x_pad.Services.Ai;

namespace x_pad.Tests;

public class OpenAiChatClientTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("secret", "secret")]
    [InlineData(" Bearer secret ", "secret")]
    [InlineData("\"secret\"", "secret")]
    [InlineData("'Bearer abc.def'", "abc.def")]
    [InlineData("sec\r\nret", "secret")]
    public void NormalizeApiKey_StripsBearerQuotesAndWhitespace(string? input, string expected)
    {
        Assert.Equal(expected, OpenAiChatClient.NormalizeApiKey(input));
    }

    [Fact]
    public void BuildChatCompletionsUri_AppendsPath()
    {
        var uri = OpenAiChatClient.BuildChatCompletionsUri("https://api.kilo.ai/api/gateway/");
        Assert.Equal("https://api.kilo.ai/api/gateway/chat/completions", uri?.ToString());
    }

    [Fact]
    public void BuildChatCompletionsUri_RejectsEmpty()
    {
        Assert.Null(OpenAiChatClient.BuildChatCompletionsUri(""));
        Assert.Null(OpenAiChatClient.BuildChatCompletionsUri(null));
    }

    [Fact]
    public async Task CompleteAsync_OmitsAuthorizationWhenKeyEmpty()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, """
            {"choices":[{"message":{"content":"return 1;"}}]}
            """);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "http://127.0.0.1:9/v1",
            "test-model",
            ApiKey: "",
            SystemPrompt: "sys",
            UserMessage: "user"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("return 1;", result.Text);
        Assert.False(handler.LastRequest!.Headers.Contains("Authorization"));
        Assert.Contains("\"model\":\"test-model\"", handler.LastBody);
        Assert.Contains("\"role\":\"system\"", handler.LastBody);
        Assert.Contains("\"role\":\"user\"", handler.LastBody);
    }

    [Fact]
    public async Task CompleteAsync_SendsBearerAndKiloModeHeader()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, """
            {"choices":[{"message":{"content":"x"}}]}
            """);
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "https://api.kilo.ai/api/gateway",
            "kilo-auto/free",
            ApiKey: "Bearer secret-key",
            SystemPrompt: "sys",
            UserMessage: "user"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Bearer secret-key", handler.LastRequest!.Headers.Authorization?.ToString());
        Assert.True(handler.LastRequest.Headers.TryGetValues("x-kilocode-mode", out var modes));
        Assert.Equal("code", Assert.Single(modes));
    }

    [Fact]
    public async Task CompleteAsync_KiloFree401_RetriesAnonymous()
    {
        // api.kilo.ai 401 → ai-gateway 401 → anonymous OK on api.kilo.ai
        var handler = new SequenceHandler(
            (HttpStatusCode.Unauthorized, """{"error":{"code":"INVALID_TOKEN","message":"bad"}}"""),
            (HttpStatusCode.Unauthorized, """{"error":{"code":"INVALID_TOKEN","message":"bad"}}"""),
            (HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok;"}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "https://api.kilo.ai/api/gateway",
            "kilo-auto/free",
            ApiKey: "session-token",
            SystemPrompt: "sys",
            UserMessage: "user"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("ok;", result.Text);
        Assert.True(result.UsedAnonymousFallback);
        Assert.Equal(3, handler.CallCount);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task CompleteAsync_ReasoningOnly_RetriesWithHigherMaxTokens()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
                """{"model":"reflection/beam-free","choices":[{"finish_reason":"length","message":{"content":"","reasoning":"think"}}]}"""),
            (HttpStatusCode.OK,
                """{"model":"reflection/beam-free","choices":[{"finish_reason":"stop","message":{"content":"turn a + b;"}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "https://api.kilo.ai/api/gateway",
            "kilo-auto/free",
            ApiKey: "",
            SystemPrompt: "sys",
            UserMessage: "user",
            MaxTokens: 256), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("turn a + b;", result.Text);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains($"\"max_tokens\":{OpenAiChatClient.ReasoningRetryTokens}", handler.LastBody);
    }

    [Fact]
    public void AiDebugLog_RedactsBearerTokens()
    {
        var redacted = AiDebugLog.Redact("Authorization: Bearer super-secret-token\napi_key");
        Assert.DoesNotContain("super-secret-token", redacted);
        Assert.Contains("Bearer ***", redacted);
    }

    [Fact]
    public async Task CompleteAsync_HonorsCancellation()
    {
        var handler = new DelayHandler(TimeSpan.FromSeconds(5));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var client = new OpenAiChatClient(http);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CompleteAsync(new AiCompletionRequest(
                "http://127.0.0.1:9/v1",
                "m",
                null,
                "sys",
                "user"), cts.Token));
    }

    [Fact]
    public async Task CompleteAsync_ReportsRateLimit()
    {
        var handler = new CaptureHandler(HttpStatusCode.TooManyRequests, "slow down");
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "http://127.0.0.1:9/v1",
            "m",
            null,
            "sys",
            "user"), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.True(result.RateLimited);
    }

    [Fact]
    public async Task CompleteAsync_EmptyContentWithReasoningBudget_ReportsClearError()
    {
        const string body = """
            {"choices":[{"finish_reason":"length","message":{"role":"assistant","content":"","reasoning":"thinking…"}}]}
            """;
        var handler = new CaptureHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "http://127.0.0.1:9/v1",
            "m",
            null,
            "sys",
            "user",
            MaxTokens: OpenAiChatClient.MaxCompletionTokens), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("reasoning", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_NullContentWithReasoning_IsRetryable()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK,
                """{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":null,"reasoning":"think"}}]}"""),
            (HttpStatusCode.OK,
                """{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":" --port 8080"}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "https://api.kilo.ai/api/gateway",
            "kilo-auto/free",
            ApiKey: "",
            SystemPrompt: "sys",
            UserMessage: "user",
            MaxTokens: 256), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("--port 8080", result.Text);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_RemembersRejectedKey_ForFreeModel()
    {
        // Call 1: api.kilo 401 → ai-gateway 401 → anonymous OK
        // Call 2: skip rejected key → anonymous OK (no auth attempts)
        var handler = new SequenceHandler(
            (HttpStatusCode.Unauthorized, """{"error":{"code":"INVALID_TOKEN"}}"""),
            (HttpStatusCode.Unauthorized, """{"error":{"code":"INVALID_TOKEN"}}"""),
            (HttpStatusCode.OK, """{"choices":[{"message":{"content":"first;"}}]}"""),
            (HttpStatusCode.OK, """{"choices":[{"message":{"content":"second;"}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var req = new AiCompletionRequest(
            "https://api.kilo.ai/api/gateway",
            "kilo-auto/free",
            ApiKey: "bad-token",
            SystemPrompt: "sys",
            UserMessage: "user");

        var first = await client.CompleteAsync(req, CancellationToken.None);
        Assert.True(first.Ok);
        Assert.True(first.UsedAnonymousFallback);
        Assert.Equal(3, handler.CallCount);

        var second = await client.CompleteAsync(req, CancellationToken.None);
        Assert.True(second.Ok);
        Assert.Equal("second;", second.Text);
        Assert.True(second.UsedAnonymousFallback);
        // Skipped straight to anonymous — no re-auth on either host.
        Assert.Equal(4, handler.CallCount);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task CompleteAsync_AltKiloHost_AcceptsValidKeyAfterApiHost401()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.Unauthorized, """{"error":{"code":"INVALID_TOKEN"}}"""),
            (HttpStatusCode.OK, """{"choices":[{"message":{"content":"from-docs-host;"}}]}"""));
        using var http = new HttpClient(handler);
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "https://api.kilo.ai/api/gateway",
            "kilo-auto/free",
            ApiKey: "gateway-jwt",
            SystemPrompt: "sys",
            UserMessage: "user"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("from-docs-host;", result.Text);
        Assert.False(result.UsedAnonymousFallback);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal("Bearer gateway-jwt", handler.LastRequest!.Headers.Authorization?.ToString());
        Assert.Contains("ai-gateway.kilo.ai", handler.LastRequest.RequestUri!.Host, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_HttpTimeout_ReturnsErrorNotCancel()
    {
        var handler = new DelayHandler(TimeSpan.FromSeconds(5));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        using var client = new OpenAiChatClient(http);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            "http://127.0.0.1:9/v1",
            "m",
            null,
            "sys",
            "user"), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public CaptureHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Status, string Body)[] _responses;
        private int _index;

        public SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) =>
            _responses = responses;

        public int CallCount => _index;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var (status, body) = _responses[Math.Min(_index, _responses.Length - 1)];
            _index++;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public DelayHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"x"}}]}""")
            };
        }
    }
}
