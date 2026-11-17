using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using x_pad.Models;
using x_pad.Services;
using x_pad.Services.Lsp;
using x_pad.Views;

namespace x_pad.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly FileService _fileService = new();
    private readonly SessionService _sessionService = new();
    private readonly SettingsService _settingsService;
    private readonly LspManager _lspManager;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly List<ClosedTabSnapshot> _closedTabs = new();
    private const int MaxClosedTabs = 20;
    private int _untitledCount;
    private bool _autosaveRunning;
    private int _externalChangeGate;

    private sealed class ClosedTabSnapshot
    {
        public string? FilePath { get; init; }
        public string Title { get; init; } = "Untitled";
        public string? Text { get; init; }
        public bool WasDirty { get; init; }
        public string? LanguageOverrideExtension { get; init; }
        public LineEndingKind LineEndingKind { get; init; }
        public string EncodingName { get; init; } = "UTF-8";
        public bool HasBom { get; init; }
        public Encoding? TextEncoding { get; init; }
        public int Line { get; init; } = 1;
        public int Column { get; init; } = 1;
    }

    public ObservableCollection<DocumentViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private DocumentViewModel? selectedTab;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private bool wordWrap;

    [ObservableProperty]
    private bool alwaysOnTop;

    [ObservableProperty]
    private bool isTerminalVisible;

    public ObservableCollection<string> RecentFiles { get; } = new();

    public event EventHandler? RecentFilesChanged;

    public SettingsService Settings => _settingsService;
    public LspManager Lsp => _lspManager;

    public MainWindowViewModel(string[]? args = null, SettingsService? settings = null)
    {
        _settingsService = settings ?? new SettingsService();
        _lspManager = new LspManager(_settingsService);
        _lspManager.DiagnosticsReceived += OnLspDiagnostics;
        WordWrap = _settingsService.Current.WordWrap;
        RefreshRecentFiles();

        EditorThemeService.ApplyAppTheme(_settingsService.Current.Theme);

        var openedFromArgs = false;
        foreach (var path in CliArgs.ResolveFilePaths(args))
        {
            OpenFileSync(path);
            openedFromArgs = true;
        }

        if (!openedFromArgs)
            RestoreSession();

        if (Tabs.Count == 0)
            NewTab();

        _autosaveTimer = new DispatcherTimer();
        _autosaveTimer.Tick += async (_, _) => await AutosaveTickAsync();
        ConfigureAutosaveTimer();
        _settingsService.Changed += ConfigureAutosaveTimer;
    }

    private void ConfigureAutosaveTimer()
    {
        var seconds = Math.Max(0, _settingsService.Current.AutosaveIntervalSeconds);
        _autosaveTimer.Stop();
        if (seconds <= 0)
            return;

        _autosaveTimer.Interval = TimeSpan.FromSeconds(seconds);
        _autosaveTimer.Start();
    }

    private async Task AutosaveTickAsync()
    {
        if (_autosaveRunning)
            return;

        var dirty = Tabs.Where(t => t.IsDirty && !string.IsNullOrWhiteSpace(t.FilePath)).ToList();
        if (dirty.Count == 0)
            return;

        _autosaveRunning = true;
        try
        {
            var saved = 0;
            foreach (var tab in dirty)
            {
                if (await SaveTabAsync(tab))
                    saved++;
            }

            if (saved > 0)
                StatusText = $"Autosaved {saved} file(s)";
        }
        finally
        {
            _autosaveRunning = false;
        }
    }

    private void OnLspDiagnostics(string uri, IReadOnlyList<Services.Lsp.LspDiagnostic> diagnostics)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedTab?.FilePath == null)
                return;

            var selectedUri = new Uri(Path.GetFullPath(SelectedTab.FilePath)).AbsoluteUri;
            if (!string.Equals(selectedUri, uri, StringComparison.OrdinalIgnoreCase))
                return;

            StatusText = diagnostics.Count == 0
                ? "Ready"
                : $"{diagnostics.Count} diagnostic(s) — {diagnostics[0].Message}";
        });
    }

    private void OpenFileSync(string path)
    {
        var existing = Tabs.FirstOrDefault(t =>
            t.FilePath != null &&
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize,
            FontFamily = _settingsService.Current.FontFamily
        };

        if (File.Exists(path))
        {
            var snapshot = _fileService.ReadFile(path);
            tab.ApplySnapshot(snapshot, path);
            RememberRecent(path);
        }
        else
        {
            tab.SetNewFile(path);
        }

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    partial void OnWordWrapChanged(bool value)
    {
        foreach (var tab in Tabs)
            tab.WordWrap = value;
        _settingsService.Update(s => s.WordWrap = value);
    }

    partial void OnSelectedTabChanged(DocumentViewModel? value)
    {
        if (value != null)
            _ = CheckExternalChangeAsync(value);

        if (IsTerminalVisible)
        {
            var panel = GetMainWindow()?.FindControl<TerminalPanel>("TerminalHost");
            panel?.SetWorkingDirectoryHint(value?.FilePath);
        }
    }

    public void CheckSelectedTabForExternalChanges()
    {
        if (SelectedTab != null)
            _ = CheckExternalChangeAsync(SelectedTab);
    }

    public void OpenFilesFromExternal(string[] paths)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BringMainWindowToFront();
            foreach (var path in CliArgs.ResolveFilePaths(paths))
                _ = OpenFileFromPath(path);
        });
    }

    private void BringMainWindowToFront()
    {
        TrayService.ShowMainWindow();
        var window = GetMainWindow();
        if (window == null)
            return;

        window.Topmost = true;
        window.Topmost = AlwaysOnTop;
    }

    [RelayCommand]
    private void NewTab()
    {
        _untitledCount++;
        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize,
            FontFamily = _settingsService.Current.FontFamily
        };
        tab.SetUntitled($"Untitled {_untitledCount}");
        Tabs.Add(tab);
        SelectedTab = tab;
        StatusText = "New document";
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var topLevel = GetMainWindow();
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = true
        });

        foreach (var file in files)
            await OpenFileFromPath(file.Path.LocalPath);
    }

    public async Task OpenFileFromPath(
        string path,
        int? restoreLine = null,
        int? restoreColumn = null,
        string? languageOverrideExtension = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        var existing = Tabs.FirstOrDefault(t =>
            t.FilePath != null &&
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedTab = existing;
            if (languageOverrideExtension != null)
                existing.LanguageOverrideExtension = languageOverrideExtension;
            if (restoreLine is int line)
            {
                existing.PendingRestoreLine = line;
                existing.PendingRestoreColumn = restoreColumn ?? 1;
                GetSelectedEditor()?.RestorePendingCaret();
            }
            GetSelectedEditor()?.RefreshGrammar();
            StatusText = $"Already open: {Path.GetFileName(path)}";
            return;
        }

        try
        {
            var tab = new DocumentViewModel
            {
                WordWrap = WordWrap,
                FontSize = _settingsService.Current.FontSize,
                FontFamily = _settingsService.Current.FontFamily,
                LanguageOverrideExtension = languageOverrideExtension,
                PendingRestoreLine = restoreLine,
                PendingRestoreColumn = restoreColumn
            };

            if (File.Exists(path))
            {
                var snapshot = await _fileService.ReadFileAsync(path);
                tab.ApplySnapshot(snapshot, path);
                RememberRecent(path);
                StatusText = $"Opened {Path.GetFileName(path)}";
            }
            else
            {
                tab.SetNewFile(path);
                StatusText = $"New file: {Path.GetFileName(path)}";
            }

            Tabs.Add(tab);
            SelectedTab = tab;

            // Drop the initial empty untitled tab if it was the only placeholder
            var emptyUntitled = Tabs.FirstOrDefault(t =>
                t != tab && t.FilePath == null && !t.IsDirty && string.IsNullOrEmpty(t.Text));
            if (emptyUntitled != null && Tabs.Count > 1)
                Tabs.Remove(emptyUntitled);

            SaveSession();
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenRecent(object? parameter)
    {
        // MenuItem may pass DataContext when CommandParameter is unset; ignore non-strings.
        if (parameter is not string path || string.IsNullOrWhiteSpace(path))
            return;
        if (!File.Exists(path))
        {
            StatusText = $"Missing: {path}";
            RemoveRecent(path);
            return;
        }
        await OpenFileFromPath(path);
    }

    [RelayCommand]
    private async Task SaveFile()
    {
        if (SelectedTab == null) return;
        await SaveTabAsync(SelectedTab);
    }

    [RelayCommand]
    private async Task SaveFileAs()
    {
        if (SelectedTab == null) return;
        await SaveTabAsAsync(SelectedTab);
    }

    private async Task<bool> SaveTabAsync(DocumentViewModel tab)
    {
        if (string.IsNullOrWhiteSpace(tab.FilePath))
            return await SaveTabAsAsync(tab);

        try
        {
            ApplySaveCleanup(tab);
            await _fileService.WriteFileAsync(
                tab.FilePath,
                tab.Text,
                tab.TextEncoding,
                tab.HasBom,
                tab.LineEndingKind,
                backupExisting: _settingsService.Current.BackupOnSave);
            tab.MarkClean(tab.FilePath);
            RememberRecent(tab.FilePath);
            SaveSession();
            StatusText = $"Saved {Path.GetFileName(tab.FilePath)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            return false;
        }
    }

    private void ApplySaveCleanup(DocumentViewModel tab)
    {
        var settings = _settingsService.Current;
        if (!settings.TrimTrailingWhitespaceOnSave && !settings.EnsureFinalNewlineOnSave)
            return;

        // Prefer the live editor so undo stack stays coherent with the visible buffer.
        var editor = GetMainWindow()?.FindDocumentEditor(tab) ??
                     (ReferenceEquals(tab, SelectedTab) ? GetSelectedEditor() : null);
        if (editor != null)
        {
            if (settings.TrimTrailingWhitespaceOnSave)
                editor.TrimTrailingWhitespace();
            if (settings.EnsureFinalNewlineOnSave)
                editor.EnsureFinalNewline();
            return;
        }

        if (settings.TrimTrailingWhitespaceOnSave)
        {
            var lines = tab.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimEnd(' ', '\t');
            var joined = string.Join(tab.LineEndingKind.ToNewLine(), lines);
            if (!string.Equals(tab.Text, joined, StringComparison.Ordinal))
                tab.Document.Text = joined;
        }

        if (settings.EnsureFinalNewlineOnSave && tab.Text.Length > 0)
        {
            var last = tab.Text[^1];
            if (last is not ('\n' or '\r'))
                tab.Document.Insert(tab.Document.TextLength, tab.LineEndingKind.ToNewLine());
        }
    }

    private async Task<bool> SaveTabAsAsync(DocumentViewModel tab)
    {
        var topLevel = GetMainWindow();
        if (topLevel == null) return false;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File As",
            SuggestedFileName = tab.FilePath != null
                ? Path.GetFileName(tab.FilePath)
                : "Untitled.txt"
        });

        if (file == null)
            return false;

        var path = file.Path.LocalPath;
        try
        {
            await _fileService.WriteFileAsync(
                path,
                tab.Text,
                tab.TextEncoding,
                tab.HasBom,
                tab.LineEndingKind,
                backupExisting: _settingsService.Current.BackupOnSave && File.Exists(path));
            tab.MarkClean(path);
            RememberRecent(path);
            SaveSession();
            StatusText = $"Saved {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (!tab.IsDirty && tab.FilePath != null)
                continue;
            var ok = await SaveTabAsync(tab);
            if (!ok && string.IsNullOrWhiteSpace(tab.FilePath))
                break;
        }
        StatusText = "All files saved";
    }

    [RelayCommand]
    private async Task CloseTab(object? parameter)
    {
        // Tab × buttons pass DocumentViewModel; menu/keybinding may pass null or wrong DC.
        var tab = parameter as DocumentViewModel ?? SelectedTab;
        if (tab == null) return;

        if (!await ConfirmCloseAsync(tab))
            return;

        RememberClosedTab(tab);
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
            NewTab();
        else if (SelectedTab == null || !Tabs.Contains(SelectedTab))
            SelectedTab = Tabs[Math.Max(0, Math.Min(index, Tabs.Count - 1))];

        SaveSession();
    }

    [RelayCommand]
    private async Task CloseAll()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (!await ConfirmCloseAsync(tab))
                return;
            RememberClosedTab(tab);
            Tabs.Remove(tab);
        }
        NewTab();
        SaveSession();
    }

    [RelayCommand]
    private async Task CloseOthers()
    {
        if (SelectedTab == null) return;
        var keep = SelectedTab;
        foreach (var tab in Tabs.Where(t => t != keep).ToList())
        {
            if (!await ConfirmCloseAsync(tab))
                return;
            RememberClosedTab(tab);
            Tabs.Remove(tab);
        }
        SaveSession();
    }

    [RelayCommand]
    private async Task ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            StatusText = "No closed tab to reopen";
            return;
        }

        var snap = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);

        if (!string.IsNullOrWhiteSpace(snap.FilePath) &&
            snap.Text == null &&
            File.Exists(snap.FilePath))
        {
            await OpenFileFromPath(
                snap.FilePath,
                snap.Line,
                snap.Column,
                snap.LanguageOverrideExtension);
            StatusText = $"Reopened {Path.GetFileName(snap.FilePath)}";
            return;
        }

        if (string.IsNullOrWhiteSpace(snap.FilePath) &&
            string.IsNullOrEmpty(snap.Text) &&
            !snap.WasDirty)
        {
            NewTab();
            StatusText = "Reopened empty tab";
            return;
        }

        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize,
            FontFamily = _settingsService.Current.FontFamily,
            LanguageOverrideExtension = snap.LanguageOverrideExtension,
            PendingRestoreLine = snap.Line,
            PendingRestoreColumn = snap.Column
        };

        var title = snap.Title;
        if (string.IsNullOrWhiteSpace(snap.FilePath) &&
            title.StartsWith("Untitled", StringComparison.Ordinal))
        {
            _untitledCount++;
            title = $"Untitled {_untitledCount}";
        }

        tab.RestoreClosedBuffer(
            snap.FilePath,
            title,
            snap.Text ?? "",
            snap.WasDirty,
            snap.LineEndingKind,
            snap.TextEncoding ?? FileService.CreateUtf8(false),
            snap.HasBom,
            snap.EncodingName);

        Tabs.Add(tab);
        SelectedTab = tab;
        GetSelectedEditor()?.RefreshGrammar();
        StatusText = $"Reopened {tab.Title.TrimEnd('*')}";
        SaveSession();
    }

    private void RememberClosedTab(DocumentViewModel tab)
    {
        var hasPath = !string.IsNullOrWhiteSpace(tab.FilePath);
        var reloadFromDisk = hasPath && !tab.IsDirty && File.Exists(tab.FilePath);

        _closedTabs.Add(new ClosedTabSnapshot
        {
            FilePath = tab.FilePath,
            Title = tab.Title.TrimEnd('*'),
            Text = reloadFromDisk ? null : tab.Text,
            WasDirty = tab.IsDirty,
            LanguageOverrideExtension = tab.LanguageOverrideExtension,
            LineEndingKind = tab.LineEndingKind,
            EncodingName = tab.Encoding,
            HasBom = tab.HasBom,
            TextEncoding = tab.TextEncoding,
            Line = Math.Max(1, tab.LineNumber),
            Column = Math.Max(1, tab.ColumnNumber)
        });

        while (_closedTabs.Count > MaxClosedTabs)
            _closedTabs.RemoveAt(0);
    }

    public async Task<bool> ConfirmCloseAllForExitAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (!await ConfirmCloseAsync(tab))
                return false;
        }
        return true;
    }

    private async Task<bool> ConfirmCloseAsync(DocumentViewModel tab)
    {
        if (!tab.IsDirty)
            return true;

        var owner = GetMainWindow();
        if (owner == null)
            return true;

        var dialog = new ConfirmSaveWindow(tab.Title);
        var result = await dialog.ShowDialog<ConfirmSaveResult>(owner);
        return result switch
        {
            ConfirmSaveResult.Discard => true,
            ConfirmSaveResult.Save => await SaveTabAsync(tab),
            _ => false
        };
    }

    [RelayCommand]
    private void ToggleWordWrap()
    {
        WordWrap = !WordWrap;
        StatusText = WordWrap ? "Word Wrap: On" : "Word Wrap: Off";
    }

    [RelayCommand]
    private void ToggleWhitespace()
    {
        var next = !_settingsService.Current.ShowWhitespace;
        _settingsService.Update(s => s.ShowWhitespace = next);
        StatusText = next ? "Whitespace: On" : "Whitespace: Off";
    }

    [RelayCommand]
    private void DuplicateLine()
    {
        GetSelectedEditor()?.DuplicateLine();
        StatusText = "Line duplicated";
    }

    [RelayCommand]
    private void MoveLineUp()
    {
        GetSelectedEditor()?.MoveLineUp();
        StatusText = "Moved line up";
    }

    [RelayCommand]
    private void MoveLineDown()
    {
        GetSelectedEditor()?.MoveLineDown();
        StatusText = "Moved line down";
    }

    [RelayCommand]
    private void JumpToMatchingBracket()
    {
        GetSelectedEditor()?.JumpToMatchingBracket();
        StatusText = "Jumped to matching bracket";
    }

    [RelayCommand]
    private void AddNextOccurrence()
    {
        var count = GetSelectedEditor()?.AddNextOccurrence() ?? 0;
        StatusText = count > 0 ? $"Occurrences: {count}" : "No occurrence found";
    }

    [RelayCommand]
    private void SelectAllOccurrences()
    {
        var count = GetSelectedEditor()?.SelectAllOccurrences() ?? 0;
        StatusText = count > 0 ? $"Selected {count} occurrences" : "No occurrence found";
    }

    [RelayCommand]
    private void ToggleLineComment()
    {
        GetSelectedEditor()?.ToggleLineComment();
        StatusText = "Toggled line comment";
    }

    [RelayCommand]
    private void InsertDateTime()
    {
        // Windows Notepad F5 style: culture-aware short date + long time.
        var stamp = DateTime.Now.ToString("g");
        GetSelectedEditor()?.InsertTextAtCaret(stamp);
        StatusText = $"Inserted {stamp}";
    }

    [RelayCommand]
    private async Task ShowAbout()
    {
        var owner = GetMainWindow();
        if (owner == null)
            return;

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XPad", "settings.json");
        var sessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XPad", "session.json");

        var dialog = new Window
        {
            Title = "About X-Pad",
            Width = 480,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        ok.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(ok, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                ok,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "X-Pad", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock { Text = $"Version {version}" },
                        new TextBlock
                        {
                            Text = "Lightweight Notepad++/Atom-style editor built with Avalonia + AvaloniaEdit.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Settings: {settingsPath}",
                            FontSize = 11,
                            Opacity = 0.75,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Session: {sessionPath}",
                            FontSize = 11,
                            Opacity = 0.75,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    }
                }
            }
        };

        await dialog.ShowDialog(owner);
    }

    [RelayCommand]
    private void UpperCase()
    {
        GetSelectedEditor()?.TransformSelectionCase(static s => s.ToUpperInvariant());
        StatusText = "Upper case";
    }

    [RelayCommand]
    private void LowerCase()
    {
        GetSelectedEditor()?.TransformSelectionCase(static s => s.ToLowerInvariant());
        StatusText = "Lower case";
    }

    [RelayCommand]
    private void TitleCase()
    {
        GetSelectedEditor()?.TransformSelectionCase(static s =>
            System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()));
        StatusText = "Title case";
    }

    [RelayCommand]
    private void SortLinesAscending()
    {
        GetSelectedEditor()?.SortLines(descending: false);
        StatusText = "Sorted lines A→Z";
    }

    [RelayCommand]
    private void SortLinesDescending()
    {
        GetSelectedEditor()?.SortLines(descending: true);
        StatusText = "Sorted lines Z→A";
    }

    [RelayCommand]
    private void JoinLines()
    {
        var joined = GetSelectedEditor()?.JoinLines() ?? 0;
        StatusText = joined == 0 ? "Nothing to join" : $"Joined {joined + 1} lines";
    }

    [RelayCommand]
    private void RemoveEmptyLines()
    {
        var count = GetSelectedEditor()?.RemoveEmptyLines() ?? 0;
        StatusText = count == 0 ? "No empty lines" : $"Removed {count} empty line(s)";
    }

    [RelayCommand]
    private void RemoveDuplicateLines()
    {
        var count = GetSelectedEditor()?.RemoveDuplicateLines() ?? 0;
        StatusText = count == 0 ? "No duplicate lines" : $"Removed {count} duplicate line(s)";
    }

    [RelayCommand]
    private void TrimTrailingWhitespace()
    {
        var count = GetSelectedEditor()?.TrimTrailingWhitespace() ?? 0;
        StatusText = count == 0 ? "No trailing whitespace" : $"Trimmed {count} line(s)";
    }

    [RelayCommand]
    private void ToggleBookmark()
    {
        var added = GetSelectedEditor()?.ToggleBookmark() == true;
        StatusText = added ? "Bookmark added" : "Bookmark removed";
    }

    [RelayCommand]
    private void NextBookmark()
    {
        StatusText = GetSelectedEditor()?.GoToNextBookmark() == true
            ? "Next bookmark"
            : "No bookmarks";
    }

    [RelayCommand]
    private void PreviousBookmark()
    {
        StatusText = GetSelectedEditor()?.GoToPreviousBookmark() == true
            ? "Previous bookmark"
            : "No bookmarks";
    }

    [RelayCommand]
    private void ClearBookmarks()
    {
        GetSelectedEditor()?.ClearBookmarks();
        StatusText = "Bookmarks cleared";
    }

    [RelayCommand]
    private async Task CopyFullPath()
    {
        var path = SelectedTab?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "No file path";
            return;
        }

        var top = GetMainWindow();
        if (top?.Clipboard == null)
        {
            StatusText = "Clipboard unavailable";
            return;
        }

        await top.Clipboard.SetTextAsync(path!);
        StatusText = "Path copied";
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        var path = SelectedTab?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "No file path";
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                    else
                    {
                        StatusText = "Path not found";
                        return;
                    }
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", ["-R", path]);
            }
            else
            {
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                if (!string.IsNullOrEmpty(dir))
                    System.Diagnostics.Process.Start("xdg-open", dir);
            }

            StatusText = "Revealed in file manager";
        }
        catch (Exception ex)
        {
            StatusText = $"Reveal failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void InstallSendTo()
    {
        if (!OperatingSystem.IsWindows())
        {
            StatusText = "SendTo install is Windows-only";
            return;
        }

        try
        {
            var path = new WindowsIntegrationService().InstallSendToShortcut();
            StatusText = $"SendTo shortcut created: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"SendTo install failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RegisterOpenWith()
    {
        if (!OperatingSystem.IsWindows())
        {
            StatusText = "Open With registration is Windows-only";
            return;
        }

        try
        {
            var count = new WindowsIntegrationService().RegisterOpenWith();
            StatusText = $"Registered X-Pad in Open With for {count} extension(s) (per-user)";
        }
        catch (Exception ex)
        {
            StatusText = $"Open With registration failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReloadFromDisk()
    {
        var tab = SelectedTab;
        if (tab?.FilePath == null)
        {
            StatusText = "No file on disk";
            return;
        }

        if (!File.Exists(tab.FilePath))
        {
            StatusText = $"Missing: {tab.FilePath}";
            return;
        }

        if (tab.IsDirty)
        {
            var owner = GetMainWindow();
            if (owner == null)
                return;

            var prompt = new Window
            {
                Title = "Reload from Disk",
                Width = 420,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            var reload = new Button { Content = "Reload", Width = 90, IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
            var tcs = new TaskCompletionSource<bool>();
            reload.Click += (_, _) => { tcs.TrySetResult(true); prompt.Close(); };
            cancel.Click += (_, _) => { tcs.TrySetResult(false); prompt.Close(); };
            prompt.Closed += (_, _) => tcs.TrySetResult(false);
            prompt.Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Reload \"{Path.GetFileName(tab.FilePath)}\" and discard unsaved changes?",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, reload }
                    }
                }
            };
            _ = prompt.ShowDialog(owner);
            if (!await tcs.Task)
                return;
        }

        try
        {
            var snapshot = await _fileService.ReadFileAsync(tab.FilePath);
            tab.ApplySnapshot(snapshot, tab.FilePath);
            StatusText = $"Reloaded {Path.GetFileName(tab.FilePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Reload failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Find()
    {
        GetSelectedEditor()?.OpenFind();
        StatusText = "Find";
    }

    [RelayCommand]
    private void FindNext()
    {
        GetSelectedEditor()?.FindNext();
        StatusText = "Find next";
    }

    [RelayCommand]
    private void FindPrevious()
    {
        GetSelectedEditor()?.FindPrevious();
        StatusText = "Find previous";
    }

    [RelayCommand]
    private void Replace()
    {
        GetSelectedEditor()?.OpenReplace();
        StatusText = "Replace";
    }

    [RelayCommand]
    private async Task FindInOpenTabs()
    {
        var owner = GetMainWindow();
        if (owner == null)
            return;

        var dialog = new FindInOpenTabsWindow(Tabs);
        var hit = await dialog.ShowDialog<FindHit?>(owner);
        if (hit == null)
            return;

        SelectedTab = hit.Tab;
        // Let the editor bind, then jump and select the match.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        GetSelectedEditor()?.GoToMatch(hit.Line, hit.Column, hit.MatchLength);
        StatusText = $"Found in {hit.Location}";
    }

    [RelayCommand]
    private async Task GoToLine()
    {
        if (SelectedTab == null) return;
        var owner = GetMainWindow();
        if (owner == null) return;

        var dialog = new GoToLineWindow(SelectedTab.LineNumber);
        var ok = await dialog.ShowDialog<bool>(owner);
        if (ok && dialog.LineNumber is int line)
        {
            GetSelectedEditor()?.GoToLine(line);
            StatusText = $"Line {line}";
        }
    }

    [RelayCommand]
    private void ToggleDarkMode() => SetTheme("Dark");

    [RelayCommand]
    private void ToggleLightMode() => SetTheme("Light");

    [RelayCommand]
    private void ToggleSystemTheme() => SetTheme("System");

    [RelayCommand]
    private void ToggleMonokaiTheme() => SetTheme("Monokai");

    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count == 0) return;
        var index = SelectedTab == null ? 0 : Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(index + 1) % Tabs.Count];
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count == 0) return;
        var index = SelectedTab == null ? 0 : Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(index - 1 + Tabs.Count) % Tabs.Count];
    }

    [RelayCommand]
    private void MoveTabLeft()
    {
        if (SelectedTab == null || Tabs.Count < 2)
            return;
        var index = Tabs.IndexOf(SelectedTab);
        if (index <= 0)
            return;
        Tabs.Move(index, index - 1);
        SaveSession();
        StatusText = "Moved tab left";
    }

    [RelayCommand]
    private void MoveTabRight()
    {
        if (SelectedTab == null || Tabs.Count < 2)
            return;
        var index = Tabs.IndexOf(SelectedTab);
        if (index < 0 || index >= Tabs.Count - 1)
            return;
        Tabs.Move(index, index + 1);
        SaveSession();
        StatusText = "Moved tab right";
    }

    [RelayCommand]
    private void SelectCurrentLine()
    {
        GetSelectedEditor()?.SelectCurrentLine();
        StatusText = "Line selected";
    }

    [RelayCommand]
    private void TriggerCompletions()
    {
        GetSelectedEditor()?.TriggerWordCompletion();
        StatusText = "Completions";
    }

    [RelayCommand]
    private void FoldAll()
    {
        GetSelectedEditor()?.FoldAll();
        StatusText = "Folded all";
    }

    [RelayCommand]
    private void UnfoldAll()
    {
        GetSelectedEditor()?.UnfoldAll();
        StatusText = "Unfolded all";
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        _settingsService.Update(s => s.RecentFiles.Clear());
        RefreshRecentFiles();
        StatusText = "Recent files cleared";
    }

    [RelayCommand]
    private void Undo() => GetSelectedEditor()?.TextEditorControl?.Undo();

    [RelayCommand]
    private void Redo() => GetSelectedEditor()?.TextEditorControl?.Redo();

    [RelayCommand]
    private void Cut() => GetSelectedEditor()?.TextEditorControl?.Cut();

    [RelayCommand]
    private void Copy() => GetSelectedEditor()?.TextEditorControl?.Copy();

    [RelayCommand]
    private void Paste() => GetSelectedEditor()?.TextEditorControl?.Paste();

    [RelayCommand]
    private void SelectAll() => GetSelectedEditor()?.TextEditorControl?.SelectAll();

    [RelayCommand]
    private void InvertSelection()
    {
        GetSelectedEditor()?.InvertCurrentSelection();
        StatusText = "Invert selection";
    }

    [RelayCommand]
    private void SearchWeb()
    {
        var query = GetSelectedEditor()?.GetSearchQuery() ?? "";
        if (SearchWebHelper.TryOpen(query, out var error))
        {
            StatusText = "Opened web search";
            return;
        }

        StatusText = string.IsNullOrEmpty(error) ? "Search Web failed" : error;
    }

    [RelayCommand]
    private void ToggleTerminal()
    {
        IsTerminalVisible = !IsTerminalVisible;
        StatusText = IsTerminalVisible ? "Terminal shown" : "Terminal hidden";
        if (!IsTerminalVisible)
            return;

        var window = GetMainWindow();
        var panel = window?.FindControl<TerminalPanel>("TerminalHost");
        panel?.SetWorkingDirectoryHint(SelectedTab?.FilePath);
        panel?.EnsureStarted();
    }

    [RelayCommand]
    private void OpenWithVsCode(object? parameter)
    {
        var path = ResolveTabPath(parameter);
        if (ExternalAppLauncher.TryOpen(ExternalAppKind.VsCode, path, out var error))
            StatusText = "Opened in VS Code";
        else
            StatusText = error;
    }

    [RelayCommand]
    private void OpenWithCursor(object? parameter)
    {
        var path = ResolveTabPath(parameter);
        if (ExternalAppLauncher.TryOpen(ExternalAppKind.Cursor, path, out var error))
            StatusText = "Opened in Cursor";
        else
            StatusText = error;
    }

    [RelayCommand]
    private void OpenWithWindowsTerminal(object? parameter)
    {
        var path = ResolveTabPath(parameter);
        if (ExternalAppLauncher.TryOpen(ExternalAppKind.WindowsTerminal, path, out var error))
            StatusText = string.IsNullOrEmpty(error) ? "Opened Windows Terminal" : error;
        else
            StatusText = error;
    }

    private string? ResolveTabPath(object? parameter)
    {
        if (parameter is DocumentViewModel tab)
            return tab.FilePath ?? SelectedTab?.FilePath;
        return SelectedTab?.FilePath;
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        if (SelectedTab == null)
            return;

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export as PDF",
            SuggestedFileName = Path.GetFileNameWithoutExtension(SelectedTab.Title.TrimEnd('*')) + ".pdf",
            FileTypeChoices =
            [
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }
            ]
        });
        if (file == null)
            return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Could not resolve PDF path";
            return;
        }

        try
        {
            var text = GetSelectedEditor()?.GetExportText() ?? SelectedTab.Text;
            PdfExportService.ExportPlainText(
                path,
                SelectedTab.Title.TrimEnd('*'),
                text,
                "Consolas",
                (float)Math.Clamp(SelectedTab.FontSize, 8, 14));
            StatusText = $"Exported PDF: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"PDF export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CycleLineEnding()
    {
        if (SelectedTab == null) return;
        var next = SelectedTab.LineEndingKind switch
        {
            LineEndingKind.Lf => LineEndingKind.Crlf,
            LineEndingKind.Crlf => LineEndingKind.Cr,
            _ => LineEndingKind.Lf
        };
        SelectedTab.SetLineEnding(next);
        MarkDirtyForMetaChange(SelectedTab);
        StatusText = $"Line ending: {next.ToDisplay()}";
    }

    [RelayCommand]
    private void SetLanguage(object? parameter)
    {
        if (SelectedTab == null)
            return;

        var extensionOrAuto = parameter as string;
        SelectedTab.LanguageOverrideExtension =
            string.IsNullOrWhiteSpace(extensionOrAuto) ? null : extensionOrAuto;

        GetSelectedEditor()?.RefreshGrammar();
        StatusText = SelectedTab.LanguageOverrideExtension == null
            ? $"Language: {SelectedTab.Language} (auto)"
            : $"Language: {SelectedTab.Language}";
    }

    [RelayCommand]
    private void CycleEncoding()
    {
        if (SelectedTab == null) return;
        if (!SelectedTab.HasBom && SelectedTab.Encoding.StartsWith("UTF-8", StringComparison.OrdinalIgnoreCase))
            SetEncodingUtf8Bom();
        else if (SelectedTab.Encoding.Contains("UTF-8", StringComparison.OrdinalIgnoreCase))
            SetEncodingUtf16Le();
        else if (SelectedTab.Encoding.Contains("UTF-16", StringComparison.OrdinalIgnoreCase))
            SetEncodingLatin1();
        else
            SetEncodingUtf8();
    }

    [RelayCommand]
    private void SetEncodingUtf8()
    {
        if (SelectedTab == null) return;
        SelectedTab.TextEncoding = FileService.CreateUtf8(false);
        SelectedTab.HasBom = false;
        SelectedTab.Encoding = "UTF-8";
        MarkDirtyForMetaChange(SelectedTab);
        StatusText = "Encoding: UTF-8";
    }

    [RelayCommand]
    private void SetEncodingUtf8Bom()
    {
        if (SelectedTab == null) return;
        SelectedTab.TextEncoding = FileService.CreateUtf8(false);
        SelectedTab.HasBom = true;
        SelectedTab.Encoding = "UTF-8 BOM";
        MarkDirtyForMetaChange(SelectedTab);
        StatusText = "Encoding: UTF-8 BOM";
    }

    [RelayCommand]
    private void SetEncodingUtf16Le()
    {
        if (SelectedTab == null) return;
        SelectedTab.TextEncoding = new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        SelectedTab.HasBom = true;
        SelectedTab.Encoding = "UTF-16 LE";
        MarkDirtyForMetaChange(SelectedTab);
        StatusText = "Encoding: UTF-16 LE";
    }

    [RelayCommand]
    private void SetEncodingLatin1()
    {
        if (SelectedTab == null) return;
        SelectedTab.TextEncoding = System.Text.Encoding.Latin1;
        SelectedTab.HasBom = false;
        SelectedTab.Encoding = "LATIN1";
        MarkDirtyForMetaChange(SelectedTab);
        StatusText = "Encoding: LATIN1";
    }

    [RelayCommand]
    private void ToggleSplitVertical()
    {
        if (SelectedTab == null) return;
        if (SelectedTab.IsSplit && SelectedTab.SplitVertical)
        {
            SelectedTab.IsSplit = false;
            StatusText = "Split: Off";
            return;
        }

        SelectedTab.SplitVertical = true;
        SelectedTab.IsSplit = true;
        StatusText = "Split: Vertical";
    }

    [RelayCommand]
    private void ToggleSplitHorizontal()
    {
        if (SelectedTab == null) return;
        if (SelectedTab.IsSplit && !SelectedTab.SplitVertical)
        {
            SelectedTab.IsSplit = false;
            StatusText = "Split: Off";
            return;
        }

        SelectedTab.SplitVertical = false;
        SelectedTab.IsSplit = true;
        StatusText = "Split: Horizontal";
    }

    [RelayCommand]
    private void ZoomIn()
    {
        GetSelectedEditor()?.ZoomBy(1);
        StatusText = $"Zoom: {SelectedTab?.FontSize:0}px";
    }

    [RelayCommand]
    private void ZoomOut()
    {
        GetSelectedEditor()?.ZoomBy(-1);
        StatusText = $"Zoom: {SelectedTab?.FontSize:0}px";
    }

    [RelayCommand]
    private void ZoomReset()
    {
        GetSelectedEditor()?.ResetZoom();
        StatusText = "Zoom: 13px";
    }

    [RelayCommand]
    private async Task OpenCommandPalette()
    {
        var owner = GetMainWindow();
        if (owner == null)
            return;

        var dialog = new CommandPaletteWindow(BuildPaletteEntries());
        var selected = await dialog.ShowDialog<PaletteEntry?>(owner);
        if (selected == null)
            return;

        await selected.InvokeAsync();
        StatusText = selected.Label;
    }

    [RelayCommand]
    private async Task OpenQuickOpen()
    {
        var owner = GetMainWindow();
        if (owner == null)
            return;

        var dialog = new CommandPaletteWindow(BuildQuickOpenEntries());
        var selected = await dialog.ShowDialog<PaletteEntry?>(owner);
        if (selected == null)
            return;

        await selected.InvokeAsync();
        StatusText = selected.Label;
    }

    private IEnumerable<PaletteEntry> BuildQuickOpenEntries()
    {
        var entries = new List<PaletteEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in Tabs)
        {
            var label = tab.FilePath != null
                ? tab.FilePath
                : tab.Title.TrimEnd('*');
            var path = tab.FilePath;
            entries.Add(new PaletteEntry
            {
                Label = path != null ? Path.GetFileName(path) : label,
                Shortcut = path,
                Group = "Open Tabs",
                InvokeAsync = () =>
                {
                    SelectedTab = tab;
                    return Task.CompletedTask;
                }
            });
            if (path != null)
                seen.Add(path);
        }

        foreach (var path in RecentFiles)
        {
            if (!seen.Add(path))
                continue;

            var capture = path;
            entries.Add(new PaletteEntry
            {
                Label = Path.GetFileName(capture),
                Shortcut = capture,
                Group = "Recent",
                InvokeAsync = () => OpenFileFromPath(capture)
            });
        }

        return entries;
    }

    private IEnumerable<PaletteEntry> BuildPaletteEntries()
    {
        var entries = new List<PaletteEntry>
        {
            Entry("File: New Tab", "Ctrl+N", "File", () => { NewTab(); return Task.CompletedTask; }),
            Entry("File: Open…", "Ctrl+O", "File", () => OpenFile()),
            Entry("File: Save", "Ctrl+S", "File", () => SaveFile()),
            Entry("File: Save As…", "Ctrl+Shift+S", "File", () => SaveFileAs()),
            Entry("File: Save All", "Ctrl+Alt+S", "File", () => SaveAll()),
            Entry("File: Close Tab", "Ctrl+W", "File", () => CloseTab(SelectedTab)),
            Entry("File: Reopen Closed Tab", "Ctrl+Shift+T", "File", () => ReopenClosedTab()),
            Entry("File: Close All", null, "File", () => CloseAll()),
            Entry("File: Print…", "Ctrl+P", "File", () => Print()),
            Entry("File: Export as PDF…", null, "File", () => ExportPdf()),
            Entry("File: Copy Full Path", null, "File", () => CopyFullPath()),
            Entry("File: Reveal in Explorer", null, "File", () => { RevealInExplorer(); return Task.CompletedTask; }),
            Entry("File: Reload from Disk", null, "File", () => ReloadFromDisk()),
            Entry("File: Exit", "Alt+F4", "File", () => { Exit(); return Task.CompletedTask; }),

            Entry("Edit: Find…", "Ctrl+F", "Edit", () => { Find(); return Task.CompletedTask; }),
            Entry("Edit: Find Next", "F3", "Edit", () => { FindNext(); return Task.CompletedTask; }),
            Entry("Edit: Find Previous", "Shift+F3", "Edit", () => { FindPrevious(); return Task.CompletedTask; }),
            Entry("Edit: Replace…", "Ctrl+H", "Edit", () => { Replace(); return Task.CompletedTask; }),
            Entry("Edit: Find in Open Tabs…", "Ctrl+Shift+F", "Edit", () => FindInOpenTabs()),
            Entry("Edit: Invert Selection", null, "Edit", () => { InvertSelection(); return Task.CompletedTask; }),
            Entry("Edit: Search Web", null, "Edit", () => { SearchWeb(); return Task.CompletedTask; }),
            Entry("Edit: Go to Line…", "Ctrl+G", "Edit", () => GoToLine()),
            Entry("Edit: Toggle Line Comment", "Ctrl+/", "Edit", () => { ToggleLineComment(); return Task.CompletedTask; }),
            Entry("Edit: Insert Date/Time", "F5", "Edit", () => { InsertDateTime(); return Task.CompletedTask; }),
            Entry("Edit: Duplicate Line", "Ctrl+D", "Edit", () => { DuplicateLine(); return Task.CompletedTask; }),
            Entry("Help: About X-Pad", null, "Help", () => ShowAbout()),
            Entry("Edit: Upper Case", "Ctrl+U", "Edit", () => { UpperCase(); return Task.CompletedTask; }),
            Entry("Edit: Lower Case", "Ctrl+Shift+U", "Edit", () => { LowerCase(); return Task.CompletedTask; }),
            Entry("Edit: Title Case", null, "Edit", () => { TitleCase(); return Task.CompletedTask; }),
            Entry("Edit: Sort Lines A→Z", null, "Edit", () => { SortLinesAscending(); return Task.CompletedTask; }),
            Entry("Edit: Join Lines", "Ctrl+J", "Edit", () => { JoinLines(); return Task.CompletedTask; }),
            Entry("Edit: Remove Empty Lines", null, "Edit", () => { RemoveEmptyLines(); return Task.CompletedTask; }),
            Entry("Edit: Remove Duplicate Lines", null, "Edit", () => { RemoveDuplicateLines(); return Task.CompletedTask; }),
            Entry("Edit: Trim Trailing Whitespace", null, "Edit", () => { TrimTrailingWhitespace(); return Task.CompletedTask; }),
            Entry("Edit: Add Next Occurrence", "Ctrl+Alt+Right", "Edit", () => { AddNextOccurrence(); return Task.CompletedTask; }),
            Entry("Edit: Select All Occurrences", "Ctrl+Shift+L", "Edit", () => { SelectAllOccurrences(); return Task.CompletedTask; }),
            Entry("Edit: Select Current Line", "Ctrl+L", "Edit", () => { SelectCurrentLine(); return Task.CompletedTask; }),
            Entry("Edit: Trigger Completions", "Ctrl+Space", "Edit", () => { TriggerCompletions(); return Task.CompletedTask; }),
            Entry("Edit: Jump to Matching Bracket", "Ctrl+M", "Edit", () => { JumpToMatchingBracket(); return Task.CompletedTask; }),

            Entry("View: Language Auto", null, "View", () => { SetLanguage(""); return Task.CompletedTask; }),
            Entry("View: Language C#", null, "View", () => { SetLanguage(".cs"); return Task.CompletedTask; }),
            Entry("View: Language Python", null, "View", () => { SetLanguage(".py"); return Task.CompletedTask; }),
            Entry("View: Language JavaScript", null, "View", () => { SetLanguage(".js"); return Task.CompletedTask; }),
            Entry("View: Language Markdown", null, "View", () => { SetLanguage(".md"); return Task.CompletedTask; }),
            Entry("View: Toggle Word Wrap", "Alt+Z", "View", () => { ToggleWordWrap(); return Task.CompletedTask; }),
            Entry("View: Toggle Whitespace", null, "View", () => { ToggleWhitespace(); return Task.CompletedTask; }),
            Entry("View: Always on Top", null, "View", () => { ToggleAlwaysOnTop(); return Task.CompletedTask; }),
            Entry("View: Split Vertical", null, "View", () => { ToggleSplitVertical(); return Task.CompletedTask; }),
            Entry("View: Split Horizontal", null, "View", () => { ToggleSplitHorizontal(); return Task.CompletedTask; }),
            Entry("View: Toggle Terminal", "Ctrl+`", "View", () => { ToggleTerminal(); return Task.CompletedTask; }),
            Entry("View: Toggle Bookmark", "Ctrl+F2", "View", () => { ToggleBookmark(); return Task.CompletedTask; }),
            Entry("View: Next Bookmark", "F2", "View", () => { NextBookmark(); return Task.CompletedTask; }),
            Entry("View: Previous Bookmark", "Shift+F2", "View", () => { PreviousBookmark(); return Task.CompletedTask; }),
            Entry("View: Clear Bookmarks", null, "View", () => { ClearBookmarks(); return Task.CompletedTask; }),
            Entry("View: Fold All", "Ctrl+K Ctrl+0", "View", () => { FoldAll(); return Task.CompletedTask; }),
            Entry("View: Unfold All", "Ctrl+K Ctrl+J", "View", () => { UnfoldAll(); return Task.CompletedTask; }),
            Entry("View: Move Tab Left", "Ctrl+Shift+PageUp", "View", () => { MoveTabLeft(); return Task.CompletedTask; }),
            Entry("View: Move Tab Right", "Ctrl+Shift+PageDown", "View", () => { MoveTabRight(); return Task.CompletedTask; }),
            Entry("File: Clear Recent Files", null, "File", () => { ClearRecentFiles(); return Task.CompletedTask; }),
            Entry("View: Zoom In", "Ctrl+=", "View", () => { ZoomIn(); return Task.CompletedTask; }),
            Entry("View: Zoom Out", "Ctrl+-", "View", () => { ZoomOut(); return Task.CompletedTask; }),
            Entry("View: Zoom Reset", "Ctrl+0", "View", () => { ZoomReset(); return Task.CompletedTask; }),
            Entry("View: Dark Mode", null, "View", () => { ToggleDarkMode(); return Task.CompletedTask; }),
            Entry("View: Light Mode", null, "View", () => { ToggleLightMode(); return Task.CompletedTask; }),
            Entry("View: Monokai Theme", null, "View", () => { ToggleMonokaiTheme(); return Task.CompletedTask; }),
            Entry("View: Preferences…", "Ctrl+,", "View", () => OpenPreferences()),
            Entry("View: Command Palette…", "Ctrl+Shift+P", "View", () => OpenCommandPalette()),
            Entry("View: Quick Open…", "Ctrl+E", "View", () => OpenQuickOpen()),

            Entry("Tools: Install Send To", null, "Tools", () => { InstallSendTo(); return Task.CompletedTask; }),
            Entry("Tools: Register Open With (text types)", null, "Tools", () => { RegisterOpenWith(); return Task.CompletedTask; }),
        };

        foreach (var path in RecentFiles)
        {
            var capture = path;
            entries.Add(Entry(
                $"Open Recent: {Path.GetFileName(capture)}",
                null,
                "Recent",
                () => OpenFileFromPath(capture)));
        }

        return entries;

        static PaletteEntry Entry(string label, string? shortcut, string group, Func<Task> invoke) =>
            new()
            {
                Label = label,
                Shortcut = shortcut,
                Group = group,
                InvokeAsync = invoke
            };
    }

    [RelayCommand]
    private void Exit()
    {
        GetMainWindow()?.Close();
    }

    [RelayCommand]
    private async Task Print()
    {
        if (SelectedTab == null)
            return;

        if (!OperatingSystem.IsWindows())
        {
            StatusText = "Print is only available on Windows";
            return;
        }

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var printer = new PrintService();
        var printers = printer.GetPrinters();
        if (printers.Count == 0)
        {
            StatusText = "No printers found";
            return;
        }

        var dialog = new PrintWindow(printers);
        var ok = await dialog.ShowDialog<bool>(owner);
        if (!ok || string.IsNullOrWhiteSpace(dialog.SelectedPrinter))
            return;

        try
        {
            printer.PrintPlainText(
                SelectedTab.Title.TrimEnd('*'),
                SelectedTab.Text,
                dialog.SelectedPrinter,
                "Consolas",
                (float)Math.Clamp(SelectedTab.FontSize, 8, 18));
            StatusText = $"Sent to printer: {dialog.SelectedPrinter}";
        }
        catch (Exception ex)
        {
            StatusText = $"Print failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleAlwaysOnTop()
    {
        AlwaysOnTop = !AlwaysOnTop;
        StatusText = AlwaysOnTop ? "Always on Top: On" : "Always on Top: Off";
    }

    [RelayCommand]
    private async Task OpenPreferences()
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var current = _settingsService.Current;
        var dialog = new PreferencesWindow(current);
        var ok = await dialog.ShowDialog<bool>(owner);
        if (!ok || dialog.Result == null)
            return;

        _settingsService.Update(s =>
        {
            s.FontSize = dialog.Result.FontSize;
            s.FontFamily = dialog.Result.FontFamily;
            s.TabSize = dialog.Result.TabSize;
            s.InsertSpaces = dialog.Result.InsertSpaces;
            s.WordCompletion = dialog.Result.WordCompletion;
            s.AutoCloseBrackets = dialog.Result.AutoCloseBrackets;
            s.AutoIndent = dialog.Result.AutoIndent;
            s.IndentGuides = dialog.Result.IndentGuides;
            s.ShowWhitespace = dialog.Result.ShowWhitespace;
            s.TrimTrailingWhitespaceOnSave = dialog.Result.TrimTrailingWhitespaceOnSave;
            s.EnsureFinalNewlineOnSave = dialog.Result.EnsureFinalNewlineOnSave;
            s.BackupOnSave = dialog.Result.BackupOnSave;
            s.AutosaveIntervalSeconds = dialog.Result.AutosaveIntervalSeconds;
            s.MinimizeToTray = dialog.Result.MinimizeToTray;
            s.LanguageServers = dialog.Result.LanguageServers;
        });
        ConfigureAutosaveTimer();

        foreach (var tab in Tabs)
        {
            tab.FontSize = dialog.Result.FontSize;
            tab.FontFamily = dialog.Result.FontFamily;
        }

        await _lspManager.ResetAsync();
        StatusText = "Preferences saved";
    }

    private static void MarkDirtyForMetaChange(DocumentViewModel tab)
    {
        tab.IsDirty = true;
        if (!tab.Title.EndsWith('*'))
            tab.Title += "*";
    }

    private void SetTheme(string theme)
    {
        _settingsService.Update(s => s.Theme = theme);
        EditorThemeService.ApplyAppTheme(theme);
        StatusText = $"Theme: {theme}";
    }

    private void RememberRecent(string path)
    {
        _settingsService.Update(s =>
        {
            s.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            s.RecentFiles.Insert(0, path);
            var max = Math.Max(1, s.MaxRecentFiles);
            if (s.RecentFiles.Count > max)
                s.RecentFiles.RemoveRange(max, s.RecentFiles.Count - max);
        });
        RefreshRecentFiles();
    }

    private void RemoveRecent(string path)
    {
        _settingsService.Update(s =>
            s.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _settingsService.Current.RecentFiles)
            RecentFiles.Add(path);
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckExternalChangeAsync(DocumentViewModel tab)
    {
        if (string.IsNullOrWhiteSpace(tab.FilePath) || tab.LastWriteTimeUtc == null)
            return;

        if (Interlocked.CompareExchange(ref _externalChangeGate, 1, 0) != 0)
            return;

        try
        {
            var current = File.GetLastWriteTimeUtc(tab.FilePath);
            if (current <= tab.LastWriteTimeUtc.Value.AddMilliseconds(50))
                return;

            var owner = GetMainWindow();
            if (owner == null) return;

            var messageWindow = new Window
            {
                Title = "File Changed",
                Width = 420,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = BuildReloadPanel(tab, out var reload, out var keep)
            };

            var tcs = new TaskCompletionSource<bool>();
            reload.Click += (_, _) => { tcs.TrySetResult(true); messageWindow.Close(); };
            keep.Click += (_, _) => { tcs.TrySetResult(false); messageWindow.Close(); };
            messageWindow.Closed += (_, _) => tcs.TrySetResult(false);

            var dialogTask = messageWindow.ShowDialog(owner);
            var shouldReload = await tcs.Task;
            await dialogTask;
            if (!shouldReload)
            {
                tab.LastWriteTimeUtc = current;
                return;
            }

            var snapshot = await _fileService.ReadFileAsync(tab.FilePath);
            tab.ApplySnapshot(snapshot, tab.FilePath);
            StatusText = $"Reloaded {Path.GetFileName(tab.FilePath)}";
        }
        catch
        {
            // Ignore race conditions
        }
        finally
        {
            Interlocked.Exchange(ref _externalChangeGate, 0);
        }
    }

    private static StackPanel BuildReloadPanel(DocumentViewModel tab, out Button reload, out Button keep)
    {
        reload = new Button { Content = "Reload", Width = 90, IsDefault = true };
        keep = new Button { Content = "Keep Mine", Width = 100, IsCancel = true };
        return new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"\"{Path.GetFileName(tab.FilePath)}\" was modified externally. Reload?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { reload, keep }
                }
            }
        };
    }

    private DocumentEditor? GetSelectedEditor()
    {
        return GetMainWindow()?.FindSelectedDocumentEditor();
    }

    private static MainWindow? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow as MainWindow;
    }

    private void SaveSession()
    {
        var state = new SessionState
        {
            SelectedPath = SelectedTab?.FilePath,
            Tabs = Tabs
                .Where(t => !string.IsNullOrWhiteSpace(t.FilePath))
                .Select(t => new SessionTabState
                {
                    Path = t.FilePath!,
                    Line = Math.Max(1, t.LineNumber),
                    Column = Math.Max(1, t.ColumnNumber)
                })
                .ToList()
        };
        _sessionService.SaveSession(state);
    }

    private void RestoreSession()
    {
        var state = _sessionService.LoadSession();
        DocumentViewModel? selected = null;

        foreach (var entry in state.Tabs)
        {
            if (string.IsNullOrWhiteSpace(entry.Path) || !File.Exists(entry.Path))
                continue;

            try
            {
                var snapshot = _fileService.ReadFile(entry.Path);
                var tab = new DocumentViewModel
                {
                    WordWrap = WordWrap,
                    FontSize = _settingsService.Current.FontSize,
                    FontFamily = _settingsService.Current.FontFamily,
                    PendingRestoreLine = Math.Max(1, entry.Line),
                    PendingRestoreColumn = Math.Max(1, entry.Column)
                };
                tab.ApplySnapshot(snapshot, entry.Path);
                Tabs.Add(tab);

                if (state.SelectedPath != null &&
                    string.Equals(state.SelectedPath, entry.Path, StringComparison.OrdinalIgnoreCase))
                    selected = tab;
            }
            catch
            {
                // Skip unreadable session files
            }
        }

        if (Tabs.Count > 0)
            SelectedTab = selected ?? Tabs[0];
    }
}
