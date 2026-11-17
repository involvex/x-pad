using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace x_pad.Editor;

/// <summary>
/// Tracks bookmarked lines via document anchors so they survive edits.
/// </summary>
public sealed class BookmarkStore : IBackgroundRenderer
{
    private static readonly IBrush LineFill = new SolidColorBrush(Color.FromArgb(40, 255, 180, 40));
    private static readonly IBrush MarkerFill = new SolidColorBrush(Color.FromRgb(255, 170, 40));

    private readonly List<TextAnchor> _anchors = [];
    private TextDocument? _document;

    public KnownLayer Layer => KnownLayer.Background;

    public int Count => _anchors.Count;

    public void Attach(TextDocument document)
    {
        Clear();
        _document = document;
    }

    public void Clear()
    {
        _anchors.Clear();
        _document = null;
    }

    public bool ToggleAtOffset(int offset)
    {
        if (_document == null || _document.TextLength == 0)
            return false;

        offset = Math.Clamp(offset, 0, Math.Max(0, _document.TextLength - 1));
        var line = _document.GetLineByOffset(offset);
        var existing = _anchors.FirstOrDefault(a => !a.IsDeleted &&
            _document.GetLineByOffset(a.Offset).LineNumber == line.LineNumber);
        if (existing != null)
        {
            _anchors.Remove(existing);
            return false;
        }

        var anchor = _document.CreateAnchor(line.Offset);
        anchor.SurviveDeletion = true;
        _anchors.Add(anchor);
        return true;
    }

    public int? GoToNext(int fromOffset)
    {
        var lines = GetSortedLineNumbers();
        if (lines.Count == 0 || _document == null)
            return null;

        var current = _document.GetLineByOffset(Math.Clamp(fromOffset, 0, Math.Max(0, _document.TextLength - 1))).LineNumber;
        var next = lines.FirstOrDefault(l => l > current);
        if (next == 0)
            next = lines[0];
        return _document.GetOffset(next, 1);
    }

    public int? GoToPrevious(int fromOffset)
    {
        var lines = GetSortedLineNumbers();
        if (lines.Count == 0 || _document == null)
            return null;

        var current = _document.GetLineByOffset(Math.Clamp(fromOffset, 0, Math.Max(0, _document.TextLength - 1))).LineNumber;
        var prev = lines.LastOrDefault(l => l < current);
        if (prev == 0)
            prev = lines[^1];
        return _document.GetOffset(prev, 1);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_document == null || _anchors.Count == 0 || !textView.VisualLinesValid)
            return;

        PruneDeleted();
        var bookmarked = new HashSet<int>(GetSortedLineNumbers());
        if (bookmarked.Count == 0)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (!bookmarked.Contains(lineNumber))
                continue;

            var top = visualLine.VisualTop - textView.ScrollOffset.Y;
            var height = visualLine.Height;
            var width = textView.Bounds.Width;
            drawingContext.FillRectangle(LineFill, new Rect(0, top, width, height));
            drawingContext.FillRectangle(MarkerFill, new Rect(0, top + 2, 3, Math.Max(2, height - 4)));
        }
    }

    private List<int> GetSortedLineNumbers()
    {
        PruneDeleted();
        if (_document == null)
            return [];

        return _anchors
            .Where(a => !a.IsDeleted)
            .Select(a => _document.GetLineByOffset(a.Offset).LineNumber)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    private void PruneDeleted() => _anchors.RemoveAll(a => a.IsDeleted);
}
