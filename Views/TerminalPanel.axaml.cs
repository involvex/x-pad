using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Iciclecreek.Terminal;

namespace x_pad.Views;

public partial class TerminalPanel : UserControl
{
    private bool _started;
    private bool _launchQueued;
    private string? _cwdHint;

    public event EventHandler? HideRequested;

    public TerminalPanel()
    {
        InitializeComponent();
        // Do not LaunchProcess on AttachedToVisualTree — the panel stays in the
        // tree while hidden, and Iciclecreek NRE's MaxScrollback before layout.
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && IsVisible)
            EnsureStarted();
    }

    public void SetWorkingDirectoryHint(string? path)
    {
        _cwdHint = path;
        UpdateTitle();
    }

    public void EnsureStarted()
    {
        if (_started || _launchQueued || Terminal == null || !IsVisible)
            return;

        _launchQueued = true;
        // Wait until the control has a real size so TerminalView viewport exists.
        Dispatcher.UIThread.Post(TryLaunchWhenReady, DispatcherPriority.Loaded);
    }

    private void TryLaunchWhenReady()
    {
        _launchQueued = false;
        if (_started || Terminal == null || !IsVisible)
            return;

        if (Terminal.Bounds.Width < 8 || Terminal.Bounds.Height < 8)
        {
            _launchQueued = true;
            Dispatcher.UIThread.Post(TryLaunchWhenReady, DispatcherPriority.Background);
            return;
        }

        try
        {
            var shell = ResolveShell();
            var cwd = ResolveStartDirectory();
            Terminal.LaunchProcess(cwd, shell);
            _started = true;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            _started = false;
            if (TitleBlock != null)
                TitleBlock.Text = "Terminal failed: " + ex.Message;
        }
    }

    private void OnNewSession(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Terminal.IsLive)
                Terminal.Kill();
        }
        catch
        {
            // ignore
        }

        _started = false;
        EnsureStarted();
    }

    private void OnKill(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Terminal.IsLive)
                Terminal.Kill();
            _started = false;
            if (TitleBlock != null)
                TitleBlock.Text = "Terminal (stopped)";
        }
        catch (Exception ex)
        {
            if (TitleBlock != null)
                TitleBlock.Text = "Kill failed: " + ex.Message;
        }
    }

    private async void OnCdHere(object? sender, RoutedEventArgs e)
    {
        var dir = ResolveStartDirectory();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;
        if (!Terminal.IsLive)
            EnsureStarted();
        try
        {
            await Terminal.SendInputAsync($"cd \"{dir}\"\r");
            UpdateTitle();
        }
        catch
        {
            // ignore
        }
    }

    private void OnHide(object? sender, RoutedEventArgs e) =>
        HideRequested?.Invoke(this, EventArgs.Empty);

    private string ResolveStartDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_cwdHint) && File.Exists(_cwdHint))
            return Path.GetDirectoryName(_cwdHint) ?? Environment.CurrentDirectory;
        if (!string.IsNullOrWhiteSpace(_cwdHint) && Directory.Exists(_cwdHint))
            return _cwdHint;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void UpdateTitle()
    {
        if (TitleBlock == null)
            return;
        var cwd = Terminal?.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(cwd))
            cwd = ResolveStartDirectory();
        TitleBlock.Text = "Terminal — " + cwd;
    }

    private static string ResolveShell()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var name in new[] { "pwsh.exe", "powershell.exe", "cmd.exe" })
            {
                var found = ExternalAppLauncherResolve(name);
                if (found != null)
                    return found;
            }

            return "cmd.exe";
        }

        return File.Exists("/bin/bash") ? "/bin/bash" : "sh";
    }

    private static string? ExternalAppLauncherResolve(string exe)
    {
        var baseName = Path.GetFileNameWithoutExtension(exe);
        return Services.ExternalAppLauncher.ResolveCommand(baseName)
               ?? Services.ExternalAppLauncher.ResolveCommand(exe);
    }
}
