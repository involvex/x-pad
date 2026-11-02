using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using x_pad.Services;

namespace x_pad.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly FileService _fileService = new();
    private readonly SessionService _sessionService = new();

    public ObservableCollection<DocumentViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private DocumentViewModel? selectedTab;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private bool wordWrap = false;

    partial void OnWordWrapChanged(bool value)
    {
        foreach (var tab in Tabs)
        {
            tab.WordWrap = value;
        }
    }

    public MainWindowViewModel(string[]? args = null)
    {
        NewTab();
        RestoreSession();
        if (args != null && args.Length > 0)
        {
            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                try
                {
                    var fullPath = Path.GetFullPath(arg);
                    if (File.Exists(fullPath))
                    {
                        _ = OpenFileFromPath(fullPath);
                    }
                }
                catch
                {
                    // Skip invalid paths
                }
            }
        }
    }

    private int _untitledCount = 0;
    
    [RelayCommand]
    private void NewTab()
    {
        _untitledCount++;
        var tab = new DocumentViewModel
        {
            WordWrap = WordWrap,
            Title = $"Untitled {_untitledCount}"
        };
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = lifetime?.MainWindow;
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = true
        });

        foreach (var file in files)
        {
            await OpenFileFromPath(file.Path.LocalPath);
        }
    }

    private async Task OpenFileFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        
        // Check if already open
        var existing = Tabs.FirstOrDefault(t => t.FilePath == path);
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        var content = await _fileService.ReadFileAsync(path);
        var tab = new DocumentViewModel
        {
            Title = Path.GetFileName(path),
            Content = content,
            FilePath = path,
            IsDirty = false,
            WordWrap = WordWrap
        };
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private async Task SaveFile()
    {
        if (SelectedTab == null) return;
        
        if (string.IsNullOrWhiteSpace(SelectedTab.FilePath))
        {
            await SaveFileAs();
            return;
        }

        await _fileService.WriteFileAsync(SelectedTab.FilePath, SelectedTab.Content);
        SelectedTab.MarkClean(SelectedTab.FilePath);
        SaveSession();
    }

    [RelayCommand]
    private async Task SaveFileAs()
    {
        if (SelectedTab == null) return;
        
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = lifetime?.MainWindow;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File As",
            SuggestedFileName = SelectedTab.FilePath != null 
                ? Path.GetFileName(SelectedTab.FilePath) 
                : "Untitled.txt"
        });

        if (file != null)
        {
            var path = file.Path.LocalPath;
            await _fileService.WriteFileAsync(path, SelectedTab.Content);
            SelectedTab.MarkClean(path);
            SaveSession();
        }
    }

    [RelayCommand]
    private void CloseTab(DocumentViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;
        
        if (tab.IsDirty)
        {
            StatusText = "Save changes before closing (use Save/Save As)";
        }
        
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        
        if (Tabs.Count == 0)
        {
            NewTab();
        }
        else if (SelectedTab == tab)
        {
            SelectedTab = Tabs[Math.Max(0, index - 1)];
        }
        
        SaveSession();
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        foreach (var tab in Tabs.ToList())
        {
            SelectedTab = tab;
            if (string.IsNullOrWhiteSpace(tab.FilePath))
            {
                await SaveFileAs();
            }
            else
            {
                await _fileService.WriteFileAsync(tab.FilePath, tab.Content);
                tab.MarkClean(tab.FilePath);
            }
        }
        SaveSession();
        StatusText = "All files saved";
    }

    [RelayCommand]
    private void CloseAll()
    {
        while (Tabs.Count > 0)
        {
            var tab = Tabs[0];
            Tabs.RemoveAt(0);
        }
        NewTab();
        SaveSession();
    }

    [RelayCommand]
    private void CloseOthers()
    {
        if (SelectedTab == null) return;
        var toKeep = SelectedTab;
        var toRemove = Tabs.Where(t => t != toKeep).ToList();
        foreach (var tab in toRemove)
        {
            Tabs.Remove(tab);
        }
        SaveSession();
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
        StatusText = "Find (Ctrl+F) - Use search panel";
    }

    [RelayCommand]
    private void Replace()
    {
        StatusText = "Replace (Ctrl+H) - Use replace panel";
    }

    [RelayCommand]
    private void GoToLine()
    {
        StatusText = "Go to Line (Ctrl+G)";
    }

    [RelayCommand]
    private void ToggleDarkMode()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            StatusText = "Theme: Dark";
        }
    }

    [RelayCommand]
    private void ToggleLightMode()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            StatusText = "Theme: Light";
        }
    }

    [RelayCommand]
    private void ToggleSystemTheme()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
            StatusText = "Theme: System";
        }
    }

    [RelayCommand]
    private void ToggleDraculaTheme()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            StatusText = "Theme: Dracula";
        }
    }

    [RelayCommand]
    private void ToggleMonokaiTheme()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            StatusText = "Theme: Monokai";
        }
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
            if (File.Exists(file))
            {
                _ = OpenFileFromPath(file);
            }
        }
        if (Tabs.Count == 0)
        {
            NewTab();
        }
    }
}
