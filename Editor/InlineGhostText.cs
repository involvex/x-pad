using System;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;

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

    /// <summary>First line only (for single-line visual element rendering).</summary>
    public string DisplayLine
    {
        get
        {
            var nl = Text.IndexOfAny(['\r', '\n']);
            return nl < 0 ? Text : Text[..nl];
        }
    }

    public static InlineGhostSuggestion? FromCleaned(int offset, string? cleaned)
    {
        if (string.IsNullOrEmpty(cleaned))
            return null;
        return new InlineGhostSuggestion(offset, cleaned);
    }

    /// <summary>Returns insert text when the caret still sits on the suggestion origin.</summary>
    public string? TryAccept(int caretOffset) =>
        IsVisible && caretOffset == Offset ? Text : null;

    public bool ShouldClearOnCaretMove(int caretOffset) =>
        IsVisible && caretOffset != Offset;

    public bool ShouldClearOnDocumentChange(int documentVersionAtSuggest, int currentDocumentVersion) =>
        IsVisible && documentVersionAtSuggest != currentDocumentVersion;
}

/// <summary>
/// Draws gray preview text at <see cref="Suggestion"/> offset without mutating the document.
/// </summary>
public sealed class InlineGhostTextGenerator : VisualLineElementGenerator
{
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x88, 0x88, 0x88));

    public InlineGhostSuggestion? Suggestion { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var s = Suggestion;
        if (s is not { IsVisible: true })
            return -1;
        return s.Offset >= startOffset ? s.Offset : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var s = Suggestion;
        if (s is not { IsVisible: true } || offset != s.Offset)
            return null;

        var display = s.DisplayLine;
        if (display.Length == 0)
            return null;

        // documentLength 0: virtual text that does not consume document characters.
        var props = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
        props.SetForegroundBrush(GhostBrush);
        var textLine = FormattedTextElement.PrepareText(TextFormatter.Current, display, props);
        return new FormattedTextElement(textLine, 0);
    }
}
