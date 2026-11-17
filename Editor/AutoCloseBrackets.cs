using System.Collections.Generic;
using AvaloniaEdit.Document;

namespace x_pad.Editor;

/// <summary>
/// Helpers for auto-inserting closing brackets/quotes and skipping over an
/// already-inserted closer when the user types it again (VS/Atom-style).
/// </summary>
public static class AutoCloseBrackets
{
    private static readonly Dictionary<char, char> Pairs = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['"'] = '"',
        ['\''] = '\'',
        ['`'] = '`'
    };

    private static readonly HashSet<char> Closers = [')', ']', '}', '"', '\'', '`'];

    public static bool IsOpener(char ch) => Pairs.ContainsKey(ch);

    public static bool TryGetCloser(char open, out char close) => Pairs.TryGetValue(open, out close);

    /// <summary>
    /// True when typing <paramref name="ch"/> should advance past an existing
    /// matching closer at <paramref name="caretOffset"/> instead of inserting.
    /// </summary>
    public static bool ShouldSkipTypingCloser(TextDocument document, int caretOffset, char ch)
    {
        if (document == null || !Closers.Contains(ch))
            return false;

        if (caretOffset < 0 || caretOffset >= document.TextLength)
            return false;

        return document.GetCharAt(caretOffset) == ch;
    }
}
