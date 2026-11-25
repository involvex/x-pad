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
        // Free/routed models can be slow; keep below "forever" but above 8s reasoning budget.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
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
            {
                var finish = TryReadFinishReason(doc.RootElement);
                var hasReasoning = HasReasoning(doc.RootElement);
                if (hasReasoning && string.Equals(finish, "length", StringComparison.OrdinalIgnoreCase))
                {
                    return new AiCompletionResult(
                        false,
                        "",
                        "AI used its token budget on reasoning with no insertion text. Try again.",
                        false);
                }

                return new AiCompletionResult(false, "", "AI returned an empty suggestion.", false);
            }

            return new AiCompletionResult(true, cleaned, null, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as TaskCanceledException with a still-open token.
            return new AiCompletionResult(false, "", "AI request timed out.", false);
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
        {
            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        if (first.TryGetProperty("text", out var legacy) && legacy.ValueKind == JsonValueKind.String)
        {
            var text = legacy.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string? TryReadFinishReason(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;
        if (choices.GetArrayLength() == 0)
            return null;
        if (choices[0].TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            return finish.GetString();
        return null;
    }

    private static bool HasReasoning(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return false;
        if (choices.GetArrayLength() == 0)
            return false;
        if (!choices[0].TryGetProperty("message", out var message))
            return false;
        if (message.TryGetProperty("reasoning", out var reasoning) &&
            reasoning.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(reasoning.GetString()))
            return true;
        return message.TryGetProperty("reasoning_details", out var details) &&
               details.ValueKind == JsonValueKind.Array &&
               details.GetArrayLength() > 0;
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
