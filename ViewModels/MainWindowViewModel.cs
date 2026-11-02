using System;
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

    public MainWindowViewModel()
    {
        NewTab();
        RestoreSession();
    }

    [RelayCommand]
    private void NewTab()
    {
        var tab = new DocumentViewModel();
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
            IsDirty = false
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
            // For MVP, just close without confirmation - keep it simple
            // TODO: Add confirmation dialog
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
