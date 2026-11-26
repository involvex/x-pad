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
/// VS Code–style inline completion: italic dim ghost text plus a floating
/// Accept / Tab hint bar above the caret.
/// </summary>
public sealed class InlineGhostRenderer : IBackgroundRenderer
{
    // Match VS Code dark inline-suggest chrome.
    private static readonly IBrush ToolbarFill = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
    private static readonly IPen ToolbarBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)), 1);
    private static readonly IBrush ToolbarFg = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly IBrush KeycapFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly IPen KeycapBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)), 1);
    private static readonly IBrush GhostForeground =
        new SolidColorBrush(Color.FromArgb(0xB8, 0x9E, 0x9E, 0x9E));

    private const double ToolbarPadX = 8;
    private const double ToolbarPadY = 4;
    private const double ToolbarGap = 6;
    private const double KeycapPadX = 5;
    private const double KeycapPadY = 1.5;
    private const double ToolbarFontSize = 11;
    private const double KeycapFontSize = 10;

    public InlineGhostSuggestion? Suggestion { get; set; }

    /// <summary>Last painted Accept hit-target in TextView viewport coordinates (for click-to-accept).</summary>
    public Rect AcceptHitBounds { get; private set; }

    public KnownLayer Layer => KnownLayer.Caret;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        AcceptHitBounds = default;
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

        var textTop = textView.GetVisualPosition(
            new TextViewPosition(location),
            VisualYPosition.TextTop) - textView.ScrollOffset;
        var lineTop = textView.GetVisualPosition(
            new TextViewPosition(location),
            VisualYPosition.LineTop) - textView.ScrollOffset;

        var editorFontSize = textView.GetValue(TextBlock.FontSizeProperty);
        if (editorFontSize <= 0)
            editorFontSize = 13;
        var fontFamily = textView.GetValue(TextBlock.FontFamilyProperty) ?? FontFamily.Default;

        DrawGhostText(drawingContext, display, textTop, fontFamily, editorFontSize);
        DrawToolbar(drawingContext, textView, lineTop, fontFamily);
    }

    /// <summary>True when <paramref name="viewportPoint"/> hits the Accept control.</summary>
    public bool HitTestAccept(Point viewportPoint) =>
        Suggestion is { IsVisible: true } &&
        AcceptHitBounds.Width > 0 &&
        AcceptHitBounds.Contains(viewportPoint);

    private static void DrawGhostText(
        DrawingContext drawingContext,
        string display,
        Point topLeft,
        FontFamily fontFamily,
        double fontSize)
    {
        var typeface = new Typeface(fontFamily, FontStyle.Italic, FontWeight.Normal);
        var formatted = new FormattedText(
            display,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            GhostForeground);
        drawingContext.DrawText(formatted, topLeft);
    }

    private void DrawToolbar(
        DrawingContext drawingContext,
        TextView textView,
        Point lineTopLeft,
        FontFamily fontFamily)
    {
        var uiFace = new Typeface(FontFamily.Default);
        var acceptLabel = new FormattedText(
            "Accept",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            uiFace,
            ToolbarFontSize,
            ToolbarFg);
        var tabLabel = new FormattedText(
            "Tab",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            uiFace,
            KeycapFontSize,
            KeycapFg);

        var keycapW = tabLabel.Width + KeycapPadX * 2;
        var keycapH = tabLabel.Height + KeycapPadY * 2;
        var contentH = Math.Max(acceptLabel.Height, keycapH);
        var contentW = acceptLabel.Width + 6 + keycapW;
        var barW = contentW + ToolbarPadX * 2;
        var barH = contentH + ToolbarPadY * 2;

        // Prefer above the line; flip below when near the top of the view.
        var barX = Math.Max(4, lineTopLeft.X);
        var barY = lineTopLeft.Y - barH - ToolbarGap;
        if (barY < 2)
            barY = lineTopLeft.Y + textView.DefaultLineHeight + 2;

        // Keep inside the viewport horizontally.
        var maxX = Math.Max(4, textView.Bounds.Width - barW - 4);
        if (barX > maxX)
            barX = maxX;

        var bar = new Rect(barX, barY, barW, barH);
        drawingContext.DrawRectangle(ToolbarFill, ToolbarBorder, bar, 4, 4);

        var acceptOrigin = new Point(bar.X + ToolbarPadX, bar.Y + (barH - acceptLabel.Height) / 2);
        drawingContext.DrawText(acceptLabel, acceptOrigin);

        var keycap = new Rect(
            acceptOrigin.X + acceptLabel.Width + 6,
            bar.Y + (barH - keycapH) / 2,
            keycapW,
            keycapH);
        drawingContext.DrawRectangle(null, KeycapBorder, keycap, 3, 3);
        drawingContext.DrawText(
            tabLabel,
            new Point(keycap.X + KeycapPadX, keycap.Y + KeycapPadY));

        // Whole toolbar is the accept hit target (VS Code clicks Accept or the keycap).
        AcceptHitBounds = bar;
    }
}
