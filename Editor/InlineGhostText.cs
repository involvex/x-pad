using System;

namespace x_pad.Editor;

/// <summary>Pure ghost-suggestion state (offset + insert text).</summary>
public sealed class InlineGhostSuggestion
{
    public InlineGhostSuggestion(int offset, string text)
    {
        Offset = Math.Max(0, offset);
        Text = text ?? "";
    }

    public int Offset { get; }
    public string Text { get; }
    public bool IsVisible => Text.Length > 0;

    /// <summary>First line only (for single-line visual rendering).</summary>
    public string DisplayLine
    {
        get
        {
            var nl = Text.IndexOfAny(['\r', '\n']);
            return nl < 0 ? Text : Text[..nl];
        }
    }

    /// <summary>Short status-bar preview (escaped whitespace, truncated).</summary>
    public string StatusPreview(int maxChars = 40)
    {
        var line = DisplayLine.Replace('\t', ' ');
        if (line.Length <= maxChars)
            return line;
        return line[..(maxChars - 1)] + "…";
    }

    public static InlineGhostSuggestion? FromCleaned(int offset, string? cleaned)
    {
        if (string.IsNullOrEmpty(cleaned))
            return null;
        return new InlineGhostSuggestion(offset, cleaned);
    }

    /// <summary>
    /// Drop suggestions that only echo text already after the caret (common with
    /// reasoning routers that copy the suffix into <c>content</c>).
    /// </summary>
    public static InlineGhostSuggestion? FromCleaned(int offset, string? cleaned, string? suffixAfterCaret)
    {
        var suggestion = FromCleaned(offset, cleaned);
        if (suggestion == null)
            return null;

        if (string.IsNullOrEmpty(suffixAfterCaret))
            return suggestion;

        var insert = suggestion.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var suffix = suffixAfterCaret.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (suffix.StartsWith(insert, StringComparison.Ordinal))
            return null;

        return suggestion;
    }

    /// <summary>Returns insert text when the caret still sits on the suggestion origin.</summary>
    public string? TryAccept(int caretOffset) =>
        IsVisible && caretOffset == Offset ? Text : null;

    public bool ShouldClearOnCaretMove(int caretOffset) =>
        IsVisible && caretOffset != Offset;

    public bool ShouldClearOnDocumentChange(int documentVersionAtSuggest, int currentDocumentVersion) =>
        IsVisible && documentVersionAtSuggest != currentDocumentVersion;
}
