using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using x_pad.Services;

namespace x_pad.Views;

public partial class TerminalPanel : UserControl
{
    private bool _started;
    private bool _launchQueued;
    private string? _cwdHint;
    private EventHandler? _layoutHandler;

    public event EventHandler? HideRequested;

    public TerminalPanel()
    {
        InitializeComponent();
        // Do not LaunchProcess on AttachedToVisualTree — the panel stays in the
        // tree while hidden, and Iciclecreek NRE's MaxScrollback before the
        // TerminalView emulator exists (OnInitialized).
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

        if (TryBeginLaunch())
            return;

        WaitForEmulatorThenLaunch();
    }

    private void WaitForEmulatorThenLaunch()
    {
        if (Terminal == null || _launchQueued)
            return;

        _launchQueued = true;

        _layoutHandler = (_, _) =>
        {
            if (TryBeginLaunch())
                StopWaitingForLayout();
        };
        Terminal.LayoutUpdated += _layoutHandler;

        // Background runs after Render, so one backstop covers a layout that
        // already happened before we subscribed.
        Dispatcher.UIThread.Post(() =>
        {
            if (TryBeginLaunch())
                StopWaitingForLayout();
        }, DispatcherPriority.Background);
    }

    private void StopWaitingForLayout()
    {
        if (_layoutHandler != null && Terminal != null)
            Terminal.LayoutUpdated -= _layoutHandler;
        _layoutHandler = null;
        _launchQueued = false;
    }

    /// <summary>
    /// Starts the shell when the emulator is ready. Returns true when waiting
    /// should stop (launched, aborted, or already started).
    /// </summary>
    private bool TryBeginLaunch()
    {
        if (_started || Terminal == null || !IsVisible)
            return true;

        if (!IsEmulatorReady())
            return false;

        StopWaitingForLayout();
        _ = LaunchAsync();
        return true;
    }

    private bool IsEmulatorReady()
    {
        if (Terminal == null || !IsVisible)
            return false;
        if (Terminal.Bounds.Width < 8 || Terminal.Bounds.Height < 8)
            return false;

        // Do not probe MaxScrollback — that getter is the NRE in the crash log.
        var view = Terminal.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();
        return view?.Terminal != null;
    }

    private async Task LaunchAsync()
    {
        if (_started || Terminal == null || !IsVisible)
            return;

        _started = true;
        try
        {
            var shell = ResolveShell();
            var cwd = ResolveStartDirectory();
            await Terminal.LaunchProcess(cwd, shell);
            UpdateTitle();
        }
        catch (Exception ex)
        {
            _started = false;
            if (TitleBlock != null)
                TitleBlock.Text = "Terminal failed: " + ex.Message;
            CrashLog.Write(ex, openConsole: false);
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
        return ExternalAppLauncher.ResolveCommand(baseName)
               ?? ExternalAppLauncher.ResolveCommand(exe);
    }
}
