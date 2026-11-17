using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace x_pad.Editor;

/// <summary>
/// Tracks multiple word/selection occurrences for VS Code-style add-next / select-all
/// editing on AvaloniaEdit (no native multi-caret). Typing/backspace applies to all ranges.
/// </summary>
public sealed class OccurrenceSelectionController : IBackgroundRenderer
{
    private static readonly IBrush ActiveFill = new SolidColorBrush(Color.FromArgb(110, 80, 160, 255));
    private static readonly IBrush OtherFill = new SolidColorBrush(Color.FromArgb(70, 80, 160, 255));

    private readonly TextEditor _editor;
    private readonly List<TextSegment> _ranges = [];
    private string? _needle;
    private bool _suppressCaret;
    private bool _editing;

    public OccurrenceSelectionController(TextEditor editor)
    {
        _editor = editor;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public bool HasMultiple => _ranges.Count > 1;

    public int Count => _ranges.Count;

    public void Clear()
    {
        _ranges.Clear();
        _needle = null;
        Invalidate();
    }

    public int AddNextOccurrence()
    {
        var document = _editor.Document;
        if (document == null)
            return 0;

        EnsureNeedle(document);
        if (string.IsNullOrEmpty(_needle))
            return _ranges.Count;

        var searchFrom = _ranges.Count == 0
            ? 0
            : _ranges[^1].EndOffset;

        var next = FindNext(document, _needle, searchFrom);
        if (next == null && searchFrom > 0)
            next = FindNext(document, _needle, 0);

        if (next == null)
            return _ranges.Count;

        if (_ranges.Any(r => r.StartOffset == next.Value.Start && r.Length == next.Value.Length))
            return _ranges.Count;

        _ranges.Add(new TextSegment { StartOffset = next.Value.Start, Length = next.Value.Length });
        FocusRange(_ranges.Count - 1);
        Invalidate();
        return _ranges.Count;
    }

    public int SelectAllOccurrences()
    {
        var document = _editor.Document;
        if (document == null)
            return 0;

        EnsureNeedle(document);
        if (string.IsNullOrEmpty(_needle))
            return 0;

        _ranges.Clear();
        var from = 0;
        while (true)
        {
            var hit = FindNext(document, _needle, from);
            if (hit == null)
                break;
            _ranges.Add(new TextSegment { StartOffset = hit.Value.Start, Length = hit.Value.Length });
            from = hit.Value.Start + Math.Max(1, hit.Value.Length);
            if (from >= document.TextLength)
                break;
        }

        if (_ranges.Count > 0)
            FocusRange(_ranges.Count - 1);

        Invalidate();
        return _ranges.Count;
    }

    public bool TryHandleTextInput(string text)
    {
        if (!HasMultiple || _editing || string.IsNullOrEmpty(text))
            return false;

        var document = _editor.Document;
        if (document == null)
            return false;

        _editing = true;
        try
        {
            using (document.RunUpdate())
            {
                foreach (var range in _ranges.OrderByDescending(r => r.StartOffset))
                {
                    document.Replace(range.StartOffset, range.Length, text);
                    range.Length = text.Length;
                }
            }

            _needle = text;
            FocusRange(_ranges.Count - 1);
            Invalidate();
            return true;
        }
        finally
        {
            _editing = false;
        }
    }

    public bool TryHandleBackspace()
    {
        if (!HasMultiple || _editing)
            return false;

        var document = _editor.Document;
        if (document == null)
            return false;

        _editing = true;
        try
        {
            using (document.RunUpdate())
            {
                foreach (var range in _ranges.OrderByDescending(r => r.StartOffset))
                {
                    if (range.Length > 0)
                    {
                        document.Remove(range.StartOffset, 1);
                        range.Length--;
                    }
                    else if (range.StartOffset > 0)
                    {
                        document.Remove(range.StartOffset - 1, 1);
                        range.StartOffset--;
                    }
                }
            }

            _ranges.RemoveAll(r => r.Length < 0);
            if (_ranges.Count <= 1)
                Clear();
            else
            {
                _needle = _ranges[0].Length > 0
                    ? document.GetText(_ranges[0].StartOffset, _ranges[0].Length)
                    : _needle;
                FocusRange(_ranges.Count - 1);
                Invalidate();
            }

            return true;
        }
        finally
        {
            _editing = false;
        }
    }

    public void OnCaretMoved()
    {
        if (_suppressCaret || !HasMultiple)
            return;

        // Leaving the active occurrence cancels multi-edit.
        var caret = _editor.CaretOffset;
        var active = _ranges[^1];
        if (caret < active.StartOffset || caret > active.EndOffset)
            Clear();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_ranges.Count == 0 || !textView.VisualLinesValid)
            return;

        for (var i = 0; i < _ranges.Count; i++)
        {
            var brush = i == _ranges.Count - 1 ? ActiveFill : OtherFill;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, _ranges[i]))
                drawingContext.FillRectangle(brush, rect);
        }
    }

    private void EnsureNeedle(TextDocument document)
    {
        if (_needle != null && _ranges.Count > 0)
            return;

        var selection = _editor.TextArea.Selection;
        if (!selection.IsEmpty && selection.Length > 0 && !selection.IsMultiline)
        {
            _needle = selection.GetText();
            _ranges.Clear();
            var seg = selection.SurroundingSegment;
            if (seg != null)
                _ranges.Add(new TextSegment { StartOffset = seg.Offset, Length = seg.Length });
            return;
        }

        var word = GetWordAtCaret(document, _editor.CaretOffset);
        if (word == null)
        {
            _needle = null;
            return;
        }

        _needle = word.Value.Text;
        _ranges.Clear();
        _ranges.Add(new TextSegment { StartOffset = word.Value.Start, Length = word.Value.Length });
        FocusRange(0);
    }

    private void FocusRange(int index)
    {
        if (index < 0 || index >= _ranges.Count)
            return;

        var range = _ranges[index];
        _suppressCaret = true;
        try
        {
            _editor.Select(range.StartOffset, range.Length);
            _editor.TextArea.Caret.BringCaretToView();
        }
        finally
        {
            _suppressCaret = false;
        }
    }

    private void Invalidate() =>
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    private static (int Start, int Length, string Text)? GetWordAtCaret(TextDocument document, int caret)
    {
        if (document.TextLength == 0)
            return null;

        caret = Math.Clamp(caret, 0, document.TextLength);
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

    private static (int Start, int Length)? FindNext(TextDocument document, string needle, int from) =>
        OccurrenceSearch.FindNext(document, needle, from);

    private static bool IsWordChar(char c) => OccurrenceSearch.IsWordChar(c);
}
