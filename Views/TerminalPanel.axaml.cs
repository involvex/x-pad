using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    private EventHandler<RoutedEventArgs>? _loadedHandler;
    private Task? _launchTask;

    public event EventHandler? HideRequested;

    public TerminalPanel()
    {
        InitializeComponent();
        // Do not LaunchProcess on AttachedToVisualTree — the panel stays in the
        // tree while hidden, and Iciclecreek NRE's MaxScrollback before the
        // TerminalView emulator exists (OnInitialized). Keep Process="" in XAML
        // so TerminalView.OnLoaded (async void) does not auto-spawn.
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

        // Disarm library auto-start until we launch explicitly after IsLoaded.
        DisarmAutoLaunch();

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

        _loadedHandler = (_, _) =>
        {
            if (TryBeginLaunch())
                StopWaitingForLayout();
        };
        Terminal.Loaded += _loadedHandler;

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

        if (_loadedHandler != null && Terminal != null)
            Terminal.Loaded -= _loadedHandler;
        _loadedHandler = null;

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
        ObserveLaunch(LaunchAsync());
        return true;
    }

    private void ObserveLaunch(Task launch)
    {
        _launchTask = launch;
        // LaunchAsync already catches, but keep a backstop so a future edit cannot
        // leave Iciclecreek's async void / fire-and-forget path unobserved again.
        _ = launch.ContinueWith(
            static t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    CrashLog.Write(t.Exception.Flatten(), openConsole: false);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool IsEmulatorReady()
    {
        if (Terminal == null || !IsVisible)
            return false;
        if (Terminal.Bounds.Width < 8 || Terminal.Bounds.Height < 8)
            return false;

        // Do not probe MaxScrollback — that getter NREs when _terminal/Buffer is gone
        // (Iciclecreek LaunchProcess error path). Require a live, loaded view that has
        // already been arranged so Cols/Rows are real before ConPTY spawn.
        var view = Terminal.GetVisualDescendants().OfType<TerminalView>().FirstOrDefault();
        return view is { Terminal: not null, IsLoaded: true } &&
               view.ViewportLines >= 1;
    }

    private async Task LaunchAsync()
    {
        if (_started || Terminal == null || !IsVisible)
            return;

        _started = true;
        DisarmAutoLaunch();
        try
        {
            var shell = ResolveShell();
            var cwd = ResolveStartDirectory();

            // Avoid LaunchProcess(cwd, shell): it leaves Process set for the whole
            // spawn await. A dock re-template then loads a new TerminalView with
            // Process still set, OnLoaded auto-spawns (async void), the spawn/error
            // path hits MaxScrollback NRE, and that fault is unobserved.
            //
            // LaunchProcess() reads Process into a local before its first await, so
            // we clear Process immediately after starting the task.
            Terminal.StartingDirectory = cwd;
            Terminal.Process = shell;
            var launch = Terminal.LaunchProcess();
            DisarmAutoLaunch();
            await launch.ConfigureAwait(true);
            UpdateTitle();
        }
        catch (Exception ex)
        {
            _started = false;
            var root = Unwrap(ex);
            if (TitleBlock != null)
            {
                TitleBlock.Text = root is NullReferenceException
                    ? "Terminal failed to start (shell spawn error)"
                    : "Terminal failed: " + root.Message;
            }

            CrashLog.Write(ex, openConsole: false);
        }
        finally
        {
            DisarmAutoLaunch();
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        if (ex is AggregateException agg)
        {
            agg = agg.Flatten();
            if (agg.InnerExceptions.Count > 0)
                return agg.InnerExceptions[0];
        }

        return ex.InnerException ?? ex;
    }

    private void DisarmAutoLaunch()
    {
        if (Terminal == null)
            return;
        try
        {
            if (!string.IsNullOrEmpty(Terminal.Process))
                Terminal.Process = "";
        }
        catch
        {
            // ignore
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

        DisarmAutoLaunch();
        _started = false;
        EnsureStarted();
    }

    private void OnKill(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Terminal.IsLive)
                Terminal.Kill();
            DisarmAutoLaunch();
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
