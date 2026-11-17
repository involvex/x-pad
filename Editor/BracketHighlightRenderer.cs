using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace x_pad.Editor;

/// <summary>
/// Highlights a matching bracket pair under/near the caret.
/// </summary>
public sealed class BracketHighlightRenderer : IBackgroundRenderer
{
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(90, 100, 180, 255));
    private static readonly IPen Border = new Pen(new SolidColorBrush(Color.FromArgb(180, 100, 180, 255)), 1);

    private BracketMatch? _match;

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetMatch(BracketMatch? match)
    {
        _match = match;
    }

    public void Clear() => _match = null;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_match is not { } match || !textView.VisualLinesValid)
            return;

        DrawOffset(textView, drawingContext, match.OpenOffset);
        DrawOffset(textView, drawingContext, match.CloseOffset);
    }

    private static void DrawOffset(TextView textView, DrawingContext drawingContext, int offset)
    {
        var segment = new TextSegment { StartOffset = offset, Length = 1 };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            drawingContext.DrawRectangle(Fill, Border, new Rect(rect.X, rect.Y, Math.Max(1, rect.Width), rect.Height));
        }
    }
}
