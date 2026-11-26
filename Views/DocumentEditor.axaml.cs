using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using x_pad.Editor;
using x_pad.Models;
using x_pad.Services;
using x_pad.Services.Ai;
using x_pad.Services.Lsp;
using x_pad.ViewModels;

namespace x_pad.Views;

public partial class DocumentEditor : UserControl
{
    private TextEditor? _editor;
    private TextEditor? _editorSecondary;
    private TextMate.Installation? _textMate;
    private TextMate.Installation? _textMateSecondary;
    private RegistryOptions? _registry;
    private Grid? _splitRoot;
    private GridSplitter? _verticalSplitter;
    private GridSplitter? _horizontalSplitter;
    private ColumnDefinition? _secondaryColumn;
    private RowDefinition? _secondaryRow;
    private SearchPanel? _searchPanel;
    private CompletionWindow? _completionWindow;
    private FoldingManager? _foldingManager;
    private TextDocument? _foldingDocument;
    private readonly BraceFoldingStrategy _foldingStrategy = new();
    private DispatcherTimer? _foldingTimer;
    private SettingsService? _settings;
    private LspManager? _lsp;
    private DocumentViewModel? _document;
    private DiagnosticRenderer? _diagnostics;
    private IndentGuideRenderer? _indentGuides;
    private BracketHighlightRenderer? _brackets;
    private OccurrenceSelectionController? _occurrences;
    private WordHighlightRenderer? _wordHighlight;
    private BookmarkStore? _bookmarks;
    private bool _initialized;
    private int _changeVersion;
    private DispatcherTimer? _wordHighlightTimer;
    private readonly WordListCache _wordListCache = new();
    private CancellationTokenSource? _lspCompletionCts;
    private int _lspCompletionVersion;
    private readonly List<LspContentChange> _pendingLspChanges = [];
    private bool _lspNeedsFullSync;
    private readonly InlineGhostRenderer _ghostRenderer = new();
    private InlineGhostSuggestion? _ghostSuggestion;
    private int _ghostDocumentVersion = -1;
    private bool _ghostDisabledAcceptsTab;
    private CancellationTokenSource? _aiCompletionCts;
    private int _aiCompletionVersion;
    private OpenAiChatClient? _aiClient;
    private DateTime _aiRateLimitedUntilUtc = DateTime.MinValue;
    private DispatcherTimer? _aiIdleTimer;

    public DocumentEditor()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public void ConfigureServices(SettingsService settings, LspManager lsp)
    {
        if (_settings != null)
            _settings.Changed -= OnSettingsChanged;
        if (_lsp != null)
            _lsp.DiagnosticsReceived -= OnLspDiagnostics;

        _settings = settings;
        _lsp = lsp;
        _settings.Changed += OnSettingsChanged;
        _lsp.DiagnosticsReceived += OnLspDiagnostics;
        ApplySettings();
    }

    public void OpenFind()
    {
        if (_searchPanel == null || _editor == null)
            return;

        SeedSearchPatternFromSelection();
        _searchPanel.IsReplaceMode = false;
        _searchPanel.Open();
        _searchPanel.Reactivate();
    }

    public void OpenReplace()
    {
        if (_searchPanel == null || _editor == null)
            return;

        SeedSearchPatternFromSelection();
        _searchPanel.IsReplaceMode = true;
        _searchPanel.Open();
        _searchPanel.Reactivate();
    }

    public void FindNext()
    {
        if (_searchPanel == null)
            return;
        if (_searchPanel.IsClosed)
        {
            if (string.IsNullOrEmpty(_searchPanel.SearchPattern))
            {
                OpenFind();
                return;
            }

            _searchPanel.Open();
        }

        _searchPanel.FindNext();
    }

    public void FindPrevious()
    {
        if (_searchPanel == null)
            return;
        if (_searchPanel.IsClosed)
        {
            if (string.IsNullOrEmpty(_searchPanel.SearchPattern))
            {
                OpenFind();
                return;
            }

            _searchPanel.Open();
        }

        _searchPanel.FindPrevious();
    }

    private void SeedSearchPatternFromSelection()
    {
        if (_searchPanel == null || _editor == null)
            return;

        var selected = _editor.SelectedText;
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n') && selected.Length < 200)
            _searchPanel.SearchPattern = selected;
    }

    public void GoToLine(int line)
    {
        if (_editor?.Document == null) return;
        line = Math.Clamp(line, 1, _editor.Document.LineCount);
        var offset = _editor.Document.GetOffset(line, 1);
        _editor.CaretOffset = offset;
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    /// <summary>
    /// Returns the current selection (or caret) as 0-based LSP line/character bounds.
    /// </summary>
    public bool TryGetLspRange(out int startLine, out int startCharacter, out int endLine, out int endCharacter)
    {
        startLine = startCharacter = endLine = endCharacter = 0;
        if (_editor?.Document == null)
            return false;

        var document = _editor.Document;
        var startOffset = _editor.SelectionLength > 0
            ? _editor.SelectionStart
            : _editor.CaretOffset;
        var endOffset = _editor.SelectionLength > 0
            ? _editor.SelectionStart + _editor.SelectionLength
            : startOffset;

        startOffset = Math.Clamp(startOffset, 0, document.TextLength);
        endOffset = Math.Clamp(endOffset, 0, document.TextLength);
        if (endOffset < startOffset)
            (startOffset, endOffset) = (endOffset, startOffset);

        var startLoc = document.GetLocation(startOffset);
        var endLoc = document.GetLocation(endOffset);
        startLine = startLoc.Line - 1;
        startCharacter = Math.Max(0, startLoc.Column - 1);
        endLine = endLoc.Line - 1;
        endCharacter = Math.Max(0, endLoc.Column - 1);
        return true;
    }

    public void GoToMatch(int line, int column, int length)
    {
        if (_editor?.Document == null)
            return;

        line = Math.Clamp(line, 1, _editor.Document.LineCount);
        var docLine = _editor.Document.GetLineByNumber(line);
        column = Math.Clamp(column, 1, docLine.Length + 1);
        var offset = _editor.Document.GetOffset(line, column);
        length = Math.Clamp(length, 0, Math.Max(0, _editor.Document.TextLength - offset));
        _editor.Select(offset, length);
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    public void InvertCurrentSelection()
    {
        if (_editor?.Document == null)
            return;

        var start = _editor.SelectionStart;
        var length = _editor.SelectionLength;
        var ranges = InvertSelection.ComputeRanges(_editor.Document, start, length);
        if (ranges.Count == 0)
        {
            _editor.Select(0, 0);
            return;
        }

        // AvaloniaEdit exposes a single contiguous selection; prefer the larger complement.
        var best = ranges.OrderByDescending(r => r.Length).First();
        _editor.Select(best.Start, best.Length);
        _editor.Focus();
    }

    public string GetSearchQuery()
    {
        if (_editor == null)
            return "";
        if (!string.IsNullOrEmpty(_editor.SelectedText))
            return _editor.SelectedText;
        return GetWordBeforeCaret();
    }

    public string GetExportText()
    {
        if (_editor == null)
            return _document?.Text ?? "";
        if (!string.IsNullOrEmpty(_editor.SelectedText))
            return _editor.SelectedText;
        return _editor.Document?.Text ?? _document?.Text ?? "";
    }

    private void InstallEditorContextMenu(TextEditor editor)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateEditorMenuItem("Cu_t", (_, _) => editor.Cut()));
        menu.Items.Add(CreateEditorMenuItem("_Copy", (_, _) => editor.Copy()));
        menu.Items.Add(CreateEditorMenuItem("_Paste", (_, _) => editor.Paste()));
        menu.Items.Add(CreateEditorMenuItem("_Delete", (_, _) =>
        {
            if (editor.SelectionLength > 0)
                editor.Document?.Remove(editor.SelectionStart, editor.SelectionLength);
        }));
        menu.Items.Add(CreateEditorMenuItem("Select _All", (_, _) => editor.SelectAll()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateEditorMenuItem("_Invert Selection", (_, _) => InvertCurrentSelection()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateEditorMenuItem("_Find...", (_, _) => OpenFind()));
        menu.Items.Add(CreateEditorMenuItem("_Replace...", (_, _) => OpenReplace()));
        menu.Items.Add(CreateEditorMenuItem("Search _Web", (_, _) =>
        {
            var q = GetSearchQuery();
            if (SearchWebHelper.TryOpen(q, out var err))
                return;
            var window = this.FindAncestorOfType<MainWindow>();
            if (window?.DataContext is MainWindowViewModel vm)
                vm.StatusText = string.IsNullOrEmpty(err) ? "Search Web failed" : err;
        }));
        editor.ContextMenu = menu;
    }

    private static MenuItem CreateEditorMenuItem(string header, EventHandler<RoutedEventArgs> onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    public TextEditor? TextEditorControl => _editor;

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_settings == null)
        {
            var window = this.FindAncestorOfType<MainWindow>();
            if (window?.DataContext is MainWindowViewModel vm)
                ConfigureServices(vm.Settings, vm.Lsp);
        }

        EnsureInitialized();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        EditorThemeService.ThemeChanged -= OnThemeChanged;
        if (_document != null)
            _document.PropertyChanged -= OnDocumentPropertyChanged;
        if (_settings != null)
            _settings.Changed -= OnSettingsChanged;
        if (_lsp != null)
            _lsp.DiagnosticsReceived -= OnLspDiagnostics;
        _foldingTimer?.Stop();
        UninstallFolding();
        UninstallDiagnostics();
        UninstallIndentGuides();
        UninstallBrackets();
        UninstallOccurrences();
        UninstallWordHighlight();
        UninstallBookmarks();
        UninstallGhostText();
        _wordHighlightTimer?.Stop();
        _aiIdleTimer?.Stop();
        _lspCompletionCts?.Cancel();
        _lspCompletionCts?.Dispose();
        _lspCompletionCts = null;
        _aiCompletionCts?.Cancel();
        _aiCompletionCts?.Dispose();
        _aiCompletionCts = null;
        _aiClient?.Dispose();
        _aiClient = null;
        _ = CloseLspDocumentAsync();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_document != null)
            _document.PropertyChanged -= OnDocumentPropertyChanged;

        _document = DataContext as DocumentViewModel;
        if (_document != null)
            _document.PropertyChanged += OnDocumentPropertyChanged;

        if (_initialized)
            BindDocument();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _editor = this.FindControl<TextEditor>("Editor");
        _editorSecondary = this.FindControl<TextEditor>("EditorSecondary");
        _splitRoot = this.FindControl<Grid>("SplitRoot");
        _verticalSplitter = this.FindControl<GridSplitter>("VerticalSplitter");
        _horizontalSplitter = this.FindControl<GridSplitter>("HorizontalSplitter");
        if (_editor == null || _splitRoot == null)
            return;

        _secondaryColumn = _splitRoot.ColumnDefinitions[2];
        _secondaryRow = _splitRoot.RowDefinitions[2];

        _initialized = true;
        var theme = EditorThemeService.CurrentTextMateTheme;
        _registry = new RegistryOptions(theme);
        _textMate = _editor.InstallTextMate(_registry);
        // InstallTextMate alone can leave a light editor surface; force the active theme.
        var loaded = _registry.LoadTheme(theme);
        _textMate.SetTheme(loaded);
        if (_editorSecondary != null)
        {
            var secondaryRegistry = new RegistryOptions(theme);
            _textMateSecondary = _editorSecondary.InstallTextMate(secondaryRegistry);
            _textMateSecondary.SetTheme(secondaryRegistry.LoadTheme(theme));
        }
        ApplyEditorSurfaceColors();
        _searchPanel = SearchPanel.Install(_editor);
        // Amber wash that stays visible on dark TextMate themes.
        _editor.SearchResultsBrush =
            new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(96, 255, 196, 64));

