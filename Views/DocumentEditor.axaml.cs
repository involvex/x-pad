using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        _wordHighlightTimer?.Stop();
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
        ApplyEditorSurfaceColors(theme);
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
        _editor.TextArea.KeyDown += OnEditorKeyDown;
        _editor.PointerWheelChanged += OnPointerWheelChanged;
        _editor.PointerMoved += OnEditorPointerMoved;
        _editor.PointerExited += OnEditorPointerExited;
        _editor.DocumentChanged += OnEditorDocumentChanged;

        EditorThemeService.ThemeChanged += OnThemeChanged;

        _foldingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _foldingTimer.Tick += (_, _) => UpdateFolding();
        _foldingTimer.Start();

        _wordHighlightTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _wordHighlightTimer.Tick += (_, _) =>
        {
            _wordHighlightTimer.Stop();
            UpdateWordHighlight();
        };

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
        _editor.Document = _document.Document;
        if (_editorSecondary != null)
            _editorSecondary.Document = _document.Document;
        _foldingManager = FoldingManager.Install(_editor.TextArea);
        _foldingDocument = _editor.Document;
        InstallDiagnostics();
        InstallIndentGuides();
        InstallBrackets();
        InstallOccurrences();
        InstallWordHighlight();
        InstallBookmarks();

        ApplyGrammar();
        ApplySettings();
        ApplySplitLayout();
        UpdateFolding();
        UpdateBracketHighlight();
        UpdateWordHighlight();
        ApplyPendingCaretRestore();
        UpdateCaretStatus();
        _ = OpenLspDocumentAsync();
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
        if (_editor == null)
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
        _ = NotifyLspChangeAsync(version);
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
        ApplyEditorSurfaceColors(theme);
        ApplyGrammar();
    }

    private void ApplyEditorSurfaceColors(ThemeName theme)
    {
        var light = theme is ThemeName.Light
            or ThemeName.LightPlus
            or ThemeName.QuietLight
            or ThemeName.AtomOneLight
            or ThemeName.VisualStudioLight
            or ThemeName.HighContrastLight
            or ThemeName.SolarizedLight;

        // TextMate should own colors; force a matching surface so Fluent DynamicResource
        // (or a missed SetTheme) cannot leave a white editor on dark chrome.
        var bg = Avalonia.Media.Color.Parse(light ? "#FFFFFF" : "#1E1E1E");
        var fg = Avalonia.Media.Color.Parse(light ? "#1E1E1E" : "#D4D4D4");
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
        if (_editor?.Document == null)
            return;

        var prefix = LanguageHelper.GetLineCommentPrefix(_document?.FilePath) ?? "//";
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
        UpdateBracketHighlight();
        _occurrences?.OnCaretMoved();
        ScheduleWordHighlight();
    }

    private void UpdateBracketHighlight()
    {
        if (_editor?.Document == null || _brackets == null)
            return;

        _brackets.SetMatch(BracketMatcher.FindMatch(_editor.Document, _editor.CaretOffset));
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void ScheduleWordHighlight()
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

        UpdateBracketHighlight();
        _ = MaybeShowLspCompletionAsync(ch);
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

    private void ShowWordCompletion(bool force)
    {
        if (_editor == null)
            return;

        var word = GetWordBeforeCaret();
        if (!force && word.Length < 2)
            return;

        var candidates = WordCompletion.RankCandidates(
            WordCompletion.CollectWords(_editor.Document.Text),
            word,
            limit: 40,
            allowEmptyPrefix: force);

        if (candidates.Count == 0)
            return;

        ShowCompletion(candidates.Select(c => new WordCompletionData(c)));
    }

    private async Task MaybeShowLspCompletionAsync(char ch)
    {
        if (_editor == null || _document == null || _lsp == null)
            return;
        if (!char.IsLetterOrDigit(ch) && ch is not ('.' or '_'))
            return;

        var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
        var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
        if (client == null)
            return;

        var uri = ToUri(_document.FilePath);
        if (uri == null)
            return;

        var caret = _editor.TextArea.Caret;
        var items = await client.CompletionAsync(uri, caret.Line - 1, caret.Column - 1);
        if (items.Count == 0)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ShowCompletion(items.Select(i => new WordCompletionData(
                i.InsertText ?? i.Label,
                i.Detail ?? i.Label)));
        });
    }

    private void ShowCompletion(IEnumerable<WordCompletionData> items)
    {
        if (_editor == null)
            return;

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
        if (e.Key == Key.Escape && _occurrences is { Count: > 0 })
        {
            _occurrences.Clear();
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
        if (_editor?.Document == null)
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
        if (_document == null || _lsp == null || string.IsNullOrWhiteSpace(_document.FilePath))
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
        if (version != _changeVersion || _document == null || _lsp == null)
            return;

        var languageId = LanguageHelper.GetLspLanguageId(_document.FilePath);
        var client = await _lsp.GetClientAsync(languageId, _document.FilePath);
        if (client == null)
            return;

        var uri = ToUri(_document.FilePath);
        if (uri == null)
            return;

        await client.DidChangeAsync(uri, _document.Text, _document.LspVersion);
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
