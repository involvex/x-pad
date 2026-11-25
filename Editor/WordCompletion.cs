using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace x_pad.Editor;

/// <summary>Caches <see cref="WordCompletion.CollectWords"/> until the document version changes.</summary>
public sealed class WordListCache
{
    private int _version = int.MinValue;
    private IReadOnlyList<string> _words = [];

    public IReadOnlyList<string> GetOrCollect(string text, int version)
    {
        if (version == _version)
            return _words;

        _words = WordCompletion.CollectWords(text);
        _version = version;
        return _words;
    }

    public void Invalidate()
    {
        _version = int.MinValue;
        _words = [];
    }
}

/// <summary>Pure helpers for document word completion (testable without UI).</summary>
public static class WordCompletion
{
    private static readonly Regex WordRegex = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    public static IReadOnlyList<string> CollectWords(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        return WordRegex.Matches(text)
            .Select(m => m.Value)
            .ToList();
    }

    public static IReadOnlyList<string> RankCandidates(
        IEnumerable<string> words,
        string prefix,
        int limit = 40,
        bool allowEmptyPrefix = false)
    {
        if (limit <= 0)
            return [];

        if (string.IsNullOrEmpty(prefix))
        {
            if (!allowEmptyPrefix)
                return [];

            return words
                .Distinct(StringComparer.Ordinal)
                .OrderBy(w => w.Length)
                .ThenBy(w => w, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }

        return words
            .Where(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(w, prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w.Length)
            .ThenBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }
}