        ConfigureEditorChrome(_editor);
        if (_editorSecondary != null)
            ConfigureEditorChrome(_editorSecondary);

        InstallEditorContextMenu(_editor);
        _editor.TextArea.RightClickMovesCaret = true;

        _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.TextEntered += OnTextEntered;
        // Tunnel so we see Tab before TextArea.OnKeyDown (which always inserts indent when AcceptsTab).
        _editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.PointerWheelChanged += OnPointerWheelChanged;
        _editor.PointerMoved += OnEditorPointerMoved;
        _editor.PointerExited += OnEditorPointerExited;
        _editor.DocumentChanged += OnEditorDocumentChanged;

        EditorThemeService.ThemeChanged += OnThemeChanged;

        _foldingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _foldingTimer.Tick += (_, _) =>
        {
            _foldingTimer.Stop();
            UpdateFolding();
        };

        _wordHighlightTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _wordHighlightTimer.Tick += (_, _) =>
        {
            _wordHighlightTimer.Stop();
            UpdateBracketHighlight();
            UpdateWordHighlight();
        };

        _aiIdleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _aiIdleTimer.Tick += (_, _) =>
        {
            _aiIdleTimer.Stop();
            _ = RequestAiCompletionAsync(manual: false);
        };

        InstallGhostText();
        BindDocument();
        ApplySettings();
        ApplySplitLayout();
        UpdateCaretStatus();
        // Ensure theme paints even when ThemeChanged was skipped (same theme already set).
        OnThemeChanged(EditorThemeService.CurrentTextMateTheme);
    }

    private static void ConfigureEditorChrome(TextEditor editor)
    {
        editor.Options.EnableTextDragDrop = true;
        editor.Options.AllowScrollBelowDocument = true;
        editor.Options.HighlightCurrentLine = true;
        editor.Options.EnableRectangularSelection = true;
        // Softer current-line wash that reads well on dark Fluent chrome.
        editor.TextArea.TextView.CurrentLineBackground =
            new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(36, 120, 160, 255));
        editor.TextArea.TextView.CurrentLineBorder =
            new Avalonia.Media.Pen(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0, 0, 0, 0)), 0);
    }

    private void ApplyFontFamily(string? family)
    {
        if (_editor == null)
            return;

        var value = string.IsNullOrWhiteSpace(family)
            ? "Cascadia Code, JetBrains Mono, Consolas, Courier New, monospace"
            : family;
        var font = new Avalonia.Media.FontFamily(value);
        _editor.FontFamily = font;
        if (_editorSecondary != null)
            _editorSecondary.FontFamily = font;
    }

    private void BindDocument()
    {
        if (_editor == null || _document == null)
            return;

        // FoldingManager must be installed on the active document. Replacing
        // TextEditor.Document after Install leaves a stale manager and crashes layout.
        UninstallFolding();
        UninstallDiagnostics();
        UninstallIndentGuides();
        UninstallBrackets();
        UninstallOccurrences();
        UninstallWordHighlight();
        UninstallBookmarks();
        ClearGhostSuggestion();
        _editor.Document = _document.Document;
        if (_editorSecondary != null)
            _editorSecondary.Document = _document.Document;
        ApplyReadOnlyState();
        _foldingTimer?.Stop();
        _aiIdleTimer?.Stop();
        _wordListCache.Invalidate();
        _pendingLspChanges.Clear();
        _lspNeedsFullSync = false;
        if (_document.IsLargeFile)
        {
            UninstallFolding();
        }
        else
        {
            _foldingManager = FoldingManager.Install(_editor.TextArea);
            _foldingDocument = _editor.Document;
            ScheduleFoldingUpdate();
        }

        InstallDiagnostics();
        InstallIndentGuides();
        InstallBrackets();
        InstallOccurrences();
        InstallWordHighlight();
        InstallBookmarks();

        ApplyGrammar();
        ApplySettings();
        ApplySplitLayout();
        if (!_document.IsLargeFile)
            UpdateFolding();
        UpdateBracketHighlight();
        UpdateWordHighlight();
        ApplyPendingCaretRestore();
        UpdateCaretStatus();
        if (!_document.IsLargeFile)
            _ = OpenLspDocumentAsync();
    }

    private void ApplyReadOnlyState()
    {
        if (_editor == null || _document == null)
            return;

        _editor.IsReadOnly = _document.IsReadOnly;
        if (_editorSecondary != null)
            _editorSecondary.IsReadOnly = _document.IsReadOnly;
    }

    public void RestorePendingCaret() => ApplyPendingCaretRestore();

    private void ApplyPendingCaretRestore()
    {
        if (_editor?.Document == null || _document == null)
            return;
        if (_document.PendingRestoreLine is not int line)
            return;

        var column = Math.Max(1, _document.PendingRestoreColumn ?? 1);
        _document.PendingRestoreLine = null;
        _document.PendingRestoreColumn = null;

        line = Math.Clamp(line, 1, _editor.Document.LineCount);
        var docLine = _editor.Document.GetLineByNumber(line);
        column = Math.Clamp(column, 1, docLine.Length + 1);
        _editor.CaretOffset = _editor.Document.GetOffset(line, column);
        _editor.ScrollToLine(line);
        UpdateCaretStatus();
    }

    private void UninstallFolding()
    {
        if (_foldingManager == null)
            return;

        FoldingManager.Uninstall(_foldingManager);
        _foldingManager = null;
        _foldingDocument = null;
    }

    private void InstallDiagnostics()
    {
        if (_editor?.Document == null)
            return;

        _diagnostics = new DiagnosticRenderer(_editor.Document);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_diagnostics);
    }

    private void UninstallDiagnostics()
    {
        if (_editor != null && _diagnostics != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_diagnostics);
        _diagnostics = null;
    }

    private void InstallIndentGuides()
    {
        if (_editor == null)
            return;

        _indentGuides = new IndentGuideRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_indentGuides);
    }

    private void UninstallIndentGuides()
    {
        if (_editor != null && _indentGuides != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_indentGuides);
        _indentGuides = null;
    }

    private void InstallBrackets()
    {
        if (_editor == null)
            return;

        _brackets = new BracketHighlightRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_brackets);
    }

    private void UninstallBrackets()
    {
        if (_editor != null && _brackets != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_brackets);
        _brackets = null;
    }

    private void InstallOccurrences()
    {
        if (_editor == null)
            return;

        _occurrences = new OccurrenceSelectionController(_editor);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_occurrences);
    }

    private void UninstallOccurrences()
    {
        if (_editor != null && _occurrences != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_occurrences);
        _occurrences?.Clear();
        _occurrences = null;
    }

    private void InstallWordHighlight()
    {
        if (_editor == null || _document?.IsLargeFile == true)
            return;

        _wordHighlight = new WordHighlightRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_wordHighlight);
    }

    private void UninstallWordHighlight()
    {
        if (_editor != null && _wordHighlight != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_wordHighlight);
        _wordHighlight?.Clear();
        _wordHighlight = null;
    }

    private void InstallBookmarks()
    {
        if (_editor?.Document == null)
            return;

        _bookmarks = new BookmarkStore();
        _bookmarks.Attach(_editor.Document);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_bookmarks);
    }

    private void UninstallBookmarks()
    {
        if (_editor != null && _bookmarks != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_bookmarks);
        _bookmarks?.Clear();
        _bookmarks = null;
    }

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_editor == null || _diagnostics == null)
            return;

        var point = e.GetPosition(_editor.TextArea.TextView);
        var pos = _editor.TextArea.TextView.GetPosition(point + _editor.TextArea.TextView.ScrollOffset);
        if (pos == null)
        {
            ToolTip.SetIsOpen(_editor, false);
            return;
        }

        var offset = _editor.Document.GetOffset(pos.Value.Location);
        var message = _diagnostics.GetMessageAtOffset(offset);
        if (string.IsNullOrWhiteSpace(message))
        {
            ToolTip.SetIsOpen(_editor, false);
            return;
        }

        ToolTip.SetTip(_editor, message);
        ToolTip.SetIsOpen(_editor, true);
    }

    private void OnEditorPointerExited(object? sender, PointerEventArgs e)
    {
        if (_editor != null)
            ToolTip.SetIsOpen(_editor, false);
    }

    private void OnSettingsChanged() =>
        Dispatcher.UIThread.Post(ApplySettings);

    private void OnLspDiagnostics(string uri, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        if (_document?.FilePath == null || _diagnostics == null || _editor == null)
            return;

        var selectedUri = ToUri(_document.FilePath);
        if (!string.Equals(selectedUri, uri, StringComparison.OrdinalIgnoreCase))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _diagnostics.SetDiagnostics(diagnostics);
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        });
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.FilePath) or nameof(DocumentViewModel.Language))
        {
            ApplyGrammar();
            _ = OpenLspDocumentAsync();
        }

        if (e.PropertyName == nameof(DocumentViewModel.WordWrap) && _editor != null && _document != null)
        {
            _editor.WordWrap = _document.WordWrap;
            if (_editorSecondary != null)
                _editorSecondary.WordWrap = _document.WordWrap;
        }

        if (e.PropertyName == nameof(DocumentViewModel.FontSize) && _editor != null && _document != null)
        {
            _editor.FontSize = _document.FontSize;
            if (_editorSecondary != null)
                _editorSecondary.FontSize = _document.FontSize;
        }

        if (e.PropertyName == nameof(DocumentViewModel.FontFamily) && _editor != null && _document != null)
        {
            ApplyFontFamily(_document.FontFamily);
        }

        if (e.PropertyName is nameof(DocumentViewModel.IsSplit) or nameof(DocumentViewModel.SplitVertical))
            ApplySplitLayout();

        if (e.PropertyName == nameof(DocumentViewModel.IsReadOnly))
            ApplyReadOnlyState();

        if (e.PropertyName == nameof(DocumentViewModel.IsLargeFile) && _document != null)
        {
            if (_document.IsLargeFile)
                UninstallFolding();
            else if (_foldingManager == null && _editor != null)
            {
                _foldingManager = FoldingManager.Install(_editor.TextArea);
                _foldingDocument = _editor.Document;
                UpdateFolding();
            }
        }

        if (e.PropertyName is nameof(DocumentViewModel.InsertSpaces) or nameof(DocumentViewModel.IndentStyle))
            ApplySettings();
    }

    private void ApplySplitLayout()
    {
        if (_document == null || _editor == null || _editorSecondary == null ||
            _splitRoot == null || _verticalSplitter == null || _horizontalSplitter == null ||
            _secondaryColumn == null || _secondaryRow == null)
            return;

        var split = _document.IsSplit;
        var vertical = _document.SplitVertical;

        if (!split)
        {
            _editorSecondary.IsVisible = false;
            _verticalSplitter.IsVisible = false;
            _horizontalSplitter.IsVisible = false;
            _secondaryColumn.Width = new GridLength(0);
            _secondaryRow.Height = new GridLength(0);
            Grid.SetRowSpan(_editor, 3);
            Grid.SetColumnSpan(_editor, 3);
            Grid.SetRow(_editorSecondary, 0);
            Grid.SetColumn(_editorSecondary, 2);
            return;
        }

        _editorSecondary.IsVisible = true;
        if (vertical)
        {
            _verticalSplitter.IsVisible = true;
            _horizontalSplitter.IsVisible = false;
            _secondaryColumn.Width = new GridLength(1, GridUnitType.Star);
            _secondaryRow.Height = new GridLength(0);
            Grid.SetRowSpan(_editor, 3);
            Grid.SetColumnSpan(_editor, 1);
            Grid.SetRow(_editorSecondary, 0);
            Grid.SetRowSpan(_editorSecondary, 3);
            Grid.SetColumn(_editorSecondary, 2);
            Grid.SetColumnSpan(_editorSecondary, 1);
        }
        else
        {
            _verticalSplitter.IsVisible = false;
            _horizontalSplitter.IsVisible = true;
            _secondaryColumn.Width = new GridLength(0);
            _secondaryRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRowSpan(_editor, 1);
            Grid.SetColumnSpan(_editor, 3);
            Grid.SetRow(_editorSecondary, 2);
            Grid.SetRowSpan(_editorSecondary, 1);
            Grid.SetColumn(_editorSecondary, 0);
            Grid.SetColumnSpan(_editorSecondary, 3);
        }
    }

    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        if (_document?.Document == null)
            return;

        _document.Document.Changed -= OnTextDocumentChanged;
        _document.Document.Changed += OnTextDocumentChanged;
    }

    private void OnTextDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        _changeVersion++;
        var version = _changeVersion;
        if (_ghostSuggestion is { IsVisible: true } ghost &&
            ghost.ShouldClearOnDocumentChange(_ghostDocumentVersion, version))
            ClearGhostSuggestion(clearStatus: true);
        ScheduleFoldingUpdate();
        AccumulateLspChange(e);
        _ = NotifyLspChangeAsync(version);
    }

    private void AccumulateLspChange(DocumentChangeEventArgs e)
    {
        if (_document?.IsLargeFile == true || _editor?.Document == null)
            return;

        // Huge paste/replace: one full sync is cheaper and safer than a giant incremental list.
        if (e.RemovalLength + e.InsertionLength > 256_000)
        {
            _pendingLspChanges.Clear();
            _lspNeedsFullSync = true;
            return;
        }

        if (_lspNeedsFullSync)
            return;

        if (LspContentChangeFactory.TryFromDocumentChange(_editor.Document, e, out var change))
            _pendingLspChanges.Add(change);
        else
        {
            _pendingLspChanges.Clear();
            _lspNeedsFullSync = true;
        }
    }

    private void ScheduleFoldingUpdate()
    {
        if (_foldingTimer == null || _document?.IsLargeFile == true)
            return;

        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    public void RefreshGrammar() => ApplyGrammar();

    private void ApplyGrammar()
    {
        if (_textMate == null || _registry == null || _document == null)
            return;

        var probe = LanguageHelper.GetProbePath(_document.FilePath, _document.LanguageOverrideExtension);
        var scope = LanguageHelper.GetScopeName(probe, _registry);
        _textMate.SetGrammar(scope);
        _textMateSecondary?.SetGrammar(scope);
        _document.Language = LanguageHelper.GetLanguageDisplay(probe, _registry);
    }

    private void OnThemeChanged(ThemeName theme)
    {
        if (_textMate == null || _registry == null)
            return;

        _registry = new RegistryOptions(theme);
        var loaded = _registry.LoadTheme(theme);
        _textMate.SetTheme(loaded);
        _textMateSecondary?.SetTheme(loaded);
        ApplyEditorSurfaceColors();
        ApplyGrammar();
    }

    private void ApplyEditorSurfaceColors()
    {
        // Prefer the app theme surface (Hacker / AMOLED / GitHub) over TextMate name heuristics.
        var surface = EditorThemeService.CurrentSurface;
        var bg = Avalonia.Media.Color.Parse(surface.BackgroundHex);
        var fg = Avalonia.Media.Color.Parse(surface.ForegroundHex);
        var bgBrush = new Avalonia.Media.SolidColorBrush(bg);
        var fgBrush = new Avalonia.Media.SolidColorBrush(fg);

        if (_editor != null)
        {
            _editor.Background = bgBrush;
            _editor.Foreground = fgBrush;
        }

        if (_editorSecondary != null)
        {
            _editorSecondary.Background = bgBrush;
            _editorSecondary.Foreground = fgBrush;
        }
    }

    public void ZoomBy(double delta)
    {
        if (_document == null || _settings == null)
            return;

        var next = Math.Clamp(_document.FontSize + delta, 8, 48);
        _document.FontSize = next;
        if (_editor != null) _editor.FontSize = next;
        if (_editorSecondary != null) _editorSecondary.FontSize = next;
        _settings.Update(s => s.FontSize = next);
    }

    public void ResetZoom()
    {
        if (_document == null)
            return;
        _document.FontSize = 13;
        if (_editor != null) _editor.FontSize = 13;
        if (_editorSecondary != null) _editorSecondary.FontSize = 13;
        _settings?.Update(s => s.FontSize = 13);
    }

    private void ApplySettings()
    {
        if (_editor == null || _settings == null || _document == null)
            return;

        var s = _settings.Current;
        _document.FontSize = s.FontSize;
        _document.FontFamily = s.FontFamily;
        _editor.FontSize = s.FontSize;
        ApplyFontFamily(s.FontFamily);
        ApplyEditorOptions(_editor, s);
        if (_editorSecondary != null)
            ApplyEditorOptions(_editorSecondary, s);
        _editor.WordWrap = _document.WordWrap;
        if (_editorSecondary != null)
            _editorSecondary.WordWrap = _document.WordWrap;

        if (_indentGuides != null)
        {
            _indentGuides.IndentSize = Math.Max(1, s.TabSize);
            _indentGuides.Enabled = s.IndentGuides;
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    public void RefreshEditorOptions() => ApplySettings();

    private void ApplyEditorOptions(TextEditor editor, AppSettings s)
    {
        editor.FontSize = s.FontSize;
        editor.Options.IndentationSize = Math.Max(1, s.TabSize);
        editor.Options.ConvertTabsToSpaces = _document?.InsertSpaces ?? s.InsertSpaces;
        editor.Options.ShowSpaces = s.ShowWhitespace;
        editor.Options.ShowTabs = s.ShowWhitespace;
        editor.Options.ShowEndOfLine = s.ShowWhitespace;
    }

    public void DuplicateLine()
    {
        if (_editor?.Document == null)
            return;

        var document = _editor.Document;
        var line = document.GetLineByOffset(_editor.CaretOffset);
        var text = document.GetText(line.Offset, line.TotalLength);
        if (line.DelimiterLength == 0)
            text += Environment.NewLine;

        var insertAt = line.Offset + line.TotalLength;
        document.Insert(insertAt, text);
        _editor.CaretOffset = Math.Min(document.TextLength, insertAt + Math.Max(0, text.Length - line.DelimiterLength));
        _editor.Focus();
    }

    public void MoveLineUp()
    {
        if (_editor?.Document == null)
            return;

        var document = _editor.Document;
        var line = document.GetLineByOffset(_editor.CaretOffset);
        if (line.LineNumber <= 1)
            return;

        var previous = document.GetLineByNumber(line.LineNumber - 1);
        var currentText = document.GetText(line.Offset, line.TotalLength);
        var previousText = document.GetText(previous.Offset, previous.TotalLength);
        var column = _editor.TextArea.Caret.Column;

        using (document.RunUpdate())
        {
            document.Replace(previous.Offset, previous.TotalLength + line.TotalLength, currentText + previousText);
        }

        var newLine = document.GetLineByNumber(line.LineNumber - 1);
        _editor.CaretOffset = document.GetOffset(newLine.LineNumber, Math.Min(column, newLine.Length + 1));
        _editor.Focus();
    }

    public void MoveLineDown()
    {
        if (_editor?.Document == null)
            return;

        var document = _editor.Document;
        var line = document.GetLineByOffset(_editor.CaretOffset);
        if (line.LineNumber >= document.LineCount)
            return;

        var next = document.GetLineByNumber(line.LineNumber + 1);
        var currentText = document.GetText(line.Offset, line.TotalLength);
        var nextText = document.GetText(next.Offset, next.TotalLength);
        var column = _editor.TextArea.Caret.Column;

        // Ensure last line has a delimiter when swapped upward into the middle.
        if (next.DelimiterLength == 0 && !currentText.EndsWith('\n') && !currentText.EndsWith('\r'))
            currentText += Environment.NewLine;

        using (document.RunUpdate())
        {
            document.Replace(line.Offset, line.TotalLength + next.TotalLength, nextText + currentText);
        }

        var newLine = document.GetLineByNumber(line.LineNumber + 1);
        _editor.CaretOffset = document.GetOffset(newLine.LineNumber, Math.Min(column, newLine.Length + 1));
        _editor.Focus();
    }

    public void JumpToMatchingBracket()
    {
        if (_editor?.Document == null)
            return;

        var match = BracketMatcher.FindMatch(_editor.Document, _editor.CaretOffset);
        if (match == null)
            return;

        var caret = _editor.CaretOffset;
        var target = caret - 1 == match.Value.OpenOffset || caret == match.Value.OpenOffset
            ? match.Value.CloseOffset
            : match.Value.OpenOffset;
        _editor.CaretOffset = target;
        _editor.TextArea.Caret.BringCaretToView();
        _editor.Focus();
        UpdateBracketHighlight();
    }

    public int AddNextOccurrence() => _occurrences?.AddNextOccurrence() ?? 0;

    public int SelectAllOccurrences() => _occurrences?.SelectAllOccurrences() ?? 0;

    public bool ToggleBookmark()
    {
        if (_editor == null || _bookmarks == null)
            return false;

        var added = _bookmarks.ToggleAtOffset(_editor.CaretOffset);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _editor.Focus();
        return added;
    }

    public bool GoToNextBookmark()
    {
        if (_editor == null || _bookmarks == null)
            return false;

        var offset = _bookmarks.GoToNext(_editor.CaretOffset);
        if (offset == null)
            return false;

        _editor.CaretOffset = offset.Value;
        _editor.TextArea.Caret.BringCaretToView();
        _editor.Focus();
        return true;
    }

    public bool GoToPreviousBookmark()
    {
        if (_editor == null || _bookmarks == null)
            return false;

        var offset = _bookmarks.GoToPrevious(_editor.CaretOffset);
        if (offset == null)
            return false;

        _editor.CaretOffset = offset.Value;
        _editor.TextArea.Caret.BringCaretToView();
        _editor.Focus();
        return true;
    }

    public void ClearBookmarks()
    {
        if (_editor == null || _bookmarks == null)
            return;

        var doc = _editor.Document;
        _bookmarks.Clear();
        if (doc != null)
            _bookmarks.Attach(doc);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    public void TransformSelectionCase(Func<string, string> transform)
    {
        if (_editor?.Document == null)
            return;

        var selection = _editor.TextArea.Selection;
        if (selection.IsEmpty)
        {
            var word = GetWordSegmentAtCaret();
            if (word == null)
                return;
            var text = _editor.Document.GetText(word.Value.Start, word.Value.Length);
            _editor.Document.Replace(word.Value.Start, word.Value.Length, transform(text));
            _editor.Select(word.Value.Start, transform(text).Length);
        }
        else
        {
            var seg = selection.SurroundingSegment;
            if (seg == null || seg.Length == 0)
                return;
            var text = _editor.Document.GetText(seg);
            var next = transform(text);
            _editor.Document.Replace(seg.Offset, seg.Length, next);
            _editor.Select(seg.Offset, next.Length);
        }

        _editor.Focus();
    }

    public void SortLines(bool descending)
    {
        if (_editor?.Document == null)
            return;

        var document = _editor.Document;
        var selection = _editor.TextArea.Selection;
        int startLine;
        int endLine;

        if (selection.IsEmpty)
        {
            startLine = 1;
            endLine = document.LineCount;
        }
        else
        {
            var seg = selection.SurroundingSegment;
            if (seg == null)
                return;
            startLine = document.GetLineByOffset(seg.Offset).LineNumber;
            endLine = document.GetLineByOffset(Math.Max(seg.Offset, seg.EndOffset - 1)).LineNumber;
        }

        if (endLine <= startLine)
            return;

        var lines = new List<string>(endLine - startLine + 1);
        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            lines.Add(document.GetText(line.Offset, line.Length));
        }

        lines.Sort(StringComparer.OrdinalIgnoreCase);
        if (descending)
            lines.Reverse();

        var first = document.GetLineByNumber(startLine);
        var last = document.GetLineByNumber(endLine);
        var startOffset = first.Offset;
        var length = last.Offset + last.Length - startOffset;
        var joined = string.Join("\n", lines);
        // Preserve last line's delimiter outside the replaced content length.
        document.Replace(startOffset, length, joined);
        _editor.Select(startOffset, joined.Length);
        _editor.Focus();
    }

    /// <summary>
    /// Joins selected lines (or the current line with the next) into one line, separated by a space.
    /// </summary>
    public int JoinLines()
    {
        if (_editor?.Document == null)
            return 0;

        var document = _editor.Document;
        var selection = _editor.TextArea.Selection;
        int startLine;
        int endLine;

        if (selection.IsEmpty)
        {
            startLine = document.GetLineByOffset(_editor.CaretOffset).LineNumber;
            endLine = Math.Min(document.LineCount, startLine + 1);
        }
        else
        {
            var seg = selection.SurroundingSegment;
            if (seg == null)
                return 0;
            startLine = document.GetLineByOffset(seg.Offset).LineNumber;
            endLine = document.GetLineByOffset(Math.Max(seg.Offset, seg.EndOffset - 1)).LineNumber;
        }

        if (endLine <= startLine)
            return 0;

        var parts = new List<string>(endLine - startLine + 1);
        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            parts.Add(document.GetText(line.Offset, line.Length).Trim());
        }

        var first = document.GetLineByNumber(startLine);
        var last = document.GetLineByNumber(endLine);
        var startOffset = first.Offset;
        var length = last.Offset + last.TotalLength - startOffset;
        // Keep the last line's delimiter if it had one (unless joining into EOF).
        var trailing = last.DelimiterLength > 0
            ? document.GetText(last.Offset + last.Length, last.DelimiterLength)
            : "";
        var joined = string.Join(' ', parts.Where(p => p.Length > 0));
        document.Replace(startOffset, length, joined + trailing);
        _editor.CaretOffset = startOffset + joined.Length;
        _editor.Focus();
        return endLine - startLine;
    }

    /// <summary>Removes blank / whitespace-only lines in the selection, or whole document.</summary>
    public int RemoveEmptyLines()
    {
        if (_editor?.Document == null)
            return 0;

        var (startLine, endLine) = GetTransformLineRange();
        var document = _editor.Document;
        var kept = new List<string>();
        var removed = 0;
        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);
            if (string.IsNullOrWhiteSpace(text))
            {
                removed++;
                continue;
            }
            kept.Add(text);
        }

        if (removed == 0)
            return 0;

        ReplaceLineRange(startLine, endLine, kept);
        return removed;
    }

    /// <summary>Removes consecutive duplicate lines (case-sensitive) in selection or whole document.</summary>
    public int RemoveDuplicateLines()
    {
        if (_editor?.Document == null)
            return 0;

        var (startLine, endLine) = GetTransformLineRange();
        var document = _editor.Document;
        var kept = new List<string>();
        string? previous = null;
        var removed = 0;
        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);
            if (previous != null && string.Equals(previous, text, StringComparison.Ordinal))
            {
                removed++;
                continue;
            }
            kept.Add(text);
            previous = text;
        }

        if (removed == 0)
            return 0;

        ReplaceLineRange(startLine, endLine, kept);
        return removed;
    }

    private (int StartLine, int EndLine) GetTransformLineRange()
    {
        var document = _editor!.Document;
        var selection = _editor.TextArea.Selection;
        if (selection.IsEmpty)
            return (1, document.LineCount);

        var seg = selection.SurroundingSegment;
        if (seg == null)
            return (1, document.LineCount);

        var startLine = document.GetLineByOffset(seg.Offset).LineNumber;
        var endLine = document.GetLineByOffset(Math.Max(seg.Offset, seg.EndOffset - 1)).LineNumber;
        return (startLine, endLine);
    }

    private void ReplaceLineRange(int startLine, int endLine, List<string> lines)
    {
        var document = _editor!.Document;
        var first = document.GetLineByNumber(startLine);
        var last = document.GetLineByNumber(endLine);
        var startOffset = first.Offset;
        var length = last.Offset + last.Length - startOffset;
        var joined = string.Join("\n", lines);
        document.Replace(startOffset, length, joined);
        _editor.Select(startOffset, joined.Length);
        _editor.Focus();
    }

    public int TrimTrailingWhitespace()
    {
        if (_editor?.Document == null)
            return 0;

        var document = _editor.Document;
        var changed = 0;
        using (document.RunUpdate())
        {
            for (var i = document.LineCount; i >= 1; i--)
            {
                var line = document.GetLineByNumber(i);
                if (line.Length == 0)
                    continue;

                var text = document.GetText(line.Offset, line.Length);
                var trimmed = text.TrimEnd(' ', '\t');
                if (trimmed.Length == text.Length)
                    continue;

                document.Replace(line.Offset, line.Length, trimmed);
                changed++;
            }
        }

        return changed;
    }

    public bool EnsureFinalNewline()
    {
        if (_editor?.Document == null)
            return false;

        var document = _editor.Document;
        if (document.TextLength == 0)
            return false;

        var last = document.GetCharAt(document.TextLength - 1);
        if (last is '\n' or '\r')
            return false;

        var ending = (_document?.LineEndingKind ?? LineEndingKind.Lf).ToNewLine();
        document.Insert(document.TextLength, ending);
        return true;
    }

    private (int Start, int Length)? GetWordSegmentAtCaret()
    {
        if (_editor?.Document == null)
            return null;

        var document = _editor.Document;
        var caret = Math.Clamp(_editor.CaretOffset, 0, document.TextLength);
        var start = caret;
        while (start > 0)
        {
            var c = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            start--;
        }

        var end = caret;
        while (end < document.TextLength)
        {
            var c = document.GetCharAt(end);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            end++;
        }

        return end > start ? (start, end - start) : null;
    }

    public void InsertTextAtCaret(string text)
    {
        if (_editor?.Document == null || string.IsNullOrEmpty(text))
            return;

        _editor.Document.Insert(_editor.CaretOffset, text);
        _editor.Focus();
    }

    public void ToggleLineComment()
    {
        if (_editor?.Document == null || _document?.IsReadOnly == true)
            return;

        var block = LanguageHelper.GetBlockCommentDelimiters(
            _document?.FilePath,
            _document?.LanguageOverrideExtension);
        if (block != null)
        {
            ToggleBlockComment(block.Value.Open, block.Value.Close);
            return;
        }

        var prefix = LanguageHelper.GetLineCommentPrefix(
            _document?.FilePath,
            _document?.LanguageOverrideExtension) ?? "//";
        var document = _editor.Document;
        var selection = _editor.TextArea.Selection;
        int startLine;
        int endLine;

        if (selection.IsEmpty)
        {
            startLine = endLine = document.GetLineByOffset(_editor.CaretOffset).LineNumber;
        }
        else
        {
            var seg = selection.SurroundingSegment;
            if (seg == null)
                return;
            startLine = document.GetLineByOffset(seg.Offset).LineNumber;
            endLine = document.GetLineByOffset(Math.Max(seg.Offset, seg.EndOffset - 1)).LineNumber;
        }

        var allCommented = true;
        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (!IsLineCommented(text, prefix))
            {
                allCommented = false;
                break;
            }
        }

        using (document.RunUpdate())
        {
            for (var i = endLine; i >= startLine; i--)
            {
                var line = document.GetLineByNumber(i);
                var text = document.GetText(line.Offset, line.Length);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (allCommented)
                {
                    var stripped = StripLineComment(text, prefix);
                    document.Replace(line.Offset, line.Length, stripped);
                }
                else
                {
                    var indentLen = text.TakeWhile(c => c is ' ' or '\t').Count();
                    document.Insert(line.Offset + indentLen, prefix);
                }
            }
        }

        _editor.Focus();
    }

    private void ToggleBlockComment(string open, string close)
    {
        if (_editor?.Document == null)
            return;

        var document = _editor.Document;
        var selection = _editor.TextArea.Selection;
        int startOffset;
        int endOffset;

        if (selection.IsEmpty)
        {
            var line = document.GetLineByOffset(_editor.CaretOffset);
            startOffset = line.Offset;
            endOffset = line.Offset + line.Length;
        }
        else
        {
            var seg = selection.SurroundingSegment;
            if (seg == null)
                return;
            startOffset = seg.Offset;
            endOffset = seg.EndOffset;
        }

        var length = Math.Max(0, endOffset - startOffset);
        var text = document.GetText(startOffset, length);
        var trimmedStart = text.TrimStart();
        var leading = text.Length - trimmedStart.Length;
        var trimmed = trimmedStart.TrimEnd();
        var trailing = trimmedStart.Length - trimmed.Length;

        if (trimmed.StartsWith(open, StringComparison.Ordinal) &&
            trimmed.EndsWith(close, StringComparison.Ordinal) &&
            trimmed.Length >= open.Length + close.Length)
        {
            var inner = trimmed[open.Length..^close.Length];
            var replacement = text[..leading] + inner + text[(text.Length - trailing)..];
            document.Replace(startOffset, length, replacement);
        }
        else
        {
            document.Replace(startOffset, length, open + text + close);
        }

        _editor.Focus();
    }

    private static bool IsLineCommented(string text, string prefix)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string StripLineComment(string text, string prefix)
    {
        var indentLen = text.TakeWhile(c => c is ' ' or '\t').Count();
        var rest = text[indentLen..];
        if (!rest.StartsWith(prefix, StringComparison.Ordinal))
            return text;

        rest = rest[prefix.Length..];
        if (prefix != "REM " && rest.StartsWith(' '))
            rest = rest[1..];
        return text[..indentLen] + rest;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        UpdateCaretStatus();
        _occurrences?.OnCaretMoved();
        ScheduleCaretSettledHighlights();
        if (_editor != null &&
            _ghostSuggestion is { IsVisible: true } ghost &&
            ghost.ShouldClearOnCaretMove(_editor.CaretOffset))
            ClearGhostSuggestion(clearStatus: true);
    }

    private void UpdateBracketHighlight()
    {
        if (_editor?.Document == null || _brackets == null)
            return;

        _brackets.SetMatch(BracketMatcher.FindMatch(_editor.Document, _editor.CaretOffset));
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void ScheduleCaretSettledHighlights()
    {
        if (_wordHighlightTimer == null)
            return;
        _wordHighlightTimer.Stop();
        _wordHighlightTimer.Start();
    }

    private void UpdateWordHighlight()
    {
        if (_editor?.Document == null || _wordHighlight == null)
            return;

        // Skip while an explicit multi-occurrence session is active.
        if (_occurrences is { Count: > 1 })
        {
            _wordHighlight.Clear();
        }
        else
        {
            _wordHighlight.Update(_editor.Document, _editor.CaretOffset);
        }

        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void UpdateCaretStatus()
    {
        if (_editor == null || _document == null)
            return;

        var caret = _editor.TextArea.Caret;
        _document.UpdateCaret(caret.Line, caret.Column);
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow != null && e.Text is { Length: > 0 } && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
            _completionWindow.CompletionList.RequestInsertion(e);

        if (e.Text is { Length: > 0 } && _occurrences?.TryHandleTextInput(e.Text) == true)
        {
            e.Handled = true;
            return;
        }

        // Skip over an already auto-inserted closer instead of doubling it.
        if (_settings?.Current.AutoCloseBrackets == true &&
            _editor?.Document != null &&
            e.Text is { Length: 1 } text &&
            AutoCloseBrackets.ShouldSkipTypingCloser(_editor.Document, _editor.CaretOffset, text[0]))
        {
            _editor.CaretOffset++;
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_editor == null || _document == null || e.Text is not { Length: > 0 })
            return;

        var ch = e.Text[0];
        if ((ch is '\n' or '\r') && _settings?.Current.AutoIndent != false)
            ApplyAutoIndent();

        if (_settings?.Current.AutoCloseBrackets == true && AutoCloseBrackets.IsOpener(ch))
            AutoCloseBracket(ch);

        if (_settings?.Current.WordCompletion != false)
            MaybeShowWordCompletion(ch);

        ScheduleCaretSettledHighlights();
        _ = MaybeShowLspCompletionAsync(ch);
        ScheduleAutomaticAiCompletion();
    }

    private void ApplyAutoIndent()
    {
        if (_editor?.Document == null)
            return;

        var document = _editor.Document;
        var caret = _editor.CaretOffset;
        var line = document.GetLineByOffset(caret);
        if (line.LineNumber <= 1)
            return;

        var previous = document.GetLineByNumber(line.LineNumber - 1);
        var previousText = document.GetText(previous.Offset, previous.Length);
        var tabSize = Math.Max(1, _settings?.Current.TabSize ?? 4);
        var insertSpaces = _document?.InsertSpaces ?? _settings?.Current.InsertSpaces != false;
        var indent = AutoIndent.ComputeNextLineIndent(previousText, tabSize, insertSpaces);

        // Dedent when the new line starts with a closing bracket typed later; for Enter alone
        // just copy (+ bump) indent from the previous line.
        if (indent.Length == 0)
            return;

        document.Insert(caret, indent);
        _editor.CaretOffset = caret + indent.Length;
    }

    private void AutoCloseBracket(char open)
    {
        if (_editor == null || !AutoCloseBrackets.TryGetCloser(open, out var close))
            return;

        var offset = _editor.CaretOffset;
        _editor.Document.Insert(offset, close.ToString());
        _editor.CaretOffset = offset;
    }

    private void MaybeShowWordCompletion(char ch)
    {
        if (_editor == null || (!char.IsLetter(ch) && ch != '_'))
            return;

        ShowWordCompletion(force: false);
    }

    public void TriggerWordCompletion() => ShowWordCompletion(force: true);

    public void TriggerAiCompletion() => _ = RequestAiCompletionAsync(manual: true);

    private void ShowWordCompletion(bool force)
    {
        if (_editor == null || _document == null || _document.IsLargeFile || _document.IsReadOnly)
            return;

        var word = GetWordBeforeCaret();
        if (!force && word.Length < 2)
            return;

        var candidates = WordCompletion.RankCandidates(
            _wordListCache.GetOrCollect(_editor.Document.Text, _document.LspVersion),
            word,
            limit: 40,
            allowEmptyPrefix: force);

        if (candidates.Count == 0)
            return;

        ShowCompletion(candidates.Select(c => new WordCompletionData(c)));
    }

    private async Task MaybeShowLspCompletionAsync(char ch)
    {
        if (_editor == null || _document == null || _lsp == null || _document.IsLargeFile)
            return;
        if (!char.IsLetterOrDigit(ch) && ch is not ('.' or '_'))
            return;
        if (_completionWindow != null)
            return;

        _lspCompletionCts?.Cancel();
        _lspCompletionCts?.Dispose();
        _lspCompletionCts = new CancellationTokenSource();
        var ct = _lspCompletionCts.Token;
        var version = ++_lspCompletionVersion;

        try
        {
            await Task.Delay(200, ct);
            if (version != _lspCompletionVersion || ct.IsCancellationRequested)
                return;

            var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
            var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
            if (client == null || ct.IsCancellationRequested || version != _lspCompletionVersion)
                return;

            var uri = ToUri(_document.FilePath);
            if (uri == null)
                return;

            var caret = _editor.TextArea.Caret;
            var items = await client.CompletionAsync(uri, caret.Line - 1, caret.Column - 1);
            if (items.Count == 0 || ct.IsCancellationRequested || version != _lspCompletionVersion)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _lspCompletionVersion)
                    return;
                ShowCompletion(items.Select(i => new WordCompletionData(
                    i.InsertText ?? i.Label,
                    i.Detail ?? i.Label)));
            });
        }
        catch (OperationCanceledException)
        {
            // Newer keystroke cancelled this request.
        }
    }

    private void ShowCompletion(IEnumerable<WordCompletionData> items)
    {
        if (_editor == null)
            return;

        // Word/LSP popup wins over AI ghost text.
        ClearGhostSuggestion();
        _completionWindow?.Close();
        _completionWindow = new CompletionWindow(_editor.TextArea);
        var prefix = GetWordBeforeCaret();
        _completionWindow.StartOffset = Math.Max(0, _editor.CaretOffset - prefix.Length);
        _completionWindow.Closed += (_, _) => _completionWindow = null;
        foreach (var item in items)
            _completionWindow.CompletionList.CompletionData.Add(item);
        if (_completionWindow.CompletionList.CompletionData.Count > 0)
            _completionWindow.Show();
        else
            _completionWindow = null;
    }

    private string GetWordBeforeCaret()
    {
        if (_editor == null)
            return "";

        var offset = _editor.CaretOffset;
        var start = offset;
        while (start > 0)
        {
            var c = _editor.Document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            start--;
        }
        return _editor.Document.GetText(start, offset - start);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_ghostSuggestion is { IsVisible: true })
            {
                ClearGhostSuggestion(clearStatus: true);
                e.Handled = true;
                return;
            }

            if (_occurrences is { Count: > 0 })
            {
                _occurrences.Clear();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Tab &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
            _completionWindow == null &&
            TryAcceptGhostSuggestion())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && _occurrences?.TryHandleBackspace() == true)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowWordCompletion(force: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = ShowLspHoverAsync();
            e.Handled = true;
        }
    }

    private void InstallGhostText()
    {
        if (_editor == null)
            return;
        if (!_editor.TextArea.TextView.BackgroundRenderers.Contains(_ghostRenderer))
            _editor.TextArea.TextView.BackgroundRenderers.Add(_ghostRenderer);
    }

    private void UninstallGhostText()
    {
        if (_editor != null)
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_ghostRenderer);
        ClearGhostSuggestion();
    }

    private void ClearGhostSuggestion(bool clearStatus = false, bool restoreAcceptsTabImmediately = true)
    {
        if (_ghostSuggestion == null && _ghostRenderer.Suggestion == null)
        {
            if (restoreAcceptsTabImmediately)
                RestoreAcceptsTabAfterGhost();
            return;
        }

        _ghostSuggestion = null;
        _ghostRenderer.Suggestion = null;
        _ghostDocumentVersion = -1;
        if (restoreAcceptsTabImmediately)
            RestoreAcceptsTabAfterGhost();
        _editor?.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        if (clearStatus)
            SetEditorStatus("");
    }

    private void ShowGhostSuggestion(InlineGhostSuggestion suggestion, int docVersion)
    {
        _ghostSuggestion = suggestion;
        _ghostDocumentVersion = docVersion;
        _ghostRenderer.Suggestion = suggestion;
        // TextArea.OnKeyDown ignores Handled and always indents when AcceptsTab is true.
        if (_editor != null && !_ghostDisabledAcceptsTab)
        {
            _editor.Options.AcceptsTab = false;
            _ghostDisabledAcceptsTab = true;
        }

        _editor?.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        var preview = suggestion.StatusPreview();
        var status = $"AI ready · Tab ⇥ {preview} · Esc dismiss";
        SetEditorStatus(status);
        AiDebugLog.Shared.Write("ghost",
            $"show offset={suggestion.Offset} len={suggestion.Text.Length} preview={AiDebugLog.SummarizeBody(suggestion.DisplayLine)}");
    }

    private void RestoreAcceptsTabAfterGhost()
    {
        if (!_ghostDisabledAcceptsTab || _editor == null)
            return;
        _editor.Options.AcceptsTab = true;
        _ghostDisabledAcceptsTab = false;
    }

    private bool TryAcceptGhostSuggestion()
    {
        if (_editor?.Document == null || _ghostSuggestion is not { IsVisible: true } ghost)
            return false;

        var insert = ghost.TryAccept(_editor.CaretOffset);
        if (insert == null)
            return false;

        // Keep AcceptsTab=false through the rest of this KeyDown so TextArea.OnKeyDown skips OnTab.
        ClearGhostSuggestion(restoreAcceptsTabImmediately: false);
        var offset = _editor.CaretOffset;
        _editor.Document.Insert(offset, insert);
        _editor.CaretOffset = offset + insert.Length;
        SetEditorStatus("AI completion accepted");
        AiDebugLog.Shared.Write("ghost", $"accepted len={insert.Length}");
        Dispatcher.UIThread.Post(RestoreAcceptsTabAfterGhost);
        return true;
    }

    private void ScheduleAutomaticAiCompletion()
    {
        if (_aiIdleTimer == null || _settings?.Current is not { } s)
            return;
        if (!s.AiCompletionEnabled || !s.AiAutomatic || string.IsNullOrWhiteSpace(s.AiBaseUrl))
            return;

        _aiIdleTimer.Stop();
        _aiIdleTimer.Start();
    }

    private async Task RequestAiCompletionAsync(bool manual)
    {
        if (_editor == null || _document == null || _settings == null)
            return;
        if (_document.IsLargeFile || _document.IsReadOnly)
        {
            if (manual)
                SetEditorStatus(_document.IsReadOnly
                    ? "AI completions unavailable in read-only buffers"
                    : "AI completions skipped for large files");
            return;
        }

        var s = _settings.Current;
        if (!s.AiCompletionEnabled || string.IsNullOrWhiteSpace(s.AiBaseUrl) || string.IsNullOrWhiteSpace(s.AiModel))
        {
            if (manual)
                SetEditorStatus("AI completions are off — enable them in Preferences → AI");
            return;
        }

        if (!manual && !s.AiAutomatic)
            return;

        if (DateTime.UtcNow < _aiRateLimitedUntilUtc)
        {
            if (manual)
                SetEditorStatus("AI rate limited — waiting before next request");
            return;
        }

        // Popup list wins over ghost text.
        if (_completionWindow != null)
        {
            if (manual)
                SetEditorStatus("Dismiss the completion list before AI suggest");
            return;
        }

        _aiCompletionCts?.Cancel();
        _aiCompletionCts?.Dispose();
        _aiCompletionCts = new CancellationTokenSource();
        var ct = _aiCompletionCts.Token;
        var version = ++_aiCompletionVersion;
        var caret = _editor.CaretOffset;
        var text = _editor.Document.Text;
        var docVersion = _changeVersion;

        var (prefix, suffix) = InlineCompletionPrompt.ExtractContext(text, caret);
        var language = _document.Language;
        if (string.IsNullOrWhiteSpace(language) || language == "Plain Text")
        {
            var probe = LanguageHelper.GetProbePath(_document.FilePath, _document.LanguageOverrideExtension);
            language = Path.GetExtension(probe ?? "")?.TrimStart('.') ?? "text";
        }

        var userMessage = InlineCompletionPrompt.BuildUserMessage(prefix, suffix, language);
        _aiClient ??= new OpenAiChatClient();

        if (manual)
            SetEditorStatus("AI suggesting…");

        try
        {
            var result = await _aiClient.CompleteAsync(
                new AiCompletionRequest(
                    s.AiBaseUrl,
                    s.AiModel,
                    s.AiApiKey,
                    InlineCompletionPrompt.SystemPrompt,
                    userMessage),
                ct).ConfigureAwait(false);

            if (version != _aiCompletionVersion || ct.IsCancellationRequested)
                return;

            if (result.RateLimited)
            {
                _aiRateLimitedUntilUtc = DateTime.UtcNow.AddMinutes(1);
                await Dispatcher.UIThread.InvokeAsync(() =>
                    SetEditorStatus(result.Error ?? "AI rate limited (429)"));
                return;
            }

            if (!result.Ok)
            {
                if (manual)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        SetEditorStatus(result.Error ?? "AI suggestion failed"));
                }
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _aiCompletionVersion || _editor == null)
                    return;
                if (_editor.CaretOffset != caret || _changeVersion != docVersion)
                {
                    if (manual)
                        SetEditorStatus("AI suggestion discarded (editor changed)");
                    return;
                }
                if (_completionWindow != null)
                {
                    if (manual)
                        SetEditorStatus("AI suggestion discarded (completion list open)");
                    return;
                }

                var suffix = caret < _editor.Document.TextLength
                    ? _editor.Document.GetText(caret, Math.Min(512, _editor.Document.TextLength - caret))
                    : "";
                var suggestion = InlineGhostSuggestion.FromCleaned(caret, result.Text, suffix);
                if (suggestion == null)
                {
                    if (manual)
                        SetEditorStatus(string.IsNullOrEmpty(result.Text)
                            ? "AI returned an empty suggestion"
                            : "AI suggestion discarded (already in buffer)");
                    return;
                }

                ShowGhostSuggestion(suggestion, docVersion);
                if (result.UsedAnonymousFallback)
                {
                    // Keep the Tab preview first; append auth hint after a short delay via status.
                    SetEditorStatus(
                        $"AI ready · Tab ⇥ {suggestion.StatusPreview()} · Esc · (anonymous — fix API key)");
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Newer keystroke or detach cancelled this request.
            if (manual && version == _aiCompletionVersion)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    SetEditorStatus("AI suggestion cancelled"));
            }
        }
        catch (Exception ex)
        {
            if (manual)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    SetEditorStatus($"AI error: {ex.Message}"));
            }
        }
    }

    private void SetEditorStatus(string text)
    {
        var window = this.FindAncestorOfType<MainWindow>();
        if (window?.DataContext is MainWindowViewModel vm)
            vm.StatusText = text;
    }

    private async Task ShowLspHoverAsync()
    {
        if (_editor == null || _document == null || _lsp == null)
            return;

        var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
        var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
        if (client == null)
            return;

        var uri = ToUri(_document.FilePath);
        if (uri == null)
            return;

        var caret = _editor.TextArea.Caret;
        var hover = await client.HoverAsync(uri, caret.Line - 1, caret.Column - 1);
        if (string.IsNullOrWhiteSpace(hover))
            return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null) return;
            var dialog = new Window
            {
                Title = "Hover",
                Width = 480,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = hover,
                        Margin = new Thickness(12),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            };
            await dialog.ShowDialog(owner);
        });
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_editor == null || _document == null || _settings == null)
            return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        var delta = e.Delta.Y > 0 ? 1 : -1;
        var next = Math.Clamp(_document.FontSize + delta, 8, 48);
        _document.FontSize = next;
        _editor.FontSize = next;
        _settings.Update(s => s.FontSize = next);
        e.Handled = true;
    }

    public void SelectCurrentLine()
    {
        if (_editor?.Document == null)
            return;

        var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
        _editor.Select(line.Offset, line.Length);
        _editor.Focus();
    }

    public void FoldAll()
    {
        UpdateFolding();
        if (_foldingManager == null)
            return;
        foreach (var folding in _foldingManager.AllFoldings)
            folding.IsFolded = true;
        _editor?.Focus();
    }

    public void UnfoldAll()
    {
        if (_foldingManager == null)
            return;
        foreach (var folding in _foldingManager.AllFoldings)
            folding.IsFolded = false;
        _editor?.Focus();
    }

    private void UpdateFolding()
    {
        if (_editor?.Document == null || _document?.IsLargeFile == true)
            return;

        // FoldingElementGenerator throws "Invalid document" when the manager
        // is still bound to a previous TextDocument after a tab/document swap.
        if (_foldingManager == null || !ReferenceEquals(_foldingDocument, _editor.Document))
        {
            UninstallFolding();
            _foldingManager = FoldingManager.Install(_editor.TextArea);
            _foldingDocument = _editor.Document;
        }

        try
        {
            _foldingStrategy.UpdateFoldings(_foldingManager, _editor.Document);
        }
        catch
        {
            // Ignore transient folding mismatches during document swaps.
            try
            {
                UninstallFolding();
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task OpenLspDocumentAsync()
    {
        if (_document == null || _lsp == null || string.IsNullOrWhiteSpace(_document.FilePath) ||
            _document.IsLargeFile)
            return;

        var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
        var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
        if (client == null)
            return;

        var uri = ToUri(_document.FilePath);
        if (uri == null)
            return;

        await client.DidOpenAsync(uri, languageId, _document.Text, _document.LspVersion);
    }

    private async Task NotifyLspChangeAsync(int version)
    {
        await Task.Delay(300);
        if (version != _changeVersion || _document == null || _lsp == null || _document.IsLargeFile)
            return;

        var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
        var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
        if (client == null)
            return;

        var uri = ToUri(_document.FilePath);
        if (uri == null)
            return;

        var needsFull = _lspNeedsFullSync || _pendingLspChanges.Count == 0;
        var incremental = needsFull ? Array.Empty<LspContentChange>() : _pendingLspChanges.ToArray();
        _pendingLspChanges.Clear();
        _lspNeedsFullSync = false;

        if (needsFull || !client.SupportsIncrementalSync)
            await client.DidChangeAsync(uri, _document.Text, _document.LspVersion);
        else
            await client.DidChangeAsync(uri, _document.LspVersion, incremental, _document.Text);
    }

    private async Task CloseLspDocumentAsync()
    {
        if (_document?.FilePath == null || _lsp == null)
            return;

        var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
        var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
        var uri = ToUri(_document.FilePath);
        if (client != null && uri != null)
            await client.DidCloseAsync(uri);
    }

    private static string? ToUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }
}

/// <summary>
/// Simple brace-based folding (AvaloniaEdit does not ship BraceFoldingStrategy in all builds).
/// </summary>
internal sealed class BraceFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<int>();

        for (var i = 0; i < document.TextLength; i++)
        {
            var c = document.GetCharAt(i);
            if (c == '{')
                stack.Push(i);
            else if (c == '}' && stack.Count > 0)
            {
                var start = stack.Pop();
                if (i - start > 2)
                    foldings.Add(new NewFolding(start, i + 1));
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        manager.UpdateFoldings(foldings, -1);
    }
}
