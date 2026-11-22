using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace x_pad.Services;

public sealed class TabSearchOptions
{
    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }
    public bool UseRegex { get; init; }
    public int MaxHits { get; init; } = 500;
}

public readonly record struct TabSearchMatch(int Offset, int Length, int Line, int Column, string Preview);

public sealed class TabSearchResult
{
    public required IReadOnlyList<TabSearchMatch> Matches { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>Pure text search across buffers for Find in Open Tabs.</summary>
public static class TabSearch
{
    public static TabSearchResult Search(string text, string pattern, TabSearchOptions options)
    {
        if (string.IsNullOrEmpty(pattern) || text.Length == 0)
            return new TabSearchResult { Matches = [], Truncated = false };

        var max = Math.Max(1, options.MaxHits);
        var matches = new List<TabSearchMatch>(Math.Min(64, max));
        var lineStarts = BuildLineStarts(text);
        var truncated = false;

        try
        {
            if (options.UseRegex)
                SearchRegex(text, pattern, options, lineStarts, matches, max, ref truncated);
            else
                SearchLiteral(text, pattern, options, lineStarts, matches, max, ref truncated);
        }
        catch (ArgumentException)
        {
            return new TabSearchResult { Matches = [], Truncated = false };
        }

        return new TabSearchResult { Matches = matches, Truncated = truncated };
    }

    /// <summary>
    /// Applies replacements from last offset to first so earlier offsets stay valid.
    /// Returns the number of replacements applied and the resulting text.
    /// </summary>
    public static (string Text, int Count) ReplaceAll(
        string text,
        IReadOnlyList<TabSearchMatch> matches,
        string replacement,
        bool useRegex,
        string? pattern = null,
        RegexOptions regexOptions = RegexOptions.None)
    {
        if (matches.Count == 0)
            return (text, 0);

        var ordered = new List<TabSearchMatch>(matches);
        ordered.Sort((a, b) => b.Offset.CompareTo(a.Offset));

        var result = text;
        var count = 0;
        Regex? regex = null;
        if (useRegex && !string.IsNullOrEmpty(pattern))
            regex = new Regex(pattern, regexOptions | RegexOptions.CultureInvariant);

        foreach (var match in ordered)
        {
            if (match.Offset < 0 || match.Offset + match.Length > result.Length)
                continue;

            var replacementText = replacement;
            if (regex != null)
            {
                var slice = result.Substring(match.Offset, match.Length);
                replacementText = regex.Replace(slice, replacement, 1);
            }

            result = result.Remove(match.Offset, match.Length).Insert(match.Offset, replacementText);
            count++;
        }

        return (result, count);
    }

    public static string ReplaceOne(
        string text,
        TabSearchMatch match,
        string replacement,
        bool useRegex,
        string? pattern = null,
        RegexOptions regexOptions = RegexOptions.None)
    {
        if (match.Offset < 0 || match.Offset + match.Length > text.Length)
            return text;

        var replacementText = replacement;
        if (useRegex && !string.IsNullOrEmpty(pattern))
        {
            var regex = new Regex(pattern, regexOptions | RegexOptions.CultureInvariant);
            var slice = text.Substring(match.Offset, match.Length);
            replacementText = regex.Replace(slice, replacement, 1);
        }

        return text.Remove(match.Offset, match.Length).Insert(match.Offset, replacementText);
    }

    private static void SearchLiteral(
        string text,
        string pattern,
        TabSearchOptions options,
        List<int> lineStarts,
        List<TabSearchMatch> matches,
        int max,
        ref bool truncated)
    {
        var comparison = options.MatchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var from = 0;
        while (from < text.Length)
        {
            var index = text.IndexOf(pattern, from, comparison);
            if (index < 0)
                break;

            if (!options.WholeWord || IsWholeWord(text, index, pattern.Length))
            {
                if (matches.Count >= max)
                {
                    truncated = true;
                    return;
                }

                matches.Add(CreateMatch(text, lineStarts, index, pattern.Length));
            }

            from = index + Math.Max(1, pattern.Length);
        }
    }

    private static void SearchRegex(
        string text,
        string pattern,
        TabSearchOptions options,
        List<int> lineStarts,
        List<TabSearchMatch> matches,
        int max,
        ref bool truncated)
    {
        var flags = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
            flags |= RegexOptions.IgnoreCase;

        var effective = options.WholeWord ? $@"\b(?:{pattern})\b" : pattern;
        var regex = new Regex(effective, flags);

        foreach (Match match in regex.Matches(text))
        {
            if (!match.Success || match.Length == 0)
                continue;

            if (matches.Count >= max)
            {
                truncated = true;
                return;
            }

            matches.Add(CreateMatch(text, lineStarts, match.Index, match.Length));
        }
    }

    private static TabSearchMatch CreateMatch(string text, List<int> lineStarts, int offset, int length)
    {
        var (line, column) = OffsetToLineColumn(lineStarts, offset);
        return new TabSearchMatch(offset, length, line, column, GetLineText(text, lineStarts, line).Trim());
    }

    private static bool IsWholeWord(string text, int index, int length)
    {
        var beforeOk = index == 0 || !IsWordChar(text[index - 1]);
        var afterOk = index + length >= text.Length || !IsWordChar(text[index + length]);
        return beforeOk && afterOk;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    internal static List<int> BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            starts.Add(i + 1);
        }
        return starts;
    }

    internal static (int Line, int Column) OffsetToLineColumn(List<int> lineStarts, int offset)
    {
        var line = lineStarts.Count;
        for (var i = 1; i < lineStarts.Count; i++)
        {
            if (lineStarts[i] > offset)
            {
                line = i;
                break;
            }
        }

        var start = lineStarts[line - 1];
        return (line, offset - start + 1);
    }

    internal static string GetLineText(string text, List<int> lineStarts, int line)
    {
        var start = lineStarts[line - 1];
        var end = line < lineStarts.Count ? lineStarts[line] : text.Length;
        if (end > start && text[end - 1] == '\n')
            end--;
        if (end > start && text[end - 1] == '\r')
            end--;
        return text[start..Math.Max(start, end)];
    }
}
