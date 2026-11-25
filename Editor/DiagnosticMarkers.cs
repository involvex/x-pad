using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using x_pad.Services.Lsp;

namespace x_pad.Editor;

public sealed class DiagnosticMarker : TextSegment
{
    public required int Severity { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Draws squiggly underlines for LSP diagnostics under AvaloniaEdit visual lines.
/// </summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private readonly TextDocument _document;
    private List<DiagnosticMarker> _markers = [];

    public DiagnosticRenderer(TextDocument document)
    {
        _document = document;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<DiagnosticMarker> Markers => _markers;

    public void SetDiagnostics(IEnumerable<LspDiagnostic> diagnostics)
    {
        var next = new List<DiagnosticMarker>();
        foreach (var d in diagnostics)
        {
            try
            {
                var startLine = Math.Clamp(d.Range.Start.Line + 1, 1, _document.LineCount);
                var endLine = Math.Clamp(d.Range.End.Line + 1, 1, _document.LineCount);
                var start = _document.GetOffset(startLine, Math.Max(1, d.Range.Start.Character + 1));
                var end = _document.GetOffset(endLine, Math.Max(1, d.Range.End.Character + 1));
                if (end <= start)
                    end = Math.Min(_document.TextLength, start + 1);

                next.Add(new DiagnosticMarker
                {
                    StartOffset = start,
                    Length = Math.Max(1, end - start),
                    Severity = d.Severity,
                    Message = d.Message
                });
            }
            catch
            {
                // Skip malformed ranges
            }
        }

        _markers = next;
    }

    public void Clear() => _markers = [];

    public string? GetMessageAtOffset(int offset)
    {
        return _markers.FirstOrDefault(m => m.StartOffset <= offset && offset <= m.EndOffset)?.Message;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_markers.Count == 0 || !textView.VisualLinesValid)
            return;

        foreach (var marker in _markers)
        {
            var pen = PenFor(marker.Severity);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker))
            {
                DrawSquiggle(drawingContext, pen, rect.BottomLeft, rect.BottomRight);
            }
        }
    }

    private static readonly Pen ErrorPen = CreatePen(Color.FromRgb(244, 71, 71));
    private static readonly Pen WarningPen = CreatePen(Color.FromRgb(204, 167, 0));
    private static readonly Pen InfoPen = CreatePen(Color.FromRgb(55, 148, 255));
    private static readonly Pen HintPen = CreatePen(Color.FromRgb(140, 140, 140));

    private static Pen CreatePen(Color color) =>
        new(new SolidColorBrush(color), 1.2);

    private static Pen PenFor(int severity) => severity switch
    {
        1 => ErrorPen,
        2 => WarningPen,
        3 => InfoPen,
        _ => HintPen
    };

    private static void DrawSquiggle(DrawingContext context, Pen pen, Point start, Point end)
    {
        const double amplitude = 1.6;
        const double period = 3.0;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            var x = start.X;
            var up = true;
            while (x < end.X)
            {
                x += period;
                var y = start.Y + (up ? -amplitude : amplitude);
                ctx.LineTo(new Point(Math.Min(x, end.X), y));
                up = !up;
            }
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
