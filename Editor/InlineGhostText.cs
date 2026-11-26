using System;
using Avalonia;
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

/// <summary>
/// Draws gray preview text at <see cref="Suggestion"/> offset without mutating the document.
/// Uses a non-1 visual length so following document text does not paint over the ghost.
/// </summary>
public sealed class InlineGhostTextGenerator : VisualLineElementGenerator
{
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0x9A, 0x9A, 0x9A));

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

        var props = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
        props.SetForegroundBrush(GhostBrush);
        var textLine = FormattedTextElement.PrepareText(TextFormatter.Current, display, props);
        // VisualLength must span the preview; FormattedTextElement hardcodes 1 and gets covered.
        return new InlineGhostVisualElement(textLine, display.Length);
    }
}

/// <summary>Zero-document-length ghost run with a real visual width.</summary>
internal sealed class InlineGhostVisualElement : VisualLineElement
{
    private readonly TextLine _textLine;

    public InlineGhostVisualElement(TextLine textLine, int visualLength)
        : base(Math.Max(1, visualLength), documentLength: 0)
    {
        _textLine = textLine ?? throw new ArgumentNullException(nameof(textLine));
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) =>
        new InlineGhostTextRun(_textLine, TextRunProperties);
}

internal sealed class InlineGhostTextRun : DrawableTextRun
{
    private readonly TextLine _textLine;

    public InlineGhostTextRun(TextLine textLine, TextRunProperties properties)
    {
        _textLine = textLine;
        Properties = properties;
    }

    public override ReadOnlyMemory<char> Text => ReadOnlyMemory<char>.Empty;
    public override TextRunProperties Properties { get; }
    public override double Baseline => _textLine.Baseline;

    public override Size Size =>
        new(_textLine.WidthIncludingTrailingWhitespace, _textLine.Height);

    public override void Draw(DrawingContext drawingContext, Point origin) =>
        _textLine.Draw(drawingContext, origin);
}
