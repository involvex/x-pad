using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace x_pad.Editor;

/// <summary>
/// Draws vertical indent guides at each indent level (spaces or tabs).
/// </summary>
public sealed class IndentGuideRenderer : IBackgroundRenderer
{
    private static readonly IPen GuidePen = new Pen(
        new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
        1);

    public int IndentSize { get; set; } = 4;
    public bool Enabled { get; set; } = true;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled || IndentSize <= 0 || !textView.VisualLinesValid)
            return;

        var document = textView.Document;
        if (document == null)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.FirstDocumentLine.IsDeleted)
                continue;

            var line = visualLine.FirstDocumentLine;
            var text = document.GetText(line.Offset, line.Length);
            var indentColumns = CountLeadingIndentColumns(text, IndentSize);
            if (indentColumns < IndentSize)
                continue;

            var textLine = visualLine.TextLines[0];
            var top = visualLine.VisualTop - textView.ScrollOffset.Y;
            var bottom = top + visualLine.Height;

            for (var col = IndentSize; col <= indentColumns; col += IndentSize)
            {
                var x = visualLine.GetTextLineVisualXPosition(textLine, col) - textView.ScrollOffset.X;
                drawingContext.DrawLine(GuidePen, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    private static int CountLeadingIndentColumns(string text, int indentSize)
    {
        var columns = 0;
        foreach (var ch in text)
        {
            if (ch == ' ')
                columns++;
            else if (ch == '\t')
                columns += indentSize - (columns % indentSize);
            else
                break;
        }
        return columns;
    }
}
