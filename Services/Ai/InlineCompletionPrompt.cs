using System;

namespace x_pad.Services.Ai;

/// <summary>Builds FIM-style chat prompts and cleans model replies (no HTTP).</summary>
public static class InlineCompletionPrompt
{
    public const int DefaultPrefixChars = 4000;
    public const int DefaultSuffixChars = 1000;
    /// <summary>
    /// Headroom for gateways that spend tokens on hidden reasoning before
    /// <c>message.content</c> (e.g. Kilo free / beam-free).
    /// </summary>
    public const int DefaultMaxTokens = 256;

    public const string SystemPrompt =
        "You are a code completion engine. Reply with only the text to insert at the cursor. " +
        "Do not repeat the prefix. Do not use markdown fences. Do not explain. " +
        "Do not think out loud or narrate. Output only the insertion characters. " +
        "Stop at a short natural boundary (end of statement, closing brace, or a few lines).";

    public static (string Prefix, string Suffix) ExtractContext(
        string documentText,
        int caretOffset,
        int prefixChars = DefaultPrefixChars,
        int suffixChars = DefaultSuffixChars)
    {
        if (documentText is null)
            documentText = "";

        caretOffset = Math.Clamp(caretOffset, 0, documentText.Length);
        var prefixStart = Math.Max(0, caretOffset - Math.Max(0, prefixChars));
        var suffixEnd = Math.Min(documentText.Length, caretOffset + Math.Max(0, suffixChars));
        var prefix = documentText[prefixStart..caretOffset];
        var suffix = documentText[caretOffset..suffixEnd];
        return (prefix, suffix);
    }

    public static string BuildUserMessage(string prefix, string suffix, string? languageHint = null)
    {
        var lang = string.IsNullOrWhiteSpace(languageHint) ? "plain text" : languageHint.Trim();
        return
            $"Language: {lang}\n" +
            "Complete the code at <CURSOR>. Return only the insertion.\n" +
            "----- PREFIX -----\n" +
            (prefix ?? "") +
            "\n<CURSOR>\n" +
            "----- SUFFIX -----\n" +
            (suffix ?? "");
    }

    /// <summary>Strip markdown fences / role chatter; return empty when nothing useful remains.</summary>
    public static string CleanResponse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var text = content.Replace("\r\n", "\n").Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0)
                text = text[(firstNl + 1)..];
            else
                text = text.TrimStart('`');

            var fence = text.LastIndexOf("\n```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
            else if (text.EndsWith("```", StringComparison.Ordinal))
                text = text[..^3];
        }

        text = text.TrimEnd();
        if (text.Length == 0)
            return "";

        // Refuse obvious chat replies.
        if (text.StartsWith("Sure,", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Here", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("I ", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains('\n'))
            {
                var nl = text.IndexOf('\n');
                text = text[(nl + 1)..].TrimStart();
            }
            else
                return "";
        }

        return text;
    }
}
