using System;
using AvaloniaEdit.Document;

namespace x_pad.Editor;

public readonly record struct BracketMatch(int OpenOffset, int CloseOffset);

/// <summary>
/// Finds matching (), [], {} pairs. Quotes are ignored (too ambiguous without a lexer).
/// </summary>
public static class BracketMatcher
{
    /// <summary>Stop scanning after this many characters from the bracket.</summary>
    public const int MaxScanDistance = 32_768;

    private static readonly (char Open, char Close)[] Pairs =
    [
        ('(', ')'),
        ('[', ']'),
        ('{', '}')
    ];

    public static BracketMatch? FindMatch(TextDocument document, int caretOffset)
    {
        if (document.TextLength == 0)
            return null;

        caretOffset = Math.Clamp(caretOffset, 0, document.TextLength);

        // Prefer bracket immediately left of caret, then under caret.
        if (caretOffset > 0)
        {
            var left = TryMatchAt(document, caretOffset - 1);
            if (left != null)
                return left;
        }

        if (caretOffset < document.TextLength)
            return TryMatchAt(document, caretOffset);

        return null;
    }

    private static BracketMatch? TryMatchAt(TextDocument document, int offset)
    {
        var ch = document.GetCharAt(offset);
        foreach (var (open, close) in Pairs)
        {
            if (ch == open)
            {
                var closeOffset = ScanForward(document, offset, open, close);
                return closeOffset >= 0 ? new BracketMatch(offset, closeOffset) : null;
            }

            if (ch == close)
            {
                var openOffset = ScanBackward(document, offset, open, close);
                return openOffset >= 0 ? new BracketMatch(openOffset, offset) : null;
            }
        }

        return null;
    }

    private static int ScanForward(TextDocument document, int start, char open, char close)
    {
        var depth = 0;
        var limit = Math.Min(document.TextLength, start + MaxScanDistance);
        for (var i = start; i < limit; i++)
        {
            var c = document.GetCharAt(i);
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static int ScanBackward(TextDocument document, int start, char open, char close)
    {
        var depth = 0;
        var limit = Math.Max(0, start - MaxScanDistance);
        for (var i = start; i >= limit; i--)
        {
            var c = document.GetCharAt(i);
            if (c == close) depth++;
            else if (c == open)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }
}
