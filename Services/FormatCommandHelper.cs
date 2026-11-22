using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace x_pad.Services;

/// <summary>Pure helpers for external format-on-save commands (testable without process IO).</summary>
public static class FormatCommandHelper
{
    public const string FileToken = "$FILE";

    /// <summary>
    /// Replaces <c>$FILE</c> with a quoted absolute path. If the token is missing,
    /// appends the quoted path as a trailing argument.
    /// </summary>
    public static string ExpandCommand(string commandTemplate, string filePath)
    {
        if (string.IsNullOrWhiteSpace(commandTemplate))
            throw new ArgumentException("Format command is empty.", nameof(commandTemplate));
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is empty.", nameof(filePath));

        var quoted = Quote(Path.GetFullPath(filePath));
        if (commandTemplate.Contains(FileToken, StringComparison.Ordinal))
            return commandTemplate.Replace(FileToken, quoted, StringComparison.Ordinal);

        return $"{commandTemplate.TrimEnd()} {quoted}";
    }

    /// <summary>
    /// Returns true when <paramref name="extensionsCsv"/> is empty/whitespace (all files),
    /// or when the file extension is listed (with or without a leading dot).
    /// </summary>
    public static bool MatchesExtension(string? filePath, string? extensionsCsv)
    {
        if (string.IsNullOrWhiteSpace(extensionsCsv))
            return true;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return false;

        foreach (var raw in SplitExtensions(extensionsCsv))
        {
            var normalized = raw.StartsWith('.') ? raw : "." + raw;
            if (string.Equals(normalized, ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<string> SplitExtensions(string? extensionsCsv)
    {
        if (string.IsNullOrWhiteSpace(extensionsCsv))
            return [];

        return extensionsCsv
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    public static string Quote(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            return path;
        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
