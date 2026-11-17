using System;
using System.IO;
using System.Runtime.InteropServices;

namespace x_pad.Services;

public static class CrashLog
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const int AttachParentProcess = -1;

    public static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XPad",
            "crash.log");

    public static void Write(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(LogPath, $"{DateTime.Now:O}\n{ex}");
            EnsureConsole();
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine($"Crash log: {LogPath}");
        }
        catch
        {
            // Last-resort: ignore logging failures
        }
    }

    private static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!AttachConsole(AttachParentProcess))
            AllocConsole();
    }
}
