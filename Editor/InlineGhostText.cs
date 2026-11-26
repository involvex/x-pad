using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
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

/// <summary>
/// Draws ghost preview at the suggestion offset on the caret layer (above document text).
/// More reliable than <see cref="VisualLineElementGenerator"/> for zero-length inserts.
/// </summary>
public sealed class InlineGhostRenderer : IBackgroundRenderer
{
    private static readonly IBrush GhostForeground =
        new SolidColorBrush(Color.FromArgb(0xE0, 0x7E, 0xC8, 0xFF));
    private static readonly IBrush GhostPill =
        new SolidColorBrush(Color.FromArgb(0x55, 0x3A, 0x6E, 0xA8));
    private static readonly IPen GhostUnderline =
        new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x7E, 0xC8, 0xFF)), 1.25);

    public InlineGhostSuggestion? Suggestion { get; set; }

    public KnownLayer Layer => KnownLayer.Caret;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var s = Suggestion;
        if (s is not { IsVisible: true } || !textView.VisualLinesValid)
            return;

        var document = textView.Document;
        if (document == null || textView.VisualLines.Count == 0)
            return;

        if (s.Offset < 0 || s.Offset > document.TextLength)
            return;

        var viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        var viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        if (s.Offset < viewStart || s.Offset > viewEnd)
            return;

        var display = s.DisplayLine;
        if (display.Length == 0)
            return;

        TextLocation location;
        try
        {
            location = document.GetLocation(s.Offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        var topLeft = textView.GetVisualPosition(
            new TextViewPosition(location),
            VisualYPosition.TextTop) - textView.ScrollOffset;

        var fontSize = textView.GetValue(TextBlock.FontSizeProperty);
        if (fontSize <= 0)
            fontSize = 13;
        var fontFamily = textView.GetValue(TextBlock.FontFamilyProperty);
        var typeface = new Typeface(fontFamily ?? FontFamily.Default);

        var formatted = new FormattedText(
            display,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            GhostForeground);

        var height = Math.Max(formatted.Height, textView.DefaultLineHeight * 0.85);
        var width = Math.Max(formatted.WidthIncludingTrailingWhitespace, fontSize * 0.4);
        var pill = new Rect(topLeft.X - 2, topLeft.Y, width + 4, height);

        drawingContext.DrawRectangle(GhostPill, null, pill, 3, 3);
        drawingContext.DrawText(formatted, topLeft);
        drawingContext.DrawLine(
            GhostUnderline,
            new Point(pill.X, pill.Bottom - 0.5),
            new Point(pill.Right, pill.Bottom - 0.5));
    }
}
