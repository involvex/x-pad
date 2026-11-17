using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace x_pad.Editor;

public sealed class WordCompletionData : ICompletionData
{
    public WordCompletionData(string text, string? description = null)
    {
        Text = text;
        Description = description ?? text;
    }

    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description { get; }
    public double Priority { get; init; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var document = textArea.Document;
        var end = Math.Max(completionSegment.EndOffset, textArea.Caret.Offset);
        var start = completionSegment.Offset;

        // Expand left so a mistimed StartOffset cannot leave the typed prefix
        // (e.g. "al" + "alpha" → "alalpha").
        while (start > 0)
        {
            var c = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            start--;
        }

        if (end < start)
            end = start;

        document.Replace(start, end - start, Text);
    }
}
