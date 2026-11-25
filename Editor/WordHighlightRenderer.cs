using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace x_pad.Editor;

/// <summary>
/// Soft-highlights all whole-word occurrences of the identifier under the caret.
/// </summary>
public sealed class WordHighlightRenderer : IBackgroundRenderer
{
    public const int MaxMatches = 256;
    public const int MaxDocumentLength = 2 * 1024 * 1024;

    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(55, 180, 180, 80));

    private readonly List<TextSegment> _segments = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public int MatchCount => _segments.Count;

    public void Clear()
    {
        _segments.Clear();
    }

    public void Update(TextDocument? document, int caretOffset)
    {
        _segments.Clear();
        if (document == null || document.TextLength == 0)
            return;

        if (document.TextLength > MaxDocumentLength)
            return;

        caretOffset = Math.Clamp(caretOffset, 0, document.TextLength);
        var word = GetWordAt(document, caretOffset);
        if (word == null || word.Value.Length < 2)
            return;

        var needle = word.Value.Text;
        var needleLen = needle.Length;
        var from = 0;
        while (from < document.TextLength && _segments.Count < MaxMatches)
        {
            var remaining = document.TextLength - from;
            if (remaining < needleLen)
                break;

            var index = document.IndexOf(needle, from, remaining, StringComparison.Ordinal);
            if (index < 0)
                break;

            var beforeOk = index == 0 || !IsWordChar(document.GetCharAt(index - 1));
            var afterOk = index + needleLen >= document.TextLength ||
                          !IsWordChar(document.GetCharAt(index + needleLen));
            if (beforeOk && afterOk)
            {
                _segments.Add(new TextSegment
                {
                    StartOffset = index,
                    Length = needleLen
                });
            }

            from = index + Math.Max(1, needleLen);
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segments.Count == 0 || !textView.VisualLinesValid)
            return;

        foreach (var segment in _segments)
        {
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.FillRectangle(Fill, rect);
        }
    }

    private static (int Start, int Length, string Text)? GetWordAt(TextDocument document, int caret)
    {
        var start = caret;
        while (start > 0 && IsWordChar(document.GetCharAt(start - 1)))
            start--;

        var end = caret;
        while (end < document.TextLength && IsWordChar(document.GetCharAt(end)))
            end++;

        if (end <= start)
            return null;

        return (start, end - start, document.GetText(start, end - start));
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
