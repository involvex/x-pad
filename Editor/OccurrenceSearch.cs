using System;
using System.Linq;
using AvaloniaEdit.Document;

namespace x_pad.Editor;

/// <summary>Whole-word-aware occurrence search used by multi-occurrence edit.</summary>
public static class OccurrenceSearch
{
    public static (int Start, int Length)? FindNext(string text, string needle, int from)
    {
        if (string.IsNullOrEmpty(needle) || string.IsNullOrEmpty(text) || from >= text.Length)
            return null;

        var index = text.IndexOf(needle, Math.Max(0, from), StringComparison.Ordinal);
        if (index < 0)
            return null;

        if (IsIdentifier(needle))
        {
            while (index >= 0)
            {
                var beforeOk = index == 0 || !IsWordChar(text[index - 1]);
                var afterOk = index + needle.Length >= text.Length ||
                              !IsWordChar(text[index + needle.Length]);
                if (beforeOk && afterOk)
                    return (index, needle.Length);

                index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
            }

            return null;
        }

        return (index, needle.Length);
    }

    public static (int Start, int Length)? FindNext(TextDocument document, string needle, int from)
    {
        if (document == null)
            return null;
        return FindNext(document.Text, needle, from);
    }

    public static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_') &&
        text.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
