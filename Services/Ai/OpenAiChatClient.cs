using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace x_pad.Services.Ai;

public sealed record AiCompletionRequest(
    string BaseUrl,
    string Model,
    string? ApiKey,
    string SystemPrompt,
    string UserMessage,
    int MaxTokens = InlineCompletionPrompt.DefaultMaxTokens,
    double Temperature = 0);

public sealed record AiCompletionResult(bool Ok, string Text, string? Error, bool RateLimited);

/// <summary>Minimal OpenAI-compatible <c>POST /chat/completions</c> client.</summary>
public sealed class OpenAiChatClient : IDisposable
{
    public const string KiloGatewayHost = "api.kilo.ai";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static bool IsKiloGateway(Uri uri) =>
        uri.Host.Equals(KiloGatewayHost, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("ai-gateway.kilo.ai", StringComparison.OrdinalIgnoreCase);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OpenAiChatClient(HttpClient? http = null)
    {
        if (http == null)
        {
            _http = CreateDefaultClient();
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("X-Pad-AiCompletion");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "";
        return baseUrl.Trim().TrimEnd('/');
    }

    public static Uri? BuildChatCompletionsUri(string? baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (normalized.Length == 0)
            return null;

        if (!Uri.TryCreate(normalized + "/chat/completions", UriKind.Absolute, out var uri))
            return null;
        return uri;
    }

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
    {
        var uri = BuildChatCompletionsUri(request.BaseUrl);
        if (uri == null)
            return new AiCompletionResult(false, "", "AI base URL is empty or invalid.", false);

        if (string.IsNullOrWhiteSpace(request.Model))
            return new AiCompletionResult(false, "", "AI model is empty.", false);

        using var msg = new HttpRequestMessage(HttpMethod.Post, uri);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());

        if (IsKiloGateway(uri))
            msg.Headers.TryAddWithoutValidation("x-kilocode-mode", "code");

        var body = new
        {
            model = request.Model.Trim(),
            temperature = request.Temperature,
            max_tokens = Math.Clamp(request.MaxTokens, 8, 512),
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserMessage }
            }
        };

        msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.SendAsync(msg, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new AiCompletionResult(false, "", "AI rate limited (429). Try again later.", true);

            if (!response.IsSuccessStatusCode)
            {
                var snippet = raw.Length > 180 ? raw[..180] + "…" : raw;
                return new AiCompletionResult(
                    false,
                    "",
                    $"AI request failed ({(int)response.StatusCode}): {snippet}",
                    false);
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var content = TryReadContent(doc.RootElement);
            var cleaned = InlineCompletionPrompt.CleanResponse(content);
            if (cleaned.Length == 0)
                return new AiCompletionResult(false, "", "AI returned an empty suggestion.", false);

            return new AiCompletionResult(true, cleaned, null, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AiCompletionResult(false, "", $"AI request error: {ex.Message}", false);
        }
    }

    private static string? TryReadContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;
        if (choices.GetArrayLength() == 0)
            return null;

        var first = choices[0];
        if (first.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (first.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();

        return null;
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
