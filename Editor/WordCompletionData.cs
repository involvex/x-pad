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
        textArea.Document.Replace(completionSegment, Text);
    }
}
