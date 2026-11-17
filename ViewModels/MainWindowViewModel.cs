using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    private int _untitledCount;

    public ObservableCollection<DocumentViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private DocumentViewModel? selectedTab;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private bool wordWrap;

    public SettingsService Settings => _settingsService;
    public LspManager Lsp => _lspManager;

    public MainWindowViewModel(string[]? args = null, SettingsService? settings = null)
    {
        _settingsService = settings ?? new SettingsService();
        _lspManager = new LspManager(_settingsService);
        _lspManager.DiagnosticsReceived += OnLspDiagnostics;
        WordWrap = _settingsService.Current.WordWrap;

        EditorThemeService.ApplyAppTheme(_settingsService.Current.Theme);

        var openedFromArgs = false;
        if (args != null)
        {
            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                try
                {
                    var fullPath = Path.GetFullPath(arg);
                    if (File.Exists(fullPath))
                    {
                        OpenFileSync(fullPath);
                        openedFromArgs = true;
                    }
                }
                catch
                {
                    // Skip invalid paths
                }
            }
        }

        if (!openedFromArgs)
            RestoreSession();

        if (Tabs.Count == 0)
            NewTab();
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

        var snapshot = _fileService.ReadFile(path);
        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize
        };
        tab.ApplySnapshot(snapshot, path);
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
    }

    public void OpenFilesFromExternal(string[] paths)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                    _ = OpenFileFromPath(path);
            }
        });
    }

    [RelayCommand]
    private void NewTab()
    {
        _untitledCount++;
        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            FontSize = _settingsService.Current.FontSize
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

    public async Task OpenFileFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var existing = Tabs.FirstOrDefault(t =>
            t.FilePath != null &&
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedTab = existing;
            StatusText = $"Already open: {Path.GetFileName(path)}";
            return;
        }

        try
        {
            var snapshot = await _fileService.ReadFileAsync(path);
            var tab = new DocumentViewModel
            {
                WordWrap = WordWrap,
                FontSize = _settingsService.Current.FontSize
            };
            tab.ApplySnapshot(snapshot, path);
            Tabs.Add(tab);
            SelectedTab = tab;

            // Drop the initial empty untitled tab if it was the only placeholder
            var emptyUntitled = Tabs.FirstOrDefault(t =>
                t != tab && t.FilePath == null && !t.IsDirty && string.IsNullOrEmpty(t.Text));
            if (emptyUntitled != null && Tabs.Count > 1)
                Tabs.Remove(emptyUntitled);

            SaveSession();
            StatusText = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
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
            await _fileService.WriteFileAsync(
                tab.FilePath,
                tab.Text,
                tab.TextEncoding,
                tab.HasBom,
                tab.LineEndingKind);
            tab.MarkClean(tab.FilePath);
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
                tab.LineEndingKind);
            tab.MarkClean(path);
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
    private async Task CloseTab(DocumentViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        if (!await ConfirmCloseAsync(tab))
            return;

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
            Tabs.Remove(tab);
        }
        SaveSession();
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
    private void Find()
    {
        GetSelectedEditor()?.OpenFind();
        StatusText = "Find";
    }

    [RelayCommand]
    private void Replace()
    {
        GetSelectedEditor()?.OpenReplace();
        StatusText = "Replace";
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

    private void SetTheme(string theme)
    {
        _settingsService.Update(s => s.Theme = theme);
        EditorThemeService.ApplyAppTheme(theme);
        StatusText = $"Theme: {theme}";
    }

    private async Task CheckExternalChangeAsync(DocumentViewModel tab)
    {
        if (string.IsNullOrWhiteSpace(tab.FilePath) || tab.LastWriteTimeUtc == null)
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

            _ = messageWindow.ShowDialog(owner);
            var shouldReload = await tcs.Task;
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
        var files = Tabs
            .Where(t => !string.IsNullOrWhiteSpace(t.FilePath))
            .Select(t => t.FilePath!)
            .ToList();
        _sessionService.SaveSession(files);
    }

    private void RestoreSession()
    {
        var files = _sessionService.LoadSession();
        foreach (var file in files)
        {
            if (!File.Exists(file))
                continue;

            try
            {
                var snapshot = _fileService.ReadFile(file);
                var tab = new DocumentViewModel
                {
                    WordWrap = WordWrap,
                    FontSize = _settingsService.Current.FontSize
                };
                tab.ApplySnapshot(snapshot, file);
                Tabs.Add(tab);
            }
            catch
            {
                // Skip unreadable session files
            }
        }

        if (Tabs.Count > 0)
            SelectedTab = Tabs[0];
    }
}
