using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using x_pad.Services;
using x_pad.ViewModels;

namespace x_pad.Views;

public sealed class FolderFindHit
{
    public required string Path { get; init; }
    public required TabSearchMatch Match { get; init; }
    public required string Location { get; init; }
    public required string Preview { get; init; }
}

public partial class FindInFolderWindow : Window
{
    private readonly string _root;
    private readonly IReadOnlyList<string> _excludes;
    private readonly IReadOnlyList<DocumentViewModel> _openTabs;
    private readonly FileService _fileService = new();
    private List<FolderFindHit> _hits = [];
    private string _lastPattern = "";

    public FolderFindHit? JumpResult { get; private set; }
    public int ReplaceCount { get; private set; }

    /// <summary>Open-tab texts that were updated in memory (path → new text).</summary>
    public Dictionary<string, string> UpdatedOpenTabs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FindInFolderWindow() : this("", [], [])
    {
    }

    public FindInFolderWindow(
        string root,
        IEnumerable<string> excludes,
        IEnumerable<DocumentViewModel> openTabs)
    {
        _root = root;
        _excludes = excludes.ToList();
        _openTabs = openTabs.ToList();
        InitializeComponent();

        var folder = this.FindControl<TextBlock>("FolderBox");
        if (folder != null)
            folder.Text = root;

        var query = this.FindControl<TextBox>("QueryBox");
        var search = this.FindControl<Button>("SearchButton");
        var replace = this.FindControl<Button>("ReplaceButton");
        var replaceAll = this.FindControl<Button>("ReplaceAllButton");
        var list = this.FindControl<ListBox>("ResultsList");

        if (search != null)
            search.Click += (_, _) => RunSearch();
        if (replace != null)
            replace.Click += OnReplaceClick;
        if (replaceAll != null)
            replaceAll.Click += OnReplaceAllClick;
        if (query != null)
        {
            query.KeyDown += OnQueryKeyDown;
            Opened += (_, _) => Dispatcher.UIThread.Post(() => query.Focus());
        }

        if (list != null)
            list.KeyDown += OnListKeyDown;
    }

    private TabSearchOptions CurrentOptions() =>
        new()
        {
            MatchCase = this.FindControl<CheckBox>("MatchCaseBox")?.IsChecked == true,
            WholeWord = this.FindControl<CheckBox>("WholeWordBox")?.IsChecked == true,
            UseRegex = this.FindControl<CheckBox>("RegexBox")?.IsChecked == true,
            MaxHits = DiskSearch.DefaultMaxHits
        };

    private void RunSearch()
    {
        var query = this.FindControl<TextBox>("QueryBox");
        var list = this.FindControl<ListBox>("ResultsList");
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (query == null || list == null)
            return;

        var needle = query.Text ?? "";
        _lastPattern = needle;
        _hits = [];

        if (needle.Length > 0 && Directory.Exists(_root))
        {
            var result = DiskSearch.Search(_root, needle, CurrentOptions(), _excludes);
            foreach (var hit in result.Hits)
            {
                var rel = Path.GetRelativePath(_root, hit.Path);
                _hits.Add(new FolderFindHit
                {
                    Path = hit.Path,
                    Match = hit.Match,
                    Location = $"{rel}:{hit.Match.Line}",
                    Preview = hit.Match.Preview
                });
            }

            list.ItemsSource = _hits;
            if (_hits.Count > 0)
                list.SelectedIndex = 0;

            if (summary != null)
            {
                var suffix = result.Truncated ? " (truncated)" : "";
                summary.Text =
                    $"{_hits.Count} hit(s) in {result.FilesScanned} file(s){suffix}; skipped {result.FilesSkipped}";
            }
        }
        else
        {
            list.ItemsSource = _hits;
            if (summary != null)
                summary.Text = "Type a pattern and Search";
        }
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("ResultsList");
        var replaceBox = this.FindControl<TextBox>("ReplaceBox");
        if (list?.SelectedItem is not FolderFindHit hit || replaceBox == null)
            return;

        var count = ApplyReplace([hit], replaceBox.Text ?? "");
        ReplaceCount += count;
        RunSearch();
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        var replaceBox = this.FindControl<TextBox>("ReplaceBox");
        if (replaceBox == null || _hits.Count == 0)
            return;

        var count = ApplyReplace(_hits, replaceBox.Text ?? "");
        ReplaceCount += count;
        RunSearch();
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (summary != null && count > 0)
            summary.Text = $"Replaced {count} occurrence(s). {_hits.Count} hit(s) remaining.";
    }

    private int ApplyReplace(IReadOnlyList<FolderFindHit> hits, string replacement)
    {
        var options = CurrentOptions();
        var total = 0;
        foreach (var group in hits.GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase))
        {
            var path = group.Key;
            var open = _openTabs.FirstOrDefault(t =>
                t.FilePath != null &&
                string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

            var matches = group.Select(h => h.Match).ToList();
            // Re-search offsets if we already updated this tab in this session.
            var openText = open != null
                ? (UpdatedOpenTabs.TryGetValue(path, out var updated) ? updated : open.Text)
                : null;
            var dirty = open?.IsDirty == true || UpdatedOpenTabs.ContainsKey(path);

            // Fresh match offsets from current text when replacing all after prior edits.
            if (openText != null)
                matches = TabSearch.Search(openText, _lastPattern, options).Matches.ToList();
            else if (!File.Exists(path))
                continue;
            else
            {
                try
                {
                    var diskText = File.ReadAllText(path);
                    matches = TabSearch.Search(diskText, _lastPattern, options).Matches.ToList();
                }
                catch
                {
                    continue;
                }
            }

            var result = DiskReplace.Apply(
                path,
                matches,
                replacement,
                options.UseRegex,
                _lastPattern,
                _fileService,
                openText,
                dirty || open != null);

            total += result.Count;
            if (result.OpenTabText != null)
                UpdatedOpenTabs[path] = result.OpenTabText;
        }

        return total;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void Accept()
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list?.SelectedItem is not FolderFindHit hit)
            return;

        JumpResult = hit;
        Close(hit);
    }
}
