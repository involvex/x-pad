using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using x_pad.Models;
using x_pad.Services;

namespace x_pad.ViewModels;

public partial class DocumentViewModel : ViewModelBase
{
    private bool _suppressDirty;
    private string _baseTitle = "Untitled";

    public TextDocument Document { get; }

    [ObservableProperty]
    private string title = "Untitled";

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private int lineNumber = 1;

    [ObservableProperty]
    private int columnNumber = 1;

    [ObservableProperty]
    private int lineCount = 1;

    [ObservableProperty]
    private string lineEnding = "LF";

    [ObservableProperty]
    private string encoding = "UTF-8";

    [ObservableProperty]
    private string language = "Plain Text";

    [ObservableProperty]
    private bool wordWrap;

    [ObservableProperty]
    private double fontSize = 13;

    public LineEndingKind LineEndingKind { get; private set; } = LineEndingKind.Lf;
    public Encoding TextEncoding { get; private set; } = FileService.CreateUtf8(false);
    public bool HasBom { get; private set; }
    public DateTime? LastWriteTimeUtc { get; set; }
    public int LspVersion { get; set; } = 1;

    public string Text
    {
        get => Document.Text;
        set
        {
            _suppressDirty = true;
            Document.Text = value ?? "";
            Document.UndoStack.ClearAll();
            Document.UndoStack.MarkAsOriginalFile();
            _suppressDirty = false;
            UpdateLineCount();
            RefreshDirtyState();
        }
    }

    public DocumentViewModel()
    {
        Document = new TextDocument();
        Document.Changed += OnDocumentChanged;
        Document.UndoStack.PropertyChanged += OnUndoStackPropertyChanged;
    }

    public void ApplySnapshot(FileSnapshot snapshot, string path)
    {
        FilePath = path;
        _baseTitle = Path.GetFileName(path);
        TextEncoding = snapshot.Encoding;
        HasBom = snapshot.HasBom;
        Encoding = snapshot.EncodingName;
        LineEndingKind = snapshot.LineEnding;
        LineEnding = snapshot.LineEnding.ToDisplay();
        LastWriteTimeUtc = snapshot.LastWriteTimeUtc;
        Text = snapshot.Text;
        Title = _baseTitle;
        IsDirty = false;
    }

    public void MarkClean(string path)
    {
        FilePath = path;
        _baseTitle = Path.GetFileName(path);
        Document.UndoStack.MarkAsOriginalFile();
        IsDirty = false;
        Title = _baseTitle;
        try
        {
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            LastWriteTimeUtc = DateTime.UtcNow;
        }
    }

    public void SetUntitled(string title)
    {
        FilePath = null;
        _baseTitle = title;
        Title = title;
        TextEncoding = FileService.CreateUtf8(false);
        HasBom = false;
        Encoding = "UTF-8";
        LineEndingKind = LineEndingKind.Lf;
        LineEnding = "LF";
        Language = "Plain Text";
        Text = "";
        IsDirty = false;
    }

    public void SetLineEnding(LineEndingKind kind)
    {
        LineEndingKind = kind;
        LineEnding = kind.ToDisplay();
    }

    public void UpdateCaret(int line, int column)
    {
        LineNumber = line;
        ColumnNumber = column;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_suppressDirty)
            return;

        UpdateLineCount();
        RefreshDirtyState();
        LspVersion++;
    }

    private void OnUndoStackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UndoStack.IsOriginalFile) or null)
            RefreshDirtyState();
    }

    private void RefreshDirtyState()
    {
        var dirty = !Document.UndoStack.IsOriginalFile;
        IsDirty = dirty;
        Title = dirty ? _baseTitle + "*" : _baseTitle;
    }

    private void UpdateLineCount()
    {
        LineCount = Math.Max(1, Document.LineCount);
    }
}
