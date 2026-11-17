using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using x_pad.Services;

namespace x_pad;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLog.Write(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };

        try
        {
            if (CliArgs.IsHelpRequest(args))
            {
                EnsureConsole();
                Console.WriteLine(CliArgs.GetHelpText());
                return;
            }

            using var singleInstance = new SingleInstanceService();
            if (!singleInstance.IsPrimary)
            {
                if (singleInstance.TrySendToPrimary(args))
                    return;

                // Primary is gone but mutex still looked taken — start anyway.
            }

            App.SingleInstance = singleInstance.IsPrimary ? singleInstance : null;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ =>
            {
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    CrashLog.Write(e.Exception);
                    // Keep the process alive for daily-driver use; details are in crash.log.
                    e.Handled = true;
                };
            });

    private static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // WinExe has no console; attach to the parent shell when launched from a terminal.
        if (!AttachConsole(-1))
            AllocConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
}
