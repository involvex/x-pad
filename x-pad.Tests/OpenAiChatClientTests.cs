using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using x_pad.Services.Ai;

namespace x_pad.Tests;

public class OpenAiChatClientTests
{
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
            ApiKey: "secret-key",
            SystemPrompt: "sys",
            UserMessage: "user"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Bearer secret-key", handler.LastRequest!.Headers.Authorization?.ToString());
        Assert.True(handler.LastRequest.Headers.TryGetValues("x-kilocode-mode", out var modes));
        Assert.Equal("code", Assert.Single(modes));
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
