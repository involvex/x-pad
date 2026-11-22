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
    private bool insertSpaces = true;

    [ObservableProperty]
    private string indentStyle = "Spaces";

    [ObservableProperty]
    private string encoding = "UTF-8";

    [ObservableProperty]
    private string language = "Plain Text";

    [ObservableProperty]
    private bool wordWrap;

    [ObservableProperty]
    private double fontSize = 13;

    [ObservableProperty]
    private string fontFamily = "Cascadia Code, JetBrains Mono, Consolas, Courier New, monospace";

    [ObservableProperty]
    private bool isSplit;

    /// <summary>True = side-by-side, false = stacked.</summary>
    [ObservableProperty]
    private bool splitVertical = true;

    public LineEndingKind LineEndingKind { get; private set; } = LineEndingKind.Lf;
    public Encoding TextEncoding { get; set; } = FileService.CreateUtf8(false);
    public bool HasBom { get; set; }
    public DateTime? LastWriteTimeUtc { get; set; }
    public int LspVersion { get; set; } = 1;

    /// <summary>
    /// When set (e.g. ".cs"), TextMate grammar uses this extension instead of the file path.
    /// Null = Auto (from file).
    /// </summary>
    public string? LanguageOverrideExtension { get; set; }

    /// <summary>One-shot caret restore after session reload (consumed by DocumentEditor).</summary>
    public int? PendingRestoreLine { get; set; }
    public int? PendingRestoreColumn { get; set; }

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

    public void ApplySnapshot(FileSnapshot snapshot, string path, bool preferInsertSpaces = true)
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
        SetInsertSpaces(x_pad.Editor.IndentStyle.ResolveInsertSpaces(snapshot.Text, preferInsertSpaces));
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

    public void SetUntitled(string title, bool insertSpaces = true)
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
        LanguageOverrideExtension = null;
        SetInsertSpaces(insertSpaces);
        Text = "";
        IsDirty = false;
    }

    /// <summary>
    /// Opens a path that does not exist yet (Notepad-style): empty buffer, path set for Save.
    /// </summary>
    public void SetNewFile(string path, bool insertSpaces = true)
    {
        FilePath = path;
        _baseTitle = Path.GetFileName(path);
        Title = _baseTitle;
        TextEncoding = FileService.CreateUtf8(false);
        HasBom = false;
        Encoding = "UTF-8";
        LineEndingKind = LineEndingKind.Lf;
        LineEnding = "LF";
        LastWriteTimeUtc = null;
        LanguageOverrideExtension = null;
        SetInsertSpaces(insertSpaces);
        Text = "";
        IsDirty = false;
    }

    public void SetLineEnding(LineEndingKind kind)
    {
        LineEndingKind = kind;
        LineEnding = kind.ToDisplay();
    }

    public void SetInsertSpaces(bool insertSpaces)
    {
        InsertSpaces = insertSpaces;
        IndentStyle = x_pad.Editor.IndentStyle.ToDisplay(insertSpaces);
    }

    /// <summary>Restores a closed-tab buffer (path optional). Does not touch LastWriteTimeUtc.</summary>
    public void RestoreClosedBuffer(
        string? path,
        string title,
        string text,
        bool dirty,
        LineEndingKind lineEnding,
        Encoding encoding,
        bool hasBom,
        string encodingName,
        bool insertSpaces = true)
    {
        FilePath = path;
        _baseTitle = !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : title;
        TextEncoding = encoding;
        HasBom = hasBom;
        Encoding = encodingName;
        SetLineEnding(lineEnding);
        SetInsertSpaces(insertSpaces);
        _suppressDirty = true;
        Document.Text = text ?? "";
        Document.UndoStack.ClearAll();
        if (!dirty)
            Document.UndoStack.MarkAsOriginalFile();
        _suppressDirty = false;
        IsDirty = dirty;
        Title = dirty ? _baseTitle + "*" : _baseTitle;
        UpdateLineCount();
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
