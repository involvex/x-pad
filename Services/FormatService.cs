using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace x_pad.Services;

public sealed class FormatResult
{
    public required bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? Error { get; init; }
    public string Stderr { get; init; } = "";
}

/// <summary>Runs a user-configured external formatter against a file on disk.</summary>
public sealed class FormatService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public Task<FormatResult> FormatFileAsync(
        string commandTemplate,
        string filePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var expanded = FormatCommandHelper.ExpandCommand(commandTemplate, filePath);
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath))
                               ?? Directory.GetCurrentDirectory();
        return RunExpandedAsync(expanded, workingDirectory, timeout ?? DefaultTimeout, cancellationToken);
    }

    private static async Task<FormatResult> RunExpandedAsync(
        string expandedCommand,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /c " + expandedCommand,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c " + FormatCommandHelper.Quote(expandedCommand),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };

            if (!process.Start())
            {
                return new FormatResult
                {
                    Success = false,
                    Error = "Failed to start formatter process"
                };
            }

            process.BeginErrorReadLine();
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new FormatResult
                {
                    Success = false,
                    Error = $"Formatter timed out after {(int)timeout.TotalSeconds}s",
                    Stderr = stderr.ToString().Trim()
                };
            }

            var errText = stderr.ToString().Trim();
            if (process.ExitCode != 0)
            {
                return new FormatResult
                {
                    Success = false,
                    ExitCode = process.ExitCode,
                    Error = string.IsNullOrEmpty(errText)
                        ? $"Formatter exited with code {process.ExitCode}"
                        : errText,
                    Stderr = errText
                };
            }

            return new FormatResult
            {
                Success = true,
                ExitCode = 0,
                Stderr = errText
            };
        }
        catch (Exception ex)
        {
            return new FormatResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort
        }
    }
}
