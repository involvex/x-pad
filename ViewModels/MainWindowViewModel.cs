using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using x_pad.Models;
using x_pad.Services;
using x_pad.Services.Lsp;
using x_pad.ViewModels.Dock;
using x_pad.Views;

namespace x_pad.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly FileService _fileService = new();
    private readonly SessionService _sessionService = new();
    private readonly FormatService _formatService = new();
    private readonly RecoveryService _recoveryService = new();
    private readonly OpenFileWatcher _fileWatcher = new();
    private readonly WorkspaceService _workspace = new();
    private readonly SettingsService _settingsService;
    private readonly LspManager _lspManager;
    private readonly FileTreeViewModel _fileTree;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private readonly List<ClosedTabSnapshot> _closedTabs = new();
    private const int MaxClosedTabs = 20;
    private int _untitledCount;
    private bool _autosaveRunning;
    private int _externalChangeGate;
    private int _watcherPromptGate;
    private bool _recoveryPromptScheduled;
    private IReadOnlyList<LspDiagnostic> _selectedTabDiagnostics = [];
    private readonly DockLayoutService _dockLayout = new();
    private DockControl? _dockControl;
    private IFactory? _dockFactory;
    private bool _syncingPanelVisibility;

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
        public bool InsertSpaces { get; init; } = true;
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
    private bool showWhitespace;

    [ObservableProperty]
    private bool alwaysOnTop;

    [ObservableProperty]
    private bool isTerminalVisible;

    [ObservableProperty]
    private bool isExplorerVisible;

    [ObservableProperty]
    private string theme = "Dark";

    public FileTreeViewModel FileTree => _fileTree;
    public ExplorerToolViewModel ExplorerTool { get; } = new();
    public TerminalToolViewModel TerminalTool { get; } = new();
    public WorkspaceService Workspace => _workspace;
    public string? WorkspaceRoot => _workspace.Root;

    private readonly UpdateService _updateService = new();
    private bool _startupUpdateScheduled;
    private bool _suppressViewStatus;
    private DocumentViewModel? _splitHookTab;
    private PropertyChangedEventHandler? _splitHookHandler;

    public ObservableCollection<string> RecentFiles { get; } = new();
    public ObservableCollection<FavoriteMenuItem> FavoriteMenuItems { get; } = new();

    public event EventHandler? RecentFilesChanged;

    public SettingsService Settings => _settingsService;
    public LspManager Lsp => _lspManager;

    public bool IsSplitVerticalActive =>
        SelectedTab is { IsSplit: true, SplitVertical: true };

    public bool IsSplitHorizontalActive =>
        SelectedTab is { IsSplit: true, SplitVertical: false };

    public bool IsThemeDark
    {
        get => string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Dark"); }
    }

    public bool IsThemeLight
    {
        get => string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Light"); }
    }

    public bool IsThemeSystem
    {
        get => string.Equals(Theme, "System", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("System"); }
    }

    public bool IsThemeMonokai
    {
        get => string.Equals(Theme, "Monokai", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Monokai"); }
    }

    public bool IsThemeHacker
    {
        get => string.Equals(Theme, "Hacker", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Hacker"); }
    }

    public bool IsThemeAmoled
    {
        get => string.Equals(Theme, "Amoled", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Amoled"); }
    }

    public bool IsThemeGithub
    {
        get => string.Equals(Theme, "Github", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Github"); }
    }

    public MainWindowViewModel(string[]? args = null, SettingsService? settings = null)
    {
        _settingsService = settings ?? new SettingsService();
        _lspManager = new LspManager(_settingsService, () => _workspace.Root);
        _lspManager.DiagnosticsReceived += OnLspDiagnostics;
        _fileTree = new FileTreeViewModel(
            _workspace,
            () => _settingsService.Current.WorkspaceExcludeNames is { Count: > 0 } names
                ? names
                : WorkspaceService.DefaultExcludeNames.ToList());
        _workspace.Changed += OnWorkspaceChanged;
        _suppressViewStatus = true;
        WordWrap = _settingsService.Current.WordWrap;
        ShowWhitespace = _settingsService.Current.ShowWhitespace;
        Theme = _settingsService.Current.Theme;
        _suppressViewStatus = false;
        RefreshRecentFiles();
        RefreshFavoriteFiles();

        EditorThemeService.ApplyAppTheme(_settingsService.Current.Theme);

        var openedFromArgs = false;
        foreach (var folder in CliArgs.ResolveWorkspacePaths(args))
        {
            if (OpenWorkspace(folder))
                openedFromArgs = true;
        }

        foreach (var path in CliArgs.ResolveFilePaths(args))
        {
            OpenFileSync(path);
            openedFromArgs = true;
        }

        if (!openedFromArgs)
        {
            TryRestoreLastWorkspace();
            RestoreSession();
        }

        if (Tabs.Count == 0)
            NewTab();

        _autosaveTimer = new DispatcherTimer();
        _autosaveTimer.Tick += async (_, _) => await AutosaveTickAsync();
        ConfigureAutosaveTimer();
        _settingsService.Changed += ConfigureAutosaveTimer;

        _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _recoveryTimer.Tick += (_, _) => PersistRecoverySnapshots();
        _recoveryTimer.Start();

        _fileWatcher.FileChanged += OnWatchedFileChanged;
        SyncFileWatchers();
        ScheduleRecoveryPrompt();
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        IsExplorerVisible = _workspace.Root != null;
        OnPropertyChanged(nameof(WorkspaceRoot));
        _ = _lspManager.ResetAsync();
        StatusText = _workspace.Root == null
            ? "Folder closed"
            : $"Folder: {Path.GetFileName(_workspace.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
    }

    public bool OpenWorkspace(string path)
    {
        if (!_workspace.TryOpen(path))
            return false;

        _settingsService.Update(s => s.LastWorkspacePath = _workspace.Root);
        IsExplorerVisible = true;
        return true;
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

    private void OnLspDiagnostics(string uri, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedTab?.FilePath == null)
                return;

            var selectedUri = new Uri(Path.GetFullPath(SelectedTab.FilePath)).AbsoluteUri;
            if (!string.Equals(selectedUri, uri, StringComparison.OrdinalIgnoreCase))
                return;

            _selectedTabDiagnostics = diagnostics;
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

        var preferSpaces = _settingsService.Current.InsertSpaces;
        if (File.Exists(path))
        {
            var snapshot = _fileService.ReadFile(path);
            tab.ApplySnapshot(snapshot, path, preferSpaces);
            RememberRecent(path);
            if (snapshot.IsLargeFile)
                StatusText = LargeFilePolicy.StatusMessage(snapshot.SourceBytes?.LongLength ?? 0);
        }
        else
        {
            tab.SetNewFile(path, preferSpaces);
        }

        Tabs.Add(tab);
        SelectedTab = tab;
        SyncFileWatchers();
    }

    partial void OnWordWrapChanged(bool value)
    {
        _settingsService.Update(s => s.WordWrap = value);
        if (!_suppressViewStatus)
            StatusText = value ? "Word Wrap: On" : "Word Wrap: Off";
    }

    partial void OnShowWhitespaceChanged(bool value)
    {
        _settingsService.Update(s => s.ShowWhitespace = value);
        if (!_suppressViewStatus)
            StatusText = value ? "Whitespace: On" : "Whitespace: Off";
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        if (!_suppressViewStatus)
            StatusText = value ? "Always on Top: On" : "Always on Top: Off";
    }

    partial void OnIsExplorerVisibleChanged(bool value)
    {
        if (_syncingPanelVisibility || _dockFactory == null)
            return;

        EnsureTool(ExplorerTool, "ExplorerToolDock", value);
    }

    partial void OnIsTerminalVisibleChanged(bool value)
    {
        if (!_suppressViewStatus)
            StatusText = value ? "Terminal shown" : "Terminal hidden";

        if (!_syncingPanelVisibility && _dockFactory != null)
            EnsureTool(TerminalTool, "TerminalToolDock", value);

        if (!value)
            return;

        var panel = TerminalTool.Panel;
        panel.SetWorkingDirectoryHint(SelectedTab?.FilePath);
        panel.EnsureStarted();
    }

    partial void OnThemeChanged(string value)
    {
        NotifyThemeProperties();
    }

    partial void OnSelectedTabChanged(DocumentViewModel? value)
    {
        HookSelectedTabSplit(value);
        _selectedTabDiagnostics = [];

        if (value != null)
            _ = CheckExternalChangeAsync(value);

        NotifySplitProperties();

        if (IsTerminalVisible)
            TerminalTool.Panel.SetWorkingDirectoryHint(value?.FilePath);

        if (_dockFactory != null && value != null)
        {
            var container = _dockFactory.GetContainerFromItem(value);
            if (container != null)
                _dockFactory.SetActiveDockable(container);
        }

        ScheduleStartupUpdateCheck();
    }

    private void HookSelectedTabSplit(DocumentViewModel? tab)
    {
        if (_splitHookTab != null && _splitHookHandler != null)
            _splitHookTab.PropertyChanged -= _splitHookHandler;

        _splitHookTab = tab;
        if (tab == null)
        {
            _splitHookHandler = null;
            return;
        }

        _splitHookHandler = (_, args) =>
        {
            if (args.PropertyName is nameof(DocumentViewModel.IsSplit) or nameof(DocumentViewModel.SplitVertical))
                NotifySplitProperties();
        };
        tab.PropertyChanged += _splitHookHandler;
    }

    private void NotifySplitProperties()
    {
        OnPropertyChanged(nameof(IsSplitVerticalActive));
        OnPropertyChanged(nameof(IsSplitHorizontalActive));
    }

    private void NotifyThemeProperties()
    {
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeMonokai));
        OnPropertyChanged(nameof(IsThemeHacker));
        OnPropertyChanged(nameof(IsThemeAmoled));
        OnPropertyChanged(nameof(IsThemeGithub));
    }

    private void ApplyTheme(string theme)
    {
        if (string.Equals(Theme, theme, StringComparison.OrdinalIgnoreCase))
        {
            _settingsService.Update(s => s.Theme = theme);
            EditorThemeService.ApplyAppTheme(theme);
            StatusText = $"Theme: {theme}";
            NotifyThemeProperties();
            return;
        }

        Theme = theme;
        _settingsService.Update(s => s.Theme = theme);
        EditorThemeService.ApplyAppTheme(theme);
        StatusText = $"Theme: {theme}";
    }

    private void ScheduleStartupUpdateCheck()
    {
        if (_startupUpdateScheduled)
            return;
        _startupUpdateScheduled = true;

        if (!_settingsService.Current.CheckForUpdatesOnStartup)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(2500);
            await CheckForUpdatesAsync(interactive: false);
        });
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
        tab.SetUntitled($"Untitled {_untitledCount}", _settingsService.Current.InsertSpaces);
        Tabs.Add(tab);
        SelectedTab = tab;
        StatusText = "New document";
        EnsureDocumentsBound();
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

    [RelayCommand]
    private async Task OpenFolder()
    {
        var topLevel = GetMainWindow();
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder == null)
            return;

        if (OpenWorkspace(folder.Path.LocalPath))
            StatusText = $"Opened folder: {_workspace.Root}";
        else
            StatusText = "Open folder failed";
    }

    [RelayCommand]
    private void CloseFolder()
    {
        _workspace.Close();
        _settingsService.Update(s => s.LastWorkspacePath = null);
    }

    [RelayCommand]
    private void ToggleExplorer()
    {
        if (_workspace.Root == null)
        {
            if (!TryOpenExplorerFolder())
                return;

            IsExplorerVisible = true;
            return;
        }

        IsExplorerVisible = !IsExplorerVisible;
    }

    private void TryRestoreLastWorkspace()
    {
        var last = _settingsService.Current.LastWorkspacePath;
        if (string.IsNullOrWhiteSpace(last) || !Directory.Exists(last))
            return;

        OpenWorkspace(last);
    }

    private bool TryOpenExplorerFolder()
    {
        var last = _settingsService.Current.LastWorkspacePath;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last))
        {
            if (OpenWorkspace(last))
            {
                StatusText = $"Folder: {Path.GetFileName(last.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
                return true;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home) || !Directory.Exists(home))
        {
            StatusText = "Could not open home folder";
            return false;
        }

        if (!OpenWorkspace(home))
        {
            StatusText = "Open folder failed";
            return false;
        }

        StatusText = $"Folder: {Path.GetFileName(home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
        return true;
    }

    [RelayCommand]
    private async Task FindInFolder()
    {
        if (string.IsNullOrWhiteSpace(_workspace.Root))
        {
            StatusText = "Open a folder to search";
            return;
        }

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var excludes = _settingsService.Current.WorkspaceExcludeNames is { Count: > 0 } names
            ? names
            : WorkspaceService.DefaultExcludeNames.ToList();
        var dialog = new FindInFolderWindow(_workspace.Root, excludes, Tabs);
        var hit = await dialog.ShowDialog<FolderFindHit?>(owner);

        foreach (var (path, text) in dialog.UpdatedOpenTabs)
        {
            var tab = Tabs.FirstOrDefault(t =>
                t.FilePath != null &&
                string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (tab == null)
                continue;
            tab.Document.Replace(0, tab.Document.TextLength, text);
        }

        if (dialog.ReplaceCount > 0)
            StatusText = $"Replaced {dialog.ReplaceCount} in folder";

        if (hit == null)
            return;

        await OpenFileFromPath(hit.Path, hit.Match.Line, hit.Match.Column);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        GetSelectedEditor()?.GoToMatch(hit.Match.Line, hit.Match.Column, hit.Match.Length);
        StatusText = $"Found in {hit.Location}";
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

            var preferSpaces = _settingsService.Current.InsertSpaces;
            if (File.Exists(path))
            {
                var snapshot = await _fileService.ReadFileAsync(path);
                tab.ApplySnapshot(snapshot, path, preferSpaces);
                RememberRecent(path);
                StatusText = snapshot.IsLargeFile
                    ? LargeFilePolicy.StatusMessage(snapshot.SourceBytes?.LongLength ?? 0)
                    : $"Opened {Path.GetFileName(path)}";
            }
            else
            {
                tab.SetNewFile(path, preferSpaces);
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
            SyncFileWatchers();
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

    private async Task<bool> SaveTabAsync(DocumentViewModel tab, bool runFormatOnSave = true)
    {
        if (string.IsNullOrWhiteSpace(tab.FilePath))
            return await SaveTabAsAsync(tab, runFormatOnSave);

        if (tab.IsReadOnly)
        {
            StatusText = "File is read-only — use Save As or clear Read-only";
            return false;
        }

        try
        {
            ApplySaveCleanup(tab);
            _fileWatcher.IgnoreOwnWrite(tab.FilePath);
            await _fileService.WriteFileAsync(
                tab.FilePath,
                tab.Text,
                tab.TextEncoding,
                tab.HasBom,
                tab.LineEndingKind,
                backupExisting: _settingsService.Current.BackupOnSave);
            tab.MarkClean(tab.FilePath);
            RefreshSourceBytesAfterSave(tab);
            RememberRecent(tab.FilePath);
            SaveSession();
            PersistRecoverySnapshots();
            StatusText = $"Saved {Path.GetFileName(tab.FilePath)}";
            if (runFormatOnSave)
                await TryFormatTabAsync(tab, onlyIfFormatOnSave: true);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            return false;
        }
    }

    private void RefreshSourceBytesAfterSave(DocumentViewModel tab)
    {
        if (string.IsNullOrWhiteSpace(tab.FilePath))
            return;

        try
        {
            tab.SourceBytes = FileService.Encode(tab.Text, tab.TextEncoding, tab.HasBom);
            var info = new FileInfo(tab.FilePath);
            tab.IsReadOnly = (info.Attributes & FileAttributes.ReadOnly) != 0;
            tab.IsLargeFile = LargeFilePolicy.IsLarge(tab.SourceBytes.LongLength);
        }
        catch
        {
            // Ignore attribute refresh failures
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

    private async Task<bool> SaveTabAsAsync(DocumentViewModel tab, bool runFormatOnSave = true)
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
            ApplySaveCleanup(tab);
            _fileWatcher.IgnoreOwnWrite(path);
            await _fileService.WriteFileAsync(
                path,
                tab.Text,
                tab.TextEncoding,
                tab.HasBom,
                tab.LineEndingKind,
                backupExisting: _settingsService.Current.BackupOnSave && File.Exists(path));
            tab.IsReadOnly = false;
            tab.MarkClean(path);
            RefreshSourceBytesAfterSave(tab);
            RememberRecent(path);
            SaveSession();
            SyncFileWatchers();
            PersistRecoverySnapshots();
            StatusText = $"Saved {Path.GetFileName(path)}";
            if (runFormatOnSave)
                await TryFormatTabAsync(tab, onlyIfFormatOnSave: true);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    private async Task FormatDocument()
    {
        if (SelectedTab == null)
            return;

        var command = _settingsService.Current.FormatCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            StatusText = "Set a format command in Preferences";
            return;
        }

        // Persist first so the external tool can read the file; skip format-on-save to avoid a double run.
        if (string.IsNullOrWhiteSpace(SelectedTab.FilePath) || SelectedTab.IsDirty)
        {
            if (!await SaveTabAsync(SelectedTab, runFormatOnSave: false))
                return;
        }

        await TryFormatTabAsync(SelectedTab, onlyIfFormatOnSave: false);
    }

    private async Task TryFormatTabAsync(DocumentViewModel tab, bool onlyIfFormatOnSave)
    {
        var settings = _settingsService.Current;
        if (string.IsNullOrWhiteSpace(settings.FormatCommand))
            return;
        if (onlyIfFormatOnSave && !settings.FormatOnSave)
            return;
        if (string.IsNullOrWhiteSpace(tab.FilePath) || !File.Exists(tab.FilePath))
            return;
        if (!FormatCommandHelper.MatchesExtension(tab.FilePath, settings.FormatExtensions))
            return;

        var line = Math.Max(1, tab.LineNumber);
        var column = Math.Max(1, tab.ColumnNumber);
        StatusText = $"Formatting {Path.GetFileName(tab.FilePath)}…";

        var result = await _formatService.FormatFileAsync(settings.FormatCommand, tab.FilePath);
        if (!result.Success)
        {
            StatusText = $"Format failed: {result.Error}";
            return;
        }

        try
        {
            var snapshot = await _fileService.ReadFileAsync(tab.FilePath);
            tab.PendingRestoreLine = line;
            tab.PendingRestoreColumn = column;
            tab.ApplySnapshot(snapshot, tab.FilePath, settings.InsertSpaces);
            var editor = GetMainWindow()?.FindDocumentEditor(tab) ??
                         (ReferenceEquals(tab, SelectedTab) ? GetSelectedEditor() : null);
            editor?.RefreshEditorOptions();
            editor?.RestorePendingCaret();
            StatusText = $"Formatted {Path.GetFileName(tab.FilePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Format reload failed: {ex.Message}";
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

        await CloseTabCoreAsync(tab);
    }

    private async Task CloseTabCoreAsync(DocumentViewModel tab)
    {
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
            snap.EncodingName,
            snap.InsertSpaces);

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
            InsertSpaces = tab.InsertSpaces,
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

        // Persist open tabs (including discarded unsaved buffers) before clearing crash recovery.
        SaveSession();
        _recoveryService.MarkCleanExit();
        _fileWatcher.Dispose();
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
        if (SelectedTab == null)
            return;

        SelectedTab.WordWrap = !SelectedTab.WordWrap;
        StatusText = SelectedTab.WordWrap ? "Word Wrap: On (this tab)" : "Word Wrap: Off (this tab)";
    }

    [RelayCommand]
    private void ToggleReadOnly()
    {
        if (SelectedTab == null)
            return;

        SelectedTab.IsReadOnly = !SelectedTab.IsReadOnly;
        GetSelectedEditor()?.RefreshEditorOptions();
        StatusText = SelectedTab.IsReadOnly ? "Read-only: On" : "Read-only: Off";
    }

    [RelayCommand]
    private void ToggleWhitespace()
    {
        ShowWhitespace = !ShowWhitespace;
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
        DockPanel.SetDock(ok, Avalonia.Controls.Dock.Bottom);

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
    private async Task CopyFullPath(object? parameter)
    {
        var path = ResolveTabPath(parameter);
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

        await top.Clipboard.SetTextAsync(path);
        StatusText = "Path copied";
    }

    [RelayCommand]
    private void RevealInExplorer(object? parameter)
    {
        var path = ResolveTabPath(parameter);
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
            tab.ApplySnapshot(snapshot, tab.FilePath, _settingsService.Current.InsertSpaces);
            GetSelectedEditor()?.RefreshEditorOptions();
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
    private async Task GoToDefinition()
    {
        if (SelectedTab?.FilePath == null)
        {
            StatusText = "Save the file to use Go to Definition";
            return;
        }

        if (SelectedTab.IsLargeFile)
        {
            StatusText = "Go to Definition disabled for large files";
            return;
        }

        var probe = LanguageHelper.GetProbePath(SelectedTab.FilePath, SelectedTab.LanguageOverrideExtension);
        var languageId = LanguageHelper.GetLspLanguageId(probe);
        var client = await _lspManager.GetClientAsync(languageId, SelectedTab.FilePath);
        if (client == null)
        {
            StatusText = "No language server for this file";
            return;
        }

        var uri = new Uri(Path.GetFullPath(SelectedTab.FilePath)).AbsoluteUri;
        var locations = await client.DefinitionAsync(
            uri,
            Math.Max(0, SelectedTab.LineNumber - 1),
            Math.Max(0, SelectedTab.ColumnNumber - 1));

        if (locations.Count == 0)
        {
            StatusText = "No definition found";
            return;
        }

        if (!await JumpToLspLocationAsync(locations[0], "Definition"))
            StatusText = "Definition target not on disk";
    }

    [RelayCommand]
    private async Task FindReferences()
    {
        if (SelectedTab?.FilePath == null)
        {
            StatusText = "Save the file to use Find References";
            return;
        }

        if (SelectedTab.IsLargeFile)
        {
            StatusText = "Find References disabled for large files";
            return;
        }

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var probe = LanguageHelper.GetProbePath(SelectedTab.FilePath, SelectedTab.LanguageOverrideExtension);
        var languageId = LanguageHelper.GetLspLanguageId(probe);
        var client = await _lspManager.GetClientAsync(languageId, SelectedTab.FilePath);
        if (client == null)
        {
            StatusText = "No language server for this file";
            return;
        }

        var uri = new Uri(Path.GetFullPath(SelectedTab.FilePath)).AbsoluteUri;
        var locations = await client.ReferencesAsync(
            uri,
            Math.Max(0, SelectedTab.LineNumber - 1),
            Math.Max(0, SelectedTab.ColumnNumber - 1));

        if (locations.Count == 0)
        {
            StatusText = "No references found";
            return;
        }

        if (locations.Count == 1)
        {
            if (!await JumpToLspLocationAsync(locations[0], "Reference"))
                StatusText = "Reference target not on disk";
            return;
        }

        var dialog = new FindReferencesWindow(locations);
        var hit = await dialog.ShowDialog<ReferenceHit?>(owner);
        if (hit == null)
            return;

        if (!await JumpToLspLocationAsync(hit.Location, "Reference"))
            StatusText = "Reference target not on disk";
    }

    private async Task<bool> JumpToLspLocationAsync(LspLocation target, string label)
    {
        var path = LspNavigationParser.TryUriToLocalPath(target.Uri);
        if (path == null || !File.Exists(path))
            return false;

        await OpenFileFromPath(path, target.Line + 1, target.Character + 1);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        GetSelectedEditor()?.GoToMatch(
            target.Line + 1,
            target.Character + 1,
            Math.Max(1, target.EndCharacter - target.Character));
        StatusText = $"{label}: {Path.GetFileName(path)}:{target.Line + 1}";
        return true;
    }

    [RelayCommand]
    private async Task ShowDocumentOutline()
    {
        if (SelectedTab?.FilePath == null)
        {
            StatusText = "Save the file to use Outline";
            return;
        }

        if (SelectedTab.IsLargeFile)
        {
            StatusText = "Outline disabled for large files";
            return;
        }

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var probe = LanguageHelper.GetProbePath(SelectedTab.FilePath, SelectedTab.LanguageOverrideExtension);
        var languageId = LanguageHelper.GetLspLanguageId(probe);
        var client = await _lspManager.GetClientAsync(languageId, SelectedTab.FilePath);
        if (client == null)
        {
            StatusText = "No language server for this file";
            return;
        }

        var uri = new Uri(Path.GetFullPath(SelectedTab.FilePath)).AbsoluteUri;
        var symbols = await client.DocumentSymbolsAsync(uri);
        var dialog = new DocumentOutlineWindow(symbols);
        var picked = await dialog.ShowDialog<LspDocumentSymbol?>(owner);
        if (picked == null)
            return;

        GetSelectedEditor()?.GoToMatch(picked.Line, picked.Character, 1);
        StatusText = $"Outline: {picked.Name}";
    }

    [RelayCommand]
    private async Task ShowCodeActions()
    {
        if (SelectedTab?.FilePath == null)
        {
            StatusText = "Save the file to use Code Actions";
            return;
        }

        if (SelectedTab.IsLargeFile)
        {
            StatusText = "Code Actions disabled for large files";
            return;
        }

        if (SelectedTab.IsReadOnly)
        {
            StatusText = "Code Actions blocked — buffer is read-only";
            return;
        }

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var editor = GetSelectedEditor();
        if (editor == null ||
            !editor.TryGetLspRange(out var startLine, out var startCharacter, out var endLine, out var endCharacter))
        {
            StatusText = "No editor selection for Code Actions";
            return;
        }

        var probe = LanguageHelper.GetProbePath(SelectedTab.FilePath, SelectedTab.LanguageOverrideExtension);
        var languageId = LanguageHelper.GetLspLanguageId(probe);
        var client = await _lspManager.GetClientAsync(languageId, SelectedTab.FilePath);
        if (client == null)
        {
            StatusText = "No language server for this file";
            return;
        }

        var uri = new Uri(Path.GetFullPath(SelectedTab.FilePath)).AbsoluteUri;
        StatusText = "Fetching code actions…";
        var actions = await client.CodeActionsAsync(
            uri,
            startLine,
            startCharacter,
            endLine,
            endCharacter,
            _selectedTabDiagnostics);

        if (actions.Count == 0)
        {
            StatusText = "No code actions";
            return;
        }

        var dialog = new CodeActionsWindow(actions);
        var picked = await dialog.ShowDialog<LspCodeAction?>(owner);
        if (picked?.Edit == null)
        {
            if (picked is { HasCommandOnly: true })
                StatusText = "Command-only code actions are not supported yet";
            return;
        }

        var result = await LspWorkspaceEditApplier.ApplyAsync(
            picked.Edit,
            path =>
            {
                var tab = Tabs.FirstOrDefault(t =>
                    t.FilePath != null &&
                    string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
                return tab?.Document;
            },
            _fileService,
            path => OpenFileFromPath(path));

        if (result.Errors.Count > 0)
        {
            StatusText = result.Errors[0];
            return;
        }

        StatusText = result.DocumentsTouched == 1
            ? $"Applied: {picked.Title}"
            : $"Applied: {picked.Title} ({result.DocumentsTouched} files)";
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
    private void ToggleHackerTheme() => SetTheme("Hacker");

    [RelayCommand]
    private void ToggleAmoledTheme() => SetTheme("Amoled");

    [RelayCommand]
    private void ToggleGithubTheme() => SetTheme("Github");

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
    private async Task OpenFavorite(object? parameter)
    {
        if (parameter is not string path || string.IsNullOrWhiteSpace(path))
            return;

        if (!File.Exists(path))
        {
            RemoveFavoritePath(path);
            StatusText = "Favorite missing — removed from list";
            return;
        }

        await OpenFileFromPath(path);
    }

    [RelayCommand]
    private void ClearFavorites()
    {
        _settingsService.Update(s => s.FavoriteFiles.Clear());
        RefreshFavoriteFiles();
        StatusText = "Favorites cleared";
    }

    [RelayCommand]
    private void AddFavorite(object? parameter)
    {
        var path = ResolveTabPath(parameter);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "Save the file before adding to favorites";
            return;
        }

        _settingsService.Update(s =>
        {
            s.FavoriteFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            s.FavoriteFiles.Insert(0, path);
            var max = Math.Max(1, s.MaxFavoriteFiles);
            if (s.FavoriteFiles.Count > max)
                s.FavoriteFiles.RemoveRange(max, s.FavoriteFiles.Count - max);
        });
        RefreshFavoriteFiles();
        StatusText = $"Favorited {Path.GetFileName(path)}";
    }

    [RelayCommand]
    private void RemoveFavorite(object? parameter)
    {
        var path = ResolveTabPath(parameter);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "No file path for this tab";
            return;
        }

        RemoveFavoritePath(path);
        StatusText = $"Removed from favorites: {Path.GetFileName(path)}";
    }

    private void RemoveFavoritePath(string path)
    {
        _settingsService.Update(s =>
            s.FavoriteFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
        RefreshFavoriteFiles();
    }

    private void RefreshFavoriteFiles()
    {
        FavoriteMenuItems.Clear();
        foreach (var path in _settingsService.Current.FavoriteFiles)
        {
            FavoriteMenuItems.Add(new FavoriteMenuItem
            {
                Name = Path.GetFileName(path),
                Path = path
            });
        }
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
    private void CycleIndentStyle()
    {
        if (SelectedTab == null) return;
        SelectedTab.SetInsertSpaces(!SelectedTab.InsertSpaces);
        GetSelectedEditor()?.RefreshEditorOptions();
        StatusText = $"Indent: {SelectedTab.IndentStyle}";
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
    private async Task CycleEncoding()
    {
        if (SelectedTab == null) return;
        if (!SelectedTab.HasBom && SelectedTab.Encoding.StartsWith("UTF-8", StringComparison.OrdinalIgnoreCase))
            await SetEncodingUtf8Bom();
        else if (SelectedTab.Encoding.Contains("UTF-8", StringComparison.OrdinalIgnoreCase))
            await SetEncodingUtf16Le();
        else if (SelectedTab.Encoding.Contains("UTF-16 LE", StringComparison.OrdinalIgnoreCase))
            await SetEncodingUtf16Be();
        else if (SelectedTab.Encoding.Contains("UTF-16", StringComparison.OrdinalIgnoreCase))
            await SetEncodingLatin1();
        else
            await SetEncodingUtf8();
    }

    [RelayCommand]
    private Task SetEncodingUtf8() =>
        ApplyEncodingAsync(FileService.CreateUtf8(false), hasBom: false, "UTF-8");

    [RelayCommand]
    private Task SetEncodingUtf8Bom() =>
        ApplyEncodingAsync(FileService.CreateUtf8(false), hasBom: true, "UTF-8 BOM");

    [RelayCommand]
    private Task SetEncodingUtf16Le() =>
        ApplyEncodingAsync(new UnicodeEncoding(bigEndian: false, byteOrderMark: true), hasBom: true, "UTF-16 LE");

    [RelayCommand]
    private Task SetEncodingUtf16Be() =>
        ApplyEncodingAsync(new UnicodeEncoding(bigEndian: true, byteOrderMark: true), hasBom: true, "UTF-16 BE");

    [RelayCommand]
    private Task SetEncodingLatin1() =>
        ApplyEncodingAsync(Encoding.Latin1, hasBom: false, "LATIN1");

    private async Task ApplyEncodingAsync(Encoding encoding, bool hasBom, string displayName)
    {
        if (SelectedTab == null)
            return;

        var tab = SelectedTab;
        var canRedeode = tab.SourceBytes is { Length: > 0 } ||
                         (!string.IsNullOrWhiteSpace(tab.FilePath) && File.Exists(tab.FilePath));

        if (canRedeode)
        {
            if (tab.IsDirty)
            {
                var owner = GetMainWindow();
                if (owner == null)
                    return;

                if (!await ConfirmEncodingReloadAsync(owner, tab, displayName))
                    return;
            }

            try
            {
                var bytes = tab.SourceBytes;
                if (bytes == null || bytes.Length == 0)
                    bytes = await File.ReadAllBytesAsync(tab.FilePath!);

                var (decodedEncoding, decodedBom, text) = FileService.DecodeWith(bytes, encoding, hasBom);
                tab.SourceBytes = bytes;
                tab.ApplyDecodedText(text, decodedEncoding, decodedBom, displayName);
                GetSelectedEditor()?.RefreshGrammar();
                GetSelectedEditor()?.RefreshEditorOptions();
                StatusText = $"Re-decoded as {displayName}";
                return;
            }
            catch (Exception ex)
            {
                StatusText = $"Re-decode failed: {ex.Message}";
                return;
            }
        }

        tab.TextEncoding = encoding;
        tab.HasBom = hasBom;
        tab.Encoding = displayName;
        MarkDirtyForMetaChange(tab);
        StatusText = $"Encoding: {displayName}";
    }

    private static async Task<bool> ConfirmEncodingReloadAsync(
        Window owner,
        DocumentViewModel tab,
        string displayName)
    {
        var prompt = new Window
        {
            Title = "Reload Encoding",
            Width = 440,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var reload = new Button { Content = "Re-decode", Width = 100, IsDefault = true };
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
                    Text =
                        $"Re-decode \"{Path.GetFileName(tab.FilePath) ?? tab.Title.TrimEnd('*')}\" as {displayName} and discard unsaved changes?",
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
        return await tcs.Task;
    }

    [RelayCommand]
    private void ToggleSplitVertical()
    {
        if (SelectedTab == null) return;
        if (SelectedTab.IsSplit && SelectedTab.SplitVertical)
        {
            SelectedTab.IsSplit = false;
            StatusText = "Split: Off";
            NotifySplitProperties();
            return;
        }

        SelectedTab.SplitVertical = true;
        SelectedTab.IsSplit = true;
        StatusText = "Split: Vertical";
        NotifySplitProperties();
    }

    [RelayCommand]
    private void ToggleSplitHorizontal()
    {
        if (SelectedTab == null) return;
        if (SelectedTab.IsSplit && !SelectedTab.SplitVertical)
        {
            SelectedTab.IsSplit = false;
            StatusText = "Split: Off";
            NotifySplitProperties();
            return;
        }

        SelectedTab.SplitVertical = false;
        SelectedTab.IsSplit = true;
        StatusText = "Split: Horizontal";
        NotifySplitProperties();
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
            Entry("File: Open Folder…", "Ctrl+Shift+O", "File", () => OpenFolder()),
            Entry("File: Close Folder", null, "File", () => { CloseFolder(); return Task.CompletedTask; }),
            Entry("View: Toggle Explorer", null, "View", () => { ToggleExplorer(); return Task.CompletedTask; }),
            Entry("Edit: Find in Folder…", "Ctrl+Shift+H", "Edit", () => FindInFolder()),
            Entry("File: Save", "Ctrl+S", "File", () => SaveFile()),
            Entry("File: Save As…", "Ctrl+Shift+S", "File", () => SaveFileAs()),
            Entry("File: Save All", "Ctrl+Alt+S", "File", () => SaveAll()),
            Entry("File: Close Tab", "Ctrl+W", "File", () => CloseTab(SelectedTab)),
            Entry("File: Reopen Closed Tab", "Ctrl+Shift+T", "File", () => ReopenClosedTab()),
            Entry("File: Close All", null, "File", () => CloseAll()),
            Entry("File: Print…", "Ctrl+P", "File", () => Print()),
            Entry("File: Export as PDF…", null, "File", () => ExportPdf()),
            Entry("File: Copy Full Path", null, "File", () => CopyFullPath(SelectedTab)),
            Entry("File: Reveal in Explorer", null, "File", () => { RevealInExplorer(SelectedTab); return Task.CompletedTask; }),
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
            Entry("Edit: Go to Definition", "F12", "Edit", () => GoToDefinition()),
            Entry("Edit: Find References", "Shift+F12", "Edit", () => FindReferences()),
            Entry("View: Document Outline…", "Ctrl+Shift+.", "View", () => ShowDocumentOutline()),
            Entry("Edit: Code Actions…", "Ctrl+.", "Edit", () => ShowCodeActions()),
            Entry("Edit: Toggle Line Comment", "Ctrl+/", "Edit", () => { ToggleLineComment(); return Task.CompletedTask; }),
            Entry("Edit: Insert Date/Time", "F5", "Edit", () => { InsertDateTime(); return Task.CompletedTask; }),
            Entry("Edit: Duplicate Line", "Shift+Alt+Down", "Edit", () => { DuplicateLine(); return Task.CompletedTask; }),
            Entry("Help: Check for Updates…", null, "Help", () => CheckForUpdates()),
            Entry("Help: About X-Pad", null, "Help", () => ShowAbout()),
            Entry("Edit: Upper Case", "Ctrl+U", "Edit", () => { UpperCase(); return Task.CompletedTask; }),
            Entry("Edit: Lower Case", "Ctrl+Shift+U", "Edit", () => { LowerCase(); return Task.CompletedTask; }),
            Entry("Edit: Title Case", null, "Edit", () => { TitleCase(); return Task.CompletedTask; }),
            Entry("Edit: Sort Lines A→Z", null, "Edit", () => { SortLinesAscending(); return Task.CompletedTask; }),
            Entry("Edit: Join Lines", "Ctrl+J", "Edit", () => { JoinLines(); return Task.CompletedTask; }),
            Entry("Edit: Remove Empty Lines", null, "Edit", () => { RemoveEmptyLines(); return Task.CompletedTask; }),
            Entry("Edit: Remove Duplicate Lines", null, "Edit", () => { RemoveDuplicateLines(); return Task.CompletedTask; }),
            Entry("Edit: Trim Trailing Whitespace", null, "Edit", () => { TrimTrailingWhitespace(); return Task.CompletedTask; }),
            Entry("Edit: Format Document", "Shift+Alt+F", "Edit", () => FormatDocument()),
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
            Entry("File: Toggle Read-only", null, "File", () => { ToggleReadOnly(); return Task.CompletedTask; }),
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
            Entry("File: Clear Favorites", null, "File", () => { ClearFavorites(); return Task.CompletedTask; }),
            Entry("File: Add to Favorites", null, "File", () => { AddFavorite(SelectedTab); return Task.CompletedTask; }),
            Entry("File: Remove from Favorites", null, "File", () => { RemoveFavorite(SelectedTab); return Task.CompletedTask; }),
            Entry("View: Zoom In", "Ctrl+=", "View", () => { ZoomIn(); return Task.CompletedTask; }),
            Entry("View: Zoom Out", "Ctrl+-", "View", () => { ZoomOut(); return Task.CompletedTask; }),
            Entry("View: Zoom Reset", "Ctrl+0", "View", () => { ZoomReset(); return Task.CompletedTask; }),
            Entry("View: Dark Mode", null, "View", () => { ToggleDarkMode(); return Task.CompletedTask; }),
            Entry("View: Light Mode", null, "View", () => { ToggleLightMode(); return Task.CompletedTask; }),
            Entry("View: Monokai Theme", null, "View", () => { ToggleMonokaiTheme(); return Task.CompletedTask; }),
            Entry("View: Hacker Theme", null, "View", () => { ToggleHackerTheme(); return Task.CompletedTask; }),
            Entry("View: AMOLED Theme", null, "View", () => { ToggleAmoledTheme(); return Task.CompletedTask; }),
            Entry("View: GitHub Theme", null, "View", () => { ToggleGithubTheme(); return Task.CompletedTask; }),
            Entry("View: Toggle Explorer", "Ctrl+B", "View", () => { ToggleExplorer(); return Task.CompletedTask; }),
            Entry("View: Preferences…", "Ctrl+,", "View", () => OpenPreferences()),
            Entry("View: Command Palette…", "Ctrl+Shift+P", "View", () => OpenCommandPalette()),
            Entry("View: Quick Open…", "Ctrl+P", "View", () => OpenQuickOpen()),

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

        foreach (var item in FavoriteMenuItems)
        {
            var capture = item.Path;
            entries.Add(Entry(
                $"Favorite: {item.Name}",
                null,
                "Favorites",
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
    }

    [RelayCommand]
    private async Task OpenPreferences()
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var current = _settingsService.Current;
        var dialog = new PreferencesWindow(current, () => CheckForUpdatesAsync(interactive: true));
        var ok = await dialog.ShowDialog<bool>(owner);
        if (!ok || dialog.Result == null)
            return;

        _settingsService.Update(s =>
        {
            s.FontSize = dialog.Result.FontSize;
            s.FontFamily = dialog.Result.FontFamily;
            s.TabSize = dialog.Result.TabSize;
            s.InsertSpaces = dialog.Result.InsertSpaces;
            s.WordWrap = dialog.Result.WordWrap;
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
            s.Theme = dialog.Result.Theme;
            s.FormatCommand = dialog.Result.FormatCommand;
            s.FormatOnSave = dialog.Result.FormatOnSave;
            s.FormatExtensions = dialog.Result.FormatExtensions;
            s.LanguageServers = dialog.Result.LanguageServers;
            s.CheckForUpdatesOnStartup = dialog.Result.CheckForUpdatesOnStartup;
            s.KeyBindings = dialog.Result.KeyBindings;
        });

        GetMainWindow()?.ApplyKeyBindings();

        _suppressViewStatus = true;
        // WordWrap preference is the default for new tabs; leave open tabs alone.
        WordWrap = dialog.Result.WordWrap;
        ShowWhitespace = dialog.Result.ShowWhitespace;
        _suppressViewStatus = false;
        if (!string.Equals(Theme, dialog.Result.Theme, StringComparison.OrdinalIgnoreCase))
            ApplyTheme(dialog.Result.Theme);
        else
            EditorThemeService.ApplyAppTheme(dialog.Result.Theme);

        ConfigureAutosaveTimer();

        foreach (var tab in Tabs)
        {
            tab.FontSize = dialog.Result.FontSize;
            tab.FontFamily = dialog.Result.FontFamily;
        }

        await _lspManager.ResetAsync();
        StatusText = "Preferences saved";
    }

    [RelayCommand]
    private Task CheckForUpdates() => CheckForUpdatesAsync(interactive: true);

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        // MSIX / Store installs are updated by the Store; the package directory is immutable.
        if (PackageIdentity.IsPackaged())
        {
            if (interactive)
                StatusText = "This install is updated by the Microsoft Store.";
            return;
        }

        if (!interactive)
        {
            if (!ShouldRunStartupUpdateCheck())
                return;
        }

        MarkUpdateCheckAttempt();
        StatusText = "Checking for updates…";

        UpdateCheckResult result;
        try
        {
            result = await _updateService.CheckLatestAsync();
        }
        catch (Exception ex)
        {
            if (interactive)
                StatusText = $"Update check failed: {ex.Message}";
            return;
        }

        if (!result.Success)
        {
            if (interactive)
                StatusText = $"Update check failed: {result.Error}";
            return;
        }

        if (!result.UpdateAvailable)
        {
            if (interactive)
                StatusText = $"You're up to date (v{result.CurrentVersion}).";
            return;
        }

        var owner = GetMainWindow();
        if (owner == null)
        {
            StatusText = $"Update available: v{result.LatestVersion}";
            return;
        }

        var choice = await ShowUpdateAvailableDialogAsync(owner, result);
        if (choice == UpdatePromptChoice.Later)
        {
            StatusText = $"Update available: v{result.LatestVersion}";
            return;
        }

        if (choice == UpdatePromptChoice.OpenReleasePage)
        {
            OpenReleasePage(result.ReleaseUrl);
            StatusText = $"Opened release page for v{result.LatestVersion}";
            return;
        }

        if (!result.CanApplyInPlace)
        {
            OpenReleasePage(result.ReleaseUrl);
            StatusText = "This build can't update in place — opened the release page.";
            return;
        }

        if (!await ConfirmCloseAllForExitAsync())
        {
            StatusText = "Update cancelled";
            return;
        }

        try
        {
            StatusText = $"Downloading v{result.LatestVersion}…";
            await _updateService.ApplyUpdateAsync(result);
            SaveSession();
            StatusText = "Installing update…";
            // Bypass the window Closing prompt; the helper waits for this PID.
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            StatusText = $"Update failed: {ex.Message}";
        }
    }

    private bool ShouldRunStartupUpdateCheck()
    {
        var raw = _settingsService.Current.LastUpdateCheckUtc;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last))
            return true;
        return DateTime.UtcNow - last.ToUniversalTime() >= TimeSpan.FromHours(12);
    }

    private void MarkUpdateCheckAttempt()
    {
        _settingsService.Update(s =>
            s.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o"));
    }

    private enum UpdatePromptChoice
    {
        Later,
        UpdateNow,
        OpenReleasePage
    }

    private static async Task<UpdatePromptChoice> ShowUpdateAvailableDialogAsync(Window owner, UpdateCheckResult result)
    {
        var dialog = new Window
        {
            Title = "Update available",
            Width = 520,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            MinWidth = 420,
            MinHeight = 280
        };

        var choice = UpdatePromptChoice.Later;
        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                ? "(No release notes)"
                : result.ReleaseNotes,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 140
        };

        var later = new Button { Content = "Later", Width = 100 };
        var openPage = new Button { Content = "Open release page", Width = 140 };
        var update = new Button
        {
            Content = result.CanApplyInPlace ? "Update now" : "Download…",
            Width = 110,
            IsDefault = true
        };

        later.Click += (_, _) =>
        {
            choice = UpdatePromptChoice.Later;
            dialog.Close();
        };
        openPage.Click += (_, _) =>
        {
            choice = UpdatePromptChoice.OpenReleasePage;
            dialog.Close();
        };
        update.Click += (_, _) =>
        {
            choice = result.CanApplyInPlace
                ? UpdatePromptChoice.UpdateNow
                : UpdatePromptChoice.OpenReleasePage;
            dialog.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { later, openPage, update }
        };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttons,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"X-Pad {result.LatestVersion} is available (you have {result.CurrentVersion}).",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        },
                        new TextBlock { Text = "Release notes", Opacity = 0.75 },
                        notes
                    }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return choice;
    }

    private static void OpenReleasePage(string? url)
    {
        var target = string.IsNullOrWhiteSpace(url)
            ? $"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}/releases/latest"
            : url;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Ignore browser launch failures.
        }
    }

    private static void MarkDirtyForMetaChange(DocumentViewModel tab)
    {
        tab.IsDirty = true;
        if (!tab.Title.EndsWith('*'))
            tab.Title += "*";
    }

    private void SetTheme(string theme) => ApplyTheme(theme);

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
            tab.ApplySnapshot(snapshot, tab.FilePath, _settingsService.Current.InsertSpaces);
            GetSelectedEditor()?.RefreshEditorOptions();
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
            SelectedIndex = SelectedTab == null ? -1 : Tabs.IndexOf(SelectedTab),
            Tabs = Tabs.Select(CaptureSessionTab).ToList()
        };
        _sessionService.SaveSession(state);
        SyncFileWatchers();
    }

    private static SessionTabState CaptureSessionTab(DocumentViewModel tab)
    {
        var untitled = string.IsNullOrWhiteSpace(tab.FilePath);
        var needsText = untitled || tab.IsDirty;
        return new SessionTabState
        {
            Path = tab.FilePath ?? "",
            Title = tab.Title.TrimEnd('*'),
            Text = needsText ? tab.Text : null,
            IsDirty = tab.IsDirty || untitled,
            EncodingName = tab.Encoding,
            HasBom = tab.HasBom,
            LineEnding = tab.LineEnding,
            InsertSpaces = tab.InsertSpaces,
            LanguageOverrideExtension = tab.LanguageOverrideExtension,
            Line = Math.Max(1, tab.LineNumber),
            Column = Math.Max(1, tab.ColumnNumber)
        };
    }

    private void RestoreSession()
    {
        var state = _sessionService.LoadSession();
        DocumentViewModel? selected = null;
        var index = 0;

        foreach (var entry in state.Tabs)
        {
            try
            {
                var tab = RestoreSessionTab(entry);
                if (tab == null)
                    continue;

                Tabs.Add(tab);

                if (state.SelectedIndex >= 0 && state.SelectedIndex == index)
                    selected = tab;
                else if (state.SelectedIndex < 0 &&
                         state.SelectedPath != null &&
                         !string.IsNullOrWhiteSpace(entry.Path) &&
                         string.Equals(state.SelectedPath, entry.Path, StringComparison.OrdinalIgnoreCase))
                    selected = tab;

                index++;
            }
            catch
            {
                // Skip unreadable session entries
            }
        }

        if (Tabs.Count > 0)
            SelectedTab = selected ?? Tabs[0];
    }

    private DocumentViewModel? RestoreSessionTab(SessionTabState entry)
    {
        var hasPath = !string.IsNullOrWhiteSpace(entry.Path);
        var restoreFromText = entry.Text != null || !hasPath;

        if (hasPath && entry.Text == null)
        {
            if (!File.Exists(entry.Path))
                return null;

            var snapshot = _fileService.ReadFile(entry.Path);
            var diskTab = new DocumentViewModel
            {
                WordWrap = WordWrap,
                FontSize = _settingsService.Current.FontSize,
                FontFamily = _settingsService.Current.FontFamily,
                LanguageOverrideExtension = entry.LanguageOverrideExtension,
                PendingRestoreLine = Math.Max(1, entry.Line),
                PendingRestoreColumn = Math.Max(1, entry.Column)
            };
            diskTab.ApplySnapshot(snapshot, entry.Path, _settingsService.Current.InsertSpaces);
            return diskTab;
        }

        if (!restoreFromText)
            return null;

        var lineEnding = (entry.LineEnding ?? "LF").ToUpperInvariant() switch
        {
            "CRLF" => LineEndingKind.Crlf,
            "CR" => LineEndingKind.Cr,
            _ => LineEndingKind.Lf
        };

        Encoding encoding = FileService.CreateUtf8(false);
        var encodingName = string.IsNullOrWhiteSpace(entry.EncodingName) ? "UTF-8" : entry.EncodingName;
        if (encodingName.Contains("UTF-16 BE", StringComparison.OrdinalIgnoreCase))
            encoding = new UnicodeEncoding(true, true);
        else if (encodingName.Contains("UTF-16", StringComparison.OrdinalIgnoreCase))
            encoding = new UnicodeEncoding(false, true);
        else if (encodingName.Contains("LATIN", StringComparison.OrdinalIgnoreCase) ||
                 encodingName.Contains("ISO-8859", StringComparison.OrdinalIgnoreCase))
            encoding = Encoding.Latin1;

        var title = entry.Title;
        if (!hasPath)
        {
            if (string.IsNullOrWhiteSpace(title) ||
                title.StartsWith("Untitled", StringComparison.Ordinal))
            {
                _untitledCount++;
                title = $"Untitled {_untitledCount}";
            }
        }
        else
        {
            title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(entry.Path) : title;
        }

        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize,
            FontFamily = _settingsService.Current.FontFamily,
            LanguageOverrideExtension = entry.LanguageOverrideExtension,
            PendingRestoreLine = Math.Max(1, entry.Line),
            PendingRestoreColumn = Math.Max(1, entry.Column)
        };

        tab.RestoreClosedBuffer(
            hasPath ? entry.Path : null,
            title ?? "Untitled",
            entry.Text ?? "",
            entry.IsDirty || !hasPath,
            lineEnding,
            encoding,
            entry.HasBom,
            encodingName,
            entry.InsertSpaces);

        return tab;
    }

    private void SyncFileWatchers() =>
        _fileWatcher.Sync(Tabs.Select(t => t.FilePath));

    private void OnWatchedFileChanged(object? sender, string path)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (Interlocked.CompareExchange(ref _watcherPromptGate, 1, 0) != 0)
                return;

            try
            {
                var tab = Tabs.FirstOrDefault(t =>
                    t.FilePath != null &&
                    string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (tab == null)
                    return;

                await CheckExternalChangeAsync(tab);
            }
            finally
            {
                Interlocked.Exchange(ref _watcherPromptGate, 0);
            }
        });
    }

    private void PersistRecoverySnapshots()
    {
        try
        {
            var entries = Tabs
                .Where(t => t.IsDirty || (string.IsNullOrWhiteSpace(t.FilePath) && !string.IsNullOrEmpty(t.Text)))
                .Select(tab => new RecoveryEntry
                {
                    Id = tab.RecoveryId,
                    FilePath = tab.FilePath,
                    Title = tab.Title.TrimEnd('*'),
                    Text = tab.Text,
                    WasDirty = tab.IsDirty || string.IsNullOrWhiteSpace(tab.FilePath),
                    EncodingName = tab.Encoding,
                    HasBom = tab.HasBom,
                    LineEnding = tab.LineEnding,
                    InsertSpaces = tab.InsertSpaces,
                    LanguageOverrideExtension = tab.LanguageOverrideExtension,
                    Line = Math.Max(1, tab.LineNumber),
                    Column = Math.Max(1, tab.ColumnNumber)
                })
                .ToList();

            _recoveryService.ReplaceAll(entries);
        }
        catch
        {
            // Recovery must never break editing.
        }
    }

    private void ScheduleRecoveryPrompt()
    {
        if (_recoveryPromptScheduled || !_recoveryService.HasPendingRecovery())
            return;

        _recoveryPromptScheduled = true;
        Dispatcher.UIThread.Post(async () => await OfferRecoveryAsync(), DispatcherPriority.Background);
    }

    private async Task OfferRecoveryAsync()
    {
        var entries = _recoveryService.ListEntries();
        if (entries.Count == 0)
            return;

        var owner = GetMainWindow();
        if (owner == null)
            return;

        var prompt = new Window
        {
            Title = "Recover Unsaved Buffers",
            Width = 460,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var restore = new Button { Content = "Restore", Width = 90, IsDefault = true };
        var discard = new Button { Content = "Discard", Width = 90, IsCancel = true };
        var tcs = new TaskCompletionSource<bool>();
        restore.Click += (_, _) => { tcs.TrySetResult(true); prompt.Close(); };
        discard.Click += (_, _) => { tcs.TrySetResult(false); prompt.Close(); };
        prompt.Closed += (_, _) => tcs.TrySetResult(false);
        prompt.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Recover {entries.Count} unsaved buffer(s) from the previous session?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { discard, restore }
                }
            }
        };
        _ = prompt.ShowDialog(owner);
        if (!await tcs.Task)
        {
            _recoveryService.ClearAll();
            return;
        }

        foreach (var entry in entries)
            RestoreRecoveryEntry(entry);

        _recoveryService.ClearAll();
        StatusText = $"Restored {entries.Count} buffer(s)";
    }

    private void RestoreRecoveryEntry(RecoveryEntry entry)
    {
        var lineEnding = entry.LineEnding.ToUpperInvariant() switch
        {
            "CRLF" => LineEndingKind.Crlf,
            "CR" => LineEndingKind.Cr,
            _ => LineEndingKind.Lf
        };

        Encoding encoding = FileService.CreateUtf8(false);
        var hasBom = entry.HasBom;
        if (entry.EncodingName.Contains("UTF-16 BE", StringComparison.OrdinalIgnoreCase))
            encoding = new UnicodeEncoding(true, true);
        else if (entry.EncodingName.Contains("UTF-16", StringComparison.OrdinalIgnoreCase))
            encoding = new UnicodeEncoding(false, true);
        else if (entry.EncodingName.Contains("LATIN", StringComparison.OrdinalIgnoreCase) ||
                 entry.EncodingName.Contains("ISO-8859", StringComparison.OrdinalIgnoreCase))
            encoding = Encoding.Latin1;

        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize,
            FontFamily = _settingsService.Current.FontFamily,
            RecoveryId = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
            LanguageOverrideExtension = entry.LanguageOverrideExtension,
            PendingRestoreLine = Math.Max(1, entry.Line),
            PendingRestoreColumn = Math.Max(1, entry.Column)
        };

        tab.RestoreClosedBuffer(
            entry.FilePath,
            entry.Title,
            entry.Text,
            entry.WasDirty,
            lineEnding,
            encoding,
            hasBom,
            string.IsNullOrWhiteSpace(entry.EncodingName) ? "UTF-8" : entry.EncodingName,
            entry.InsertSpaces);

        Tabs.Add(tab);
        SelectedTab = tab;
        SyncFileWatchers();
    }

    public void AttachDock(DockControl dockControl)
    {
        _dockControl = dockControl;
        _dockFactory = dockControl.Factory;
        if (_dockFactory == null)
            return;

        ExplorerTool.FileTree = FileTree;
        _dockFactory.HideToolsOnClose = true;
        if (_dockFactory.ContextLocator != null)
        {
            _dockFactory.ContextLocator["Explorer"] = () => ExplorerTool;
            _dockFactory.ContextLocator["Terminal"] = () => TerminalTool;
        }

        _dockFactory.DockableClosing += OnDockableClosing;
        _dockFactory.DockableClosed += OnDockableClosed;
        _dockFactory.ActiveDockableChanged += OnActiveDockableChanged;

        TryApplySavedLayout(dockControl);
        EnsureToolsRegistered();
        BindDocumentDockItemsSource();
        ApplyPanelVisibilityFromState();
        EnsureDocumentsBound();
        WireDocumentDockCreate();
    }

    private void WireDocumentDockCreate()
    {
        if (_dockControl?.Layout is not IDockable root)
            return;

        foreach (var docDock in EnumerateDockables(root).OfType<DocumentDock>())
        {
            if (docDock.Id != "Documents")
                continue;

            docDock.CanCreateDocument = true;
            docDock.CreateDocument = NewTabCommand;
        }
    }

    public void SaveDockLayout()
    {
        if (_dockControl?.Layout is IDockable layout)
            _dockLayout.TrySave(layout);
    }

    private void TryApplySavedLayout(DockControl dockControl)
    {
        if (_dockLayout.TryLoadRoot() is not IRootDock loaded)
            return;

        try
        {
            DockLayoutSanitizer.StripItemsSourceDocuments(loaded);
            dockControl.Layout = loaded;
        }
        catch
        {
            _dockLayout.Delete();
        }
    }

    private void BindDocumentDockItemsSource()
    {
        // GetDockable only consults DockableLocator, which stays empty for the
        // XAML layout. Walk the live tree so the document dock is actually bound.
        EnsureDocumentsBound();
    }

    private EditorDocumentGenerator? _editorDocuments;

    private void EnsureDocumentsBound()
    {
        if (_dockControl?.Layout is not IDockable root)
            return;

        _editorDocuments ??= new EditorDocumentGenerator(_settingsService, _lspManager);
        foreach (var docDock in EnumerateDockables(root).OfType<DocumentDock>())
        {
            if (docDock.Id != "Documents")
                continue;

            var before = docDock.VisibleDockables?.Count ?? 0;
            var sameSource = ReferenceEquals(docDock.ItemsSource, Tabs);
            docDock.ItemContainerGenerator = _editorDocuments;
            if (!sameSource || (before == 0 && Tabs.Count > 0))
            {
                docDock.ItemsSource = null;
                docDock.ItemsSource = Tabs;
            }
        }
    }

    private void EnsureTool(Tool tool, string dockId, bool visible)
    {
        var dock = FindInLayout<IDock>(dockId);
        var visibleHere = dock?.VisibleDockables?.Contains(tool) == true;
        var hidden = _dockFactory?.FindRoot(tool, _ => true)?.HiddenDockables?.Contains(tool) == true;
        if (_dockFactory == null || dock == null)
            return;

        if (visible)
        {
            if (hidden)
                _dockFactory.RestoreDockable(tool);
            else if (!visibleHere)
                _dockFactory.AddDockable(dock, tool);
        }
        else if (visibleHere)
        {
            _dockFactory.HideDockable(tool);
        }
    }

    private void EnsureToolsRegistered()
    {
        if (_dockFactory == null)
            return;

        RegisterToolInDock("ExplorerToolDock", ExplorerTool);
        RegisterToolInDock("TerminalToolDock", TerminalTool);
    }

    private void RegisterToolInDock(string dockId, Tool tool)
    {
        var dock = FindInLayout<IDock>(dockId);
        if (dock == null)
            return;

        var existing = FindInLayout<IDockable>(tool.Id);
        if (existing != null && !ReferenceEquals(existing, tool))
            _dockFactory!.RemoveDockable(existing, collapse: false);

        if (dock.VisibleDockables?.Contains(tool) != true)
            _dockFactory!.AddDockable(dock, tool);
    }

    private T? FindInLayout<T>(string id) where T : class, IDockable
    {
        if (_dockControl?.Layout is not IDockable root || string.IsNullOrEmpty(id))
            return null;

        return EnumerateDockables(root).OfType<T>().FirstOrDefault(d => d.Id == id);
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable node)
    {
        yield return node;
        if (node is not IDock dock || dock.VisibleDockables == null)
            yield break;

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var nested in EnumerateDockables(child))
                yield return nested;
        }
    }

    private void ApplyPanelVisibilityFromState()
    {
        if (_dockFactory == null)
            return;

        _syncingPanelVisibility = true;
        try
        {
            EnsureTool(ExplorerTool, "ExplorerToolDock", IsExplorerVisible);
            EnsureTool(TerminalTool, "TerminalToolDock", IsTerminalVisible);
        }
        finally
        {
            _syncingPanelVisibility = false;
        }
    }

    private void OnDockableClosing(object? sender, DockableClosingEventArgs e)
    {
        if (e.Dockable is not IDocument doc || doc.Context is not DocumentViewModel tab)
            return;

        e.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = CloseTabCoreAsync(tab));
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable?.Id == ExplorerTool.Id)
        {
            _syncingPanelVisibility = true;
            try
            {
                IsExplorerVisible = false;
            }
            finally
            {
                _syncingPanelVisibility = false;
            }

            return;
        }

        if (e.Dockable?.Id == TerminalTool.Id)
        {
            _syncingPanelVisibility = true;
            try
            {
                IsTerminalVisible = false;
            }
            finally
            {
                _syncingPanelVisibility = false;
            }
        }
    }

    private void OnActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs e)
    {
        if (e.Dockable is IDocument doc && doc.Context is DocumentViewModel tab &&
            !ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = tab;
        }
    }
}
