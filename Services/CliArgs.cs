using System;
using System.Collections.Generic;
using System.IO;

namespace x_pad.Services;

/// <summary>
/// Parses startup / single-instance arguments into resolvable file paths.
/// Supports Notepad-style open-or-create for missing paths.
/// </summary>
public static class CliArgs
{
    public static bool IsHelpRequest(IEnumerable<string>? args)
    {
        if (args == null)
            return false;

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var arg = raw.Trim().Trim('"');
            if (arg is "-h" or "-?" or "/?" or "/h" or "/H" or "/help" or "/HELP" or
                "--help" or "--Help" or "-help")
                return true;
        }

        return false;
    }

    public static string GetHelpText() =>
        """
        X-Pad - lightweight Notepad++/Atom-style editor

        Usage:
          xpad [options] [file|folder ...]

        Options:
          -h, --help, /?     Show this help and exit

        Behavior:
          - Existing files open in tabs
          - Existing folders open as a workspace (file tree)
          - Missing files open as empty buffers (Save creates them)
          - A second launch hands files to the running instance
        """;

    /// <summary>Existing directories from args (workspace roots). Does not include files.</summary>
    public static IReadOnlyList<string> ResolveWorkspacePaths(IEnumerable<string>? args)
    {
        var result = new List<string>();
        if (args == null)
            return result;

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var arg = raw.Trim().Trim('"');
            if (IsFlag(arg))
                continue;

            try
            {
                var full = Path.GetFullPath(arg);
                if (Directory.Exists(full))
                    result.Add(full);
            }
            catch
            {
                // Skip invalid paths
            }
        }

        return result;
    }

    public static IReadOnlyList<string> ResolveFilePaths(IEnumerable<string>? args)
    {
        var result = new List<string>();
        if (args == null)
            return result;

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var arg = raw.Trim().Trim('"');
            if (IsFlag(arg))
                continue;

            try
            {
                var full = Path.GetFullPath(arg);
                // Directories are workspaces, not file tabs.
                if (Directory.Exists(full))
                    continue;
                // Accept existing files, or any path that looks like a file (has a name).
                if (File.Exists(full) || LooksLikeFilePath(full))
                    result.Add(full);
            }
            catch
            {
                // Skip invalid paths
            }
        }

        return result;
    }

    private static bool IsFlag(string arg)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
            return true;

        // Windows: "/?" help, "-h", but not "C:\file.txt" or "/folder/file" on Unix.
        if (arg.Length >= 2 && arg[0] is '-' or '/')
        {
            // Drive letter path: C:\...
            if (arg.Length >= 3 && char.IsLetter(arg[0]) && arg[1] == ':')
                return false;

            // Absolute unix path
            if (arg[0] == '/' && arg.Length > 1 && arg[1] != '?' && !arg.StartsWith("/-", StringComparison.Ordinal))
            {
                // "/something" may be a path; treat short option-like tokens as flags.
                if (arg is "/?" or "/h" or "/help" or "/H" or "/HELP")
                    return true;
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool LooksLikeFilePath(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Reject bare directory paths that already exist as directories.
        if (Directory.Exists(fullPath))
            return false;

        var dir = Path.GetDirectoryName(fullPath);
        return string.IsNullOrEmpty(dir) || Directory.Exists(dir) || !Path.IsPathRooted(fullPath);
    }
}
