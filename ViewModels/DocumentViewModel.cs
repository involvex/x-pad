using System;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace x_pad.ViewModels;

public partial class DocumentViewModel : ViewModelBase
{
    [ObservableProperty]
    private string title = "Untitled";

    [ObservableProperty]
    private string content = "";

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private int caretIndex;

    [ObservableProperty]
    private int lineCount = 1;

    [ObservableProperty]
    private string lineEnding = "LF";

    [ObservableProperty]
    private string encoding = "UTF-8";

    [ObservableProperty]
    private bool wordWrap;

    public TextWrapping TextWrapping => WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    partial void OnWordWrapChanged(bool value)
    {
        OnPropertyChanged(nameof(TextWrapping));
    }

    public int LineNumber
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return 1;
            var index = CaretIndex <= 0 ? 0 : Math.Min(CaretIndex, Content.Length);
            int line = 1;
            for (int i = 0; i < index; i++)
            {
                if (Content[i] == '\n')
                    line++;
            }
            return line;
        }
    }

    public int ColumnNumber
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return 1;
            var index = CaretIndex <= 0 ? 0 : Math.Min(CaretIndex, Content.Length);
            var lastNewLine = Content.LastIndexOf('\n', index - 1);
            if (lastNewLine < 0)
                return index + 1;
            return index - lastNewLine;
        }
    }

    partial void OnContentChanged(string value)
    {
        UpdateLineCount(value);
        DetectLineEnding(value);
        
        if (FilePath == null)
        {
            if (!Title.EndsWith("*"))
            {
                Title = Title + "*";
            }
        }
        else if (!IsDirty)
        {
            Title = System.IO.Path.GetFileName(FilePath) + "*";
        }
        IsDirty = true;
        OnPropertyChanged(nameof(LineNumber));
        OnPropertyChanged(nameof(ColumnNumber));
    }

    partial void OnCaretIndexChanged(int value)
    {
        OnPropertyChanged(nameof(LineNumber));
        OnPropertyChanged(nameof(ColumnNumber));
    }

    private void UpdateLineCount(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            LineCount = 1;
            return;
        }
        LineCount = value.Count(c => c == '\n') + 1;
    }

    private void DetectLineEnding(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Contains("\r\n"))
            LineEnding = "CRLF";
        else if (value.Contains("\r"))
            LineEnding = "CR";
        else if (value.Contains("\n"))
            LineEnding = "LF";
    }

    public void MarkClean(string filePath)
    {
        FilePath = filePath;
        IsDirty = false;
        Title = System.IO.Path.GetFileName(filePath);
    }
}
