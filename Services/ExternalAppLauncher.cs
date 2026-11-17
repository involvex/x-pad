using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace x_pad.Services;

public enum ExternalAppKind
{
    VsCode,
    Cursor,
    WindowsTerminal
}

public static class ExternalAppLauncher
{
    public static bool TryOpen(ExternalAppKind kind, string? filePath, out string error)
    {
        error = "";
        try
        {
            return kind switch
            {
                ExternalAppKind.VsCode => LaunchEditor("code", filePath, out error),
                ExternalAppKind.Cursor => LaunchEditor("cursor", filePath, out error),
                ExternalAppKind.WindowsTerminal => LaunchWindowsTerminal(filePath, out error),
                _ => Fail(out error, "Unknown app")
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string? ResolveCommand(string commandName, IEnumerable<string>? pathExtra = null)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(pathExtra ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var names = OperatingSystem.IsWindows()
            ? new[] { commandName + ".cmd", commandName + ".exe", commandName + ".bat", commandName }
            : new[] { commandName };

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                var full = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(full))
                    return full;
            }
        }

        return null;
    }

    private static bool LaunchEditor(string command, string? filePath, out string error)
    {
        var extras = CommonEditorDirs();
        var resolved = ResolveCommand(command, extras);
        if (resolved == null)
        {
            error = $"{command} not found on PATH";
            return false;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            error = "No file path for this tab";
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = resolved,
            Arguments = Quote(filePath),
            UseShellExecute = false,
            CreateNoWindow = true
        });
        error = "";
        return true;
    }

    private static bool LaunchWindowsTerminal(string? filePath, out string error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "Windows Terminal is only available on Windows";
            return false;
        }

        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            cwd = Path.GetDirectoryName(filePath) ?? cwd;
        else if (!string.IsNullOrWhiteSpace(filePath) && Directory.Exists(filePath))
            cwd = filePath;
        else
            cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var wt = ResolveCommand("wt", CommonTerminalDirs());
        if (wt != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = wt,
                Arguments = $"-d {Quote(cwd)}",
                UseShellExecute = false
            });
            error = "";
            return true;
        }

        // Fallback: open folder in Explorer if wt missing
        Process.Start(new ProcessStartInfo
        {
            FileName = cwd,
            UseShellExecute = true
        });
        error = "Windows Terminal not found; opened folder instead";
        return true;
    }

    private static IEnumerable<string> CommonEditorDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(local, "Programs", "Microsoft VS Code", "bin");
        yield return Path.Combine(local, "Programs", "cursor", "resources", "app", "bin");
        yield return Path.Combine(pf, "Microsoft VS Code", "bin");
        yield return Path.Combine(local, "Programs", "Cursor", "resources", "app", "bin");
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> CommonTerminalDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "WindowsApps");
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static bool Fail(out string error, string message)
    {
        error = message;
        return false;
    }
}
