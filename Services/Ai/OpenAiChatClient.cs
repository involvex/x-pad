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

    /// <summary>
    /// Strip whitespace, surrounding quotes, and a duplicated <c>Bearer </c> prefix
    /// so pasted dashboard keys and <c>Bearer …</c> clips both work.
    /// </summary>
    public static string NormalizeApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return "";

        var key = apiKey.Trim();
        if (key.Length >= 2)
        {
            if ((key[0] == '"' && key[^1] == '"') || (key[0] == '\'' && key[^1] == '\''))
                key = key[1..^1].Trim();
        }

        if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            key = key["Bearer ".Length..].Trim();

        // Soft line-wrap / zero-width junk from paste into the password box.
        if (key.IndexOfAny(['\r', '\n', '\t', ' ', '\u200b']) >= 0)
        {
            var buffer = new char[key.Length];
            var n = 0;
            foreach (var c in key)
            {
                if (char.IsWhiteSpace(c) || c == '\u200b')
                    continue;
                buffer[n++] = c;
            }
            key = new string(buffer, 0, n);
        }

        return key;
    }

    public static bool LooksLikeFreeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;
        var m = model.Trim();
        return m.Contains(":free", StringComparison.OrdinalIgnoreCase) ||
               m.EndsWith("/free", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(m, "kilo-auto/free", StringComparison.OrdinalIgnoreCase);
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

        var apiKey = NormalizeApiKey(request.ApiKey);
        var result = await SendCompletionAsync(uri, request, apiKey, ct).ConfigureAwait(false);

        // Kilo free tiers allow anonymous access. A pasted IDE/session token often
        // 401s even when the model itself is free — retry once without Authorization.
        if (!result.Ok &&
            result.Error?.Contains("(401)", StringComparison.Ordinal) == true &&
            apiKey.Length > 0 &&
            IsKiloGateway(uri) &&
            LooksLikeFreeModel(request.Model))
        {
            var anon = await SendCompletionAsync(uri, request, apiKey: "", ct).ConfigureAwait(false);
            if (anon.Ok)
                return anon;
        }

        if (!result.Ok && result.Error?.Contains("(401)", StringComparison.Ordinal) == true)
        {
            return result with
            {
                Error = IsKiloGateway(uri)
                    ? "Kilo rejected the API key (401). For kilo-auto/free leave API key empty, " +
                      "or paste the Gateway key from app.kilo.ai → Profile (not an extension session token)."
                    : "AI auth failed (401). Check the API key, or leave it empty if the provider allows anonymous access."
            };
        }

        return result;
    }

    private async Task<AiCompletionResult> SendCompletionAsync(
        Uri uri,
        AiCompletionRequest request,
        string apiKey,
        CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, uri);
        if (apiKey.Length > 0)
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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
