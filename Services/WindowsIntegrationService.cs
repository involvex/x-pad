using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace x_pad.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsIntegrationService
{
    public string? GetCurrentExePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return path;

        path = Process.GetCurrentProcess().MainModule?.FileName;
        return File.Exists(path) ? path : null;
    }

    public string InstallSendToShortcut()
    {
        var exe = GetCurrentExePath()
            ?? throw new InvalidOperationException("Could not resolve xpad.exe path.");

        var sendTo = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "SendTo");
        Directory.CreateDirectory(sendTo);
        var shortcut = Path.Combine(sendTo, "X-Pad.lnk");

        var ps = new StringBuilder();
        ps.Append("$s=(New-Object -ComObject WScript.Shell).CreateShortcut('")
            .Append(EscapePs(shortcut))
            .Append("');$s.TargetPath='")
            .Append(EscapePs(exe))
            .Append("';$s.WorkingDirectory='")
            .Append(EscapePs(Path.GetDirectoryName(exe) ?? ""))
            .Append("';$s.Description='Open with X-Pad';$s.Save()");

        RunPowerShell(ps.ToString());
        return shortcut;
    }

    public void RegisterOpenWithTxt() => RegisterOpenWith(OpenWithExtensions.Default);

    public int RegisterOpenWith(params string[] extensions)
    {
        var exe = GetCurrentExePath()
            ?? throw new InvalidOperationException("Could not resolve xpad.exe path.");

        // Per-user Open With registration — does not replace the default notepad handler.
        using (var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\xpad.exe"))
        {
            app?.SetValue("FriendlyAppName", "X-Pad");
            using var supported = app?.CreateSubKey("SupportedTypes");
            if (supported != null)
            {
                foreach (var ext in NormalizeExtensions(extensions))
                    supported.SetValue(ext, "", RegistryValueKind.String);
            }
        }

        using (var command = Registry.CurrentUser.CreateSubKey(
                   @"Software\Classes\Applications\xpad.exe\shell\open\command"))
        {
            command?.SetValue("", $"\"{exe}\" \"%1\"");
        }

        var count = 0;
        foreach (var ext in NormalizeExtensions(extensions))
        {
            using (var openWith = Registry.CurrentUser.CreateSubKey(
                       $@"Software\Classes\{ext}\OpenWithList\xpad.exe"))
            {
                openWith?.SetValue("", "");
            }

            using (var openWithProgIds = Registry.CurrentUser.CreateSubKey(
                       $@"Software\Classes\{ext}\OpenWithProgids"))
            {
                openWithProgIds?.SetValue("Applications\\xpad.exe", Array.Empty<byte>(), RegistryValueKind.None);
            }

            count++;
        }

        return count;
    }

    private static string[] NormalizeExtensions(string[] extensions) =>
        extensions
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToArray();

    private static void RunPowerShell(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", command },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start PowerShell.");
        process.WaitForExit(10_000);
        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(err)
                ? $"PowerShell exited with code {process.ExitCode}"
                : err.Trim());
        }
    }

    private static string EscapePs(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
