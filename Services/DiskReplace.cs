using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using x_pad.Models;

namespace x_pad.Services;

public sealed class DiskReplaceResult
{
    public required string Path { get; init; }
    public int Count { get; init; }
    /// <summary>When set, caller should apply this text to an open tab instead of writing disk.</summary>
    public string? OpenTabText { get; init; }
    public bool WroteToDisk { get; init; }
}

/// <summary>Apply search replacements on disk or to an open buffer.</summary>
public static class DiskReplace
{
    public static DiskReplaceResult Apply(
        string path,
        IReadOnlyList<TabSearchMatch> matches,
        string replacement,
        bool useRegex,
        string? pattern,
        FileService fileService,
        string? openTabText,
        bool openTabIsDirty)
    {
        if (matches.Count == 0)
            return new DiskReplaceResult { Path = path, Count = 0 };

        if (openTabText != null && openTabIsDirty)
        {
            var (text, count) = TabSearch.ReplaceAll(
                openTabText,
                matches,
                replacement,
                useRegex,
                pattern,
                useRegex && !string.IsNullOrEmpty(pattern)
                    ? RegexOptions.CultureInvariant
                    : RegexOptions.None);
            return new DiskReplaceResult
            {
                Path = path,
                Count = count,
                OpenTabText = text,
                WroteToDisk = false
            };
        }

        if (openTabText != null && !openTabIsDirty)
        {
            var (text, count) = TabSearch.ReplaceAll(
                openTabText,
                matches,
                replacement,
                useRegex,
                pattern);
            return new DiskReplaceResult
            {
                Path = path,
                Count = count,
                OpenTabText = text,
                WroteToDisk = false
            };
        }

        var snapshot = fileService.ReadFile(path);
        var (newText, replaced) = TabSearch.ReplaceAll(
            snapshot.Text,
            matches,
            replacement,
            useRegex,
            pattern);

        if (replaced == 0 || string.Equals(snapshot.Text, newText, StringComparison.Ordinal))
            return new DiskReplaceResult { Path = path, Count = 0 };

        fileService.WriteFileAsync(
                path,
                newText,
                snapshot.Encoding,
                snapshot.HasBom,
                snapshot.LineEnding)
            .GetAwaiter()
            .GetResult();

        return new DiskReplaceResult
        {
            Path = path,
            Count = replaced,
            WroteToDisk = true
        };
    }
}
