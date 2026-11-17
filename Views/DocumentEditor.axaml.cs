using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
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
    private static readonly Regex WordRegex = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly char[] OpenBrackets = ['(', '[', '{', '"', '\'', '`'];
    private static readonly Dictionary<char, char> BracketPairs = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['"'] = '"',
        ['\''] = '\'',
        ['`'] = '`'
    };

    private TextEditor? _editor;
    private TextMate.Installation? _textMate;
    private RegistryOptions? _registry;
    private SearchPanel? _searchPanel;
    private CompletionWindow? _completionWindow;
    private FoldingManager? _foldingManager;
    private readonly BraceFoldingStrategy _foldingStrategy = new();
    private DispatcherTimer? _foldingTimer;
    private SettingsService? _settings;
    private LspManager? _lsp;
    private DocumentViewModel? _document;
    private bool _initialized;
    private int _changeVersion;

    public DocumentEditor()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public void ConfigureServices(SettingsService settings, LspManager lsp)
    {
        _settings = settings;
        _lsp = lsp;
        ApplySettings();
    }

    public void OpenFind() => _searchPanel?.Open();

    public void OpenReplace()
    {
        if (_searchPanel == null) return;
        var replaceMode = _searchPanel.GetType().GetProperty("IsReplaceMode");
        replaceMode?.SetValue(_searchPanel, true);
        _searchPanel.Open();
        _editor?.Focus();
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
        _foldingTimer?.Stop();
        UninstallFolding();
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
        if (_editor == null)
            return;

        _initialized = true;
        _registry = new RegistryOptions(EditorThemeService.CurrentTextMateTheme);
        _textMate = _editor.InstallTextMate(_registry);
        _searchPanel = SearchPanel.Install(_editor);

        _editor.Options.EnableTextDragDrop = true;
        _editor.Options.AllowScrollBelowDocument = true;
        _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.KeyDown += OnEditorKeyDown;
        _editor.PointerWheelChanged += OnPointerWheelChanged;
        _editor.DocumentChanged += OnEditorDocumentChanged;

        EditorThemeService.ThemeChanged += OnThemeChanged;

        _foldingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _foldingTimer.Tick += (_, _) => UpdateFolding();
        _foldingTimer.Start();

        BindDocument();
        ApplySettings();
        UpdateCaretStatus();
    }

    private void BindDocument()
    {
        if (_editor == null || _document == null)
            return;

        // FoldingManager must be installed on the active document. Replacing
        // TextEditor.Document after Install leaves a stale manager and crashes layout.
        UninstallFolding();
        _editor.Document = _document.Document;
        _foldingManager = FoldingManager.Install(_editor.TextArea);

        ApplyGrammar();
        ApplySettings();
        UpdateFolding();
        UpdateCaretStatus();
        _ = OpenLspDocumentAsync();
    }

    private void UninstallFolding()
    {
        if (_foldingManager == null)
            return;

        FoldingManager.Uninstall(_foldingManager);
        _foldingManager = null;
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.FilePath) or nameof(DocumentViewModel.Language))
        {
            ApplyGrammar();
            _ = OpenLspDocumentAsync();
        }

        if (e.PropertyName == nameof(DocumentViewModel.WordWrap) && _editor != null && _document != null)
            _editor.WordWrap = _document.WordWrap;

        if (e.PropertyName == nameof(DocumentViewModel.FontSize) && _editor != null && _document != null)
            _editor.FontSize = _document.FontSize;
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

    private void ApplyGrammar()
    {
        if (_textMate == null || _registry == null || _document == null)
            return;

        var scope = LanguageHelper.GetScopeName(_document.FilePath, _registry);
        _textMate.SetGrammar(scope);
        _document.Language = LanguageHelper.GetLanguageDisplay(_document.FilePath, _registry);
    }

    private void OnThemeChanged(ThemeName theme)
    {
        if (_textMate == null || _registry == null)
            return;

        _registry = new RegistryOptions(theme);
        _textMate.SetTheme(_registry.LoadTheme(theme));
        ApplyGrammar();
    }

    private void ApplySettings()
    {
        if (_editor == null || _settings == null || _document == null)
            return;

        var s = _settings.Current;
        _document.FontSize = s.FontSize;
        _editor.FontSize = s.FontSize;
        _editor.Options.IndentationSize = s.TabSize;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.WordWrap = _document.WordWrap;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) => UpdateCaretStatus();

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
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_editor == null || _document == null || e.Text is not { Length: > 0 })
            return;

        var ch = e.Text[0];
        if (_settings?.Current.AutoCloseBrackets == true && OpenBrackets.Contains(ch))
            AutoCloseBracket(ch);

        if (_settings?.Current.WordCompletion != false)
            MaybeShowWordCompletion(ch);

        _ = MaybeShowLspCompletionAsync(ch);
    }

    private void AutoCloseBracket(char open)
    {
        if (_editor == null || !BracketPairs.TryGetValue(open, out var close))
            return;

        var offset = _editor.CaretOffset;
        _editor.Document.Insert(offset, close.ToString());
        _editor.CaretOffset = offset;
    }

    private void MaybeShowWordCompletion(char ch)
    {
        if (_editor == null || (!char.IsLetter(ch) && ch != '_'))
            return;

        var word = GetWordBeforeCaret();
        if (word.Length < 2)
            return;

        var candidates = CollectWords()
            .Where(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(w, word, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w.Length)
            .ThenBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();

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

    private IEnumerable<string> CollectWords()
    {
        if (_editor?.Document == null)
            yield break;

        foreach (Match match in WordRegex.Matches(_editor.Document.Text))
            yield return match.Value;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            MaybeShowWordCompletion('_');
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

    private void UpdateFolding()
    {
        if (_foldingManager == null || _editor?.Document == null)
            return;

        try
        {
            _foldingStrategy.UpdateFoldings(_foldingManager, _editor.Document);
        }
        catch
        {
            // Ignore transient folding mismatches during document swaps.
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
