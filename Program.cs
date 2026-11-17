using System;
using System.IO;
using Avalonia;

namespace x_pad;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            using var singleInstance = new Services.SingleInstanceService();
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
            WriteCrashLog(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XPad");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.WriteAllText(path, $"{DateTime.Now:O}\n{ex}");
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine($"Crash log: {path}");
        }
        catch
        {
            // Last-resort: ignore logging failures
        }
    }
}
