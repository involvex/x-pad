using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace x_pad.Services;

public sealed class DiskSearchHit
{
    public required string Path { get; init; }
    public required TabSearchMatch Match { get; init; }
}

public sealed class DiskSearchResult
{
    public required IReadOnlyList<DiskSearchHit> Hits { get; init; }
    public bool Truncated { get; init; }
    public int FilesScanned { get; init; }
    public int FilesSkipped { get; init; }
}

/// <summary>Recursive folder search using <see cref="TabSearch"/>.</summary>
public static class DiskSearch
{
    public const int DefaultMaxHits = 2000;

    public static DiskSearchResult Search(
        string root,
        string pattern,
        TabSearchOptions options,
        IEnumerable<string>? excludeNames = null,
        int maxHits = DefaultMaxHits)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || string.IsNullOrEmpty(pattern))
            return new DiskSearchResult { Hits = [], Truncated = false };

        var excludes = new HashSet<string>(
            excludeNames ?? WorkspaceService.DefaultExcludeNames,
            StringComparer.OrdinalIgnoreCase);
        var hits = new List<DiskSearchHit>();
        var truncated = false;
        var scanned = 0;
        var skipped = 0;
        var cap = Math.Max(1, maxHits);

        foreach (var path in EnumerateFiles(root, excludes))
        {
            if (hits.Count >= cap)
            {
                truncated = true;
                break;
            }

            if (ShouldSkipFile(path))
            {
                skipped++;
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                skipped++;
                continue;
            }

            scanned++;
            var remaining = cap - hits.Count;
            var result = TabSearch.Search(text, pattern, new TabSearchOptions
            {
                MatchCase = options.MatchCase,
                WholeWord = options.WholeWord,
                UseRegex = options.UseRegex,
                MaxHits = remaining
            });
            truncated |= result.Truncated;

            foreach (var match in result.Matches)
            {
                hits.Add(new DiskSearchHit { Path = path, Match = match });
                if (hits.Count >= cap)
                {
                    truncated = true;
                    break;
                }
            }
        }

        return new DiskSearchResult
        {
            Hits = hits,
            Truncated = truncated,
            FilesScanned = scanned,
            FilesSkipped = skipped
        };
    }

    public static IEnumerable<string> EnumerateFiles(string root, ISet<string> excludeNames)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var child in children.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(child);
                if (excludeNames.Contains(name))
                    continue;
                stack.Push(child);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }

    public static bool ShouldSkipFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return true;
            if (LargeFilePolicy.IsLarge(info.Length))
                return true;
            return LooksBinary(path);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Null-byte in the first 8 KB (after optional UTF-16 BOM) ⇒ binary.</summary>
    public static bool LooksBinary(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[8192];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return false;

            var utf16Bom = read >= 2 &&
                           ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
                            (buffer[0] == 0xFE && buffer[1] == 0xFF));
            if (utf16Bom)
                return false; // UTF-16 text is full of null high bytes

            var start = 0;
            if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                start = 3; // UTF-8 BOM

            for (var i = start; i < read; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }
}
