using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        int maxHits = DefaultMaxHits) =>
        Search(root, pattern, options, excludeNames, maxHits, CancellationToken.None);

    public static Task<DiskSearchResult> SearchAsync(
        string root,
        string pattern,
        TabSearchOptions options,
        IEnumerable<string>? excludeNames = null,
        int maxHits = DefaultMaxHits,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Search(root, pattern, options, excludeNames, maxHits, cancellationToken), cancellationToken);

    public static DiskSearchResult Search(
        string root,
        string pattern,
        TabSearchOptions options,
        IEnumerable<string>? excludeNames,
        int maxHits,
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();

            if (hits.Count >= cap)
            {
                truncated = true;
                break;
            }

            if (!TryReadTextFile(path, out var text))
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

    /// <summary>
    /// One read: skip large/binary files, decode with <see cref="FileService.Decode"/>.
    /// </summary>
    public static bool TryReadTextFile(string path, out string text)
    {
        text = "";
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || LargeFilePolicy.IsLarge(info.Length))
                return false;

            var bytes = File.ReadAllBytes(path);
            if (LooksBinary(bytes))
                return false;

            text = FileService.Decode(bytes).Text;
            return true;
        }
        catch
        {
            return false;
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
            return LooksBinary(buffer.AsSpan(0, read));
        }
        catch
        {
            return true;
        }
    }

    public static bool LooksBinary(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
            return false;

        var utf16Bom = buffer.Length >= 2 &&
                       ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
                        (buffer[0] == 0xFE && buffer[1] == 0xFF));
        if (utf16Bom)
            return false; // UTF-16 text is full of null high bytes

        var start = 0;
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            start = 3; // UTF-8 BOM

        var end = Math.Min(buffer.Length, 8192);
        for (var i = start; i < end; i++)
        {
            if (buffer[i] == 0)
                return true;
        }

        return false;
    }
}
