using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using x_pad.ViewModels;

namespace x_pad.Services;

/// <summary>Owns the system tray icon and Recent submenu for minimize-to-tray.</summary>
public sealed class TrayService : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly TrayIcons _icons;
    private readonly NativeMenuItem _recentRoot;
    private MainWindowViewModel? _vm;
    private bool _disposed;

    public TrayService()
    {
        _recentRoot = new NativeMenuItem("Open Recent")
        {
            Menu = new NativeMenu()
        };

        var menu = new NativeMenu();
        var show = new NativeMenuItem("Show X-Pad");
        show.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(show);
        menu.Items.Add(_recentRoot);
        menu.Items.Add(new NativeMenuItemSeparator());
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://xpad/Assets/xpad.ico"))),
            ToolTipText = "X-Pad",
            IsVisible = true,
            Menu = menu,
            Command = new SimpleCommand(ShowMainWindow)
        };

        // Avalonia only shows the icon after it is attached to Application.Current.
        _icons = new TrayIcons { _icon };
        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, _icons);
    }

    public void Attach(MainWindowViewModel vm)
    {
        if (_vm != null)
            _vm.RecentFilesChanged -= OnRecentChanged;

        _vm = vm;
        _vm.RecentFilesChanged += OnRecentChanged;
        RebuildRecentMenu();
    }

    private void OnRecentChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RebuildRecentMenu);

    private void RebuildRecentMenu()
    {
        var menu = new NativeMenu();
        var paths = _vm?.RecentFiles.ToList() ?? [];
        if (paths.Count == 0)
        {
            menu.Items.Add(new NativeMenuItem("(empty)") { IsEnabled = false });
        }
        else
        {
            foreach (var path in paths)
            {
                var capture = path;
                var item = new NativeMenuItem(System.IO.Path.GetFileName(capture) + " — " + capture);
                item.Click += async (_, _) =>
                {
                    ShowMainWindow();
                    if (_vm != null)
                        await _vm.OpenFileFromPath(capture);
                };
                menu.Items.Add(item);
            }
        }

        _recentRoot.Menu = menu;
    }

    public static void ShowMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        if (desktop.MainWindow is not Window window)
            return;

        window.ShowInTaskbar = true;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static void ExitApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow?.Close();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_vm != null)
            _vm.RecentFilesChanged -= OnRecentChanged;

        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, new TrayIcons());

        _icon.IsVisible = false;
        _icon.Dispose();
    }

    private sealed class SimpleCommand(Action execute) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    }
}
