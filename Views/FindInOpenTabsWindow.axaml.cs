using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using x_pad.Services;
using x_pad.ViewModels;

namespace x_pad.Views;

public sealed class FindHit
{
    public required DocumentViewModel Tab { get; init; }
    public required int Offset { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required int MatchLength { get; init; }
    public required string Location { get; init; }
    public required string Preview { get; init; }
}

public partial class FindInOpenTabsWindow : Window
{
    private readonly IReadOnlyList<DocumentViewModel> _tabs;
    private List<FindHit> _hits = [];
    private string _lastPattern = "";
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _debounceTimer;
    private int _searchVersion;

    public FindHit? Result { get; private set; }
    public int ReplaceCount { get; private set; }

    public FindInOpenTabsWindow() : this([])
    {
    }

    public FindInOpenTabsWindow(IEnumerable<DocumentViewModel> tabs)
    {
        _tabs = tabs.ToList();
        InitializeComponent();

        var query = this.FindControl<TextBox>("QueryBox");
        var list = this.FindControl<ListBox>("ResultsList");
        if (query == null || list == null)
            return;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _ = SearchAsync();
        };

        query.TextChanged += (_, _) => ScheduleSearch();
        WireOption(this.FindControl<CheckBox>("MatchCaseBox"));
        WireOption(this.FindControl<CheckBox>("WholeWordBox"));
        WireOption(this.FindControl<CheckBox>("RegexBox"));

        var replaceButton = this.FindControl<Button>("ReplaceButton");
        var replaceAllButton = this.FindControl<Button>("ReplaceAllButton");
        if (replaceButton != null)
            replaceButton.Click += OnReplaceClick;
        if (replaceAllButton != null)
            replaceAllButton.Click += OnReplaceAllClick;

        query.KeyDown += OnQueryKeyDown;
        list.KeyDown += OnListKeyDown;
        Closed += (_, _) =>
        {
            _debounceTimer?.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => query.Focus());
    }

    private void WireOption(CheckBox? box)
    {
        if (box != null)
            box.IsCheckedChanged += (_, _) => ScheduleSearch();
    }

    private TabSearchOptions CurrentOptions() =>
        new()
        {
            MatchCase = this.FindControl<CheckBox>("MatchCaseBox")?.IsChecked == true,
            WholeWord = this.FindControl<CheckBox>("WholeWordBox")?.IsChecked == true,
            UseRegex = this.FindControl<CheckBox>("RegexBox")?.IsChecked == true,
            MaxHits = 500
        };

    private void ScheduleSearch()
    {
        if (_debounceTimer == null)
        {
            _ = SearchAsync();
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task SearchAsync()
    {
        var query = this.FindControl<TextBox>("QueryBox");
        var list = this.FindControl<ListBox>("ResultsList");
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (query == null || list == null)
            return;

        var needle = query.Text ?? "";
        _lastPattern = needle;
        var options = CurrentOptions();
        var version = ++_searchVersion;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (needle.Length == 0)
        {
            _hits = [];
            list.ItemsSource = _hits;
            if (summary != null)
                summary.Text = "Type to search open tabs";
            return;
        }

        if (summary != null)
            summary.Text = "Searching…";

        // Snapshot text on UI thread; search off-thread.
        var snapshots = _tabs
            .Select(t => (Tab: t, Title: t.Title.TrimEnd('*'), Text: t.Text))
            .ToList();

        List<FindHit> hits;
        var truncated = false;
        try
        {
            (hits, truncated) = await Task.Run(() =>
            {
                var found = new List<FindHit>();
                var wasTruncated = false;
                foreach (var snap in snapshots)
                {
                    ct.ThrowIfCancellationRequested();
                    var remaining = 500 - found.Count;
                    if (remaining <= 0)
                    {
                        wasTruncated = true;
                        break;
                    }

                    var result = TabSearch.Search(snap.Text, needle, new TabSearchOptions
                    {
                        MatchCase = options.MatchCase,
                        WholeWord = options.WholeWord,
                        UseRegex = options.UseRegex,
                        MaxHits = remaining
                    });
                    wasTruncated |= result.Truncated;

                    foreach (var match in result.Matches)
                    {
                        found.Add(new FindHit
                        {
                            Tab = snap.Tab,
                            Offset = match.Offset,
                            Line = match.Line,
                            Column = match.Column,
                            MatchLength = match.Length,
                            Location = $"{snap.Title}:{match.Line}",
                            Preview = match.Preview
                        });
                    }
                }

                return (found, wasTruncated);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (version != _searchVersion || ct.IsCancellationRequested)
            return;

        _hits = hits;
        list.ItemsSource = _hits;
        if (_hits.Count > 0)
            list.SelectedIndex = 0;

        if (summary != null)
        {
            var suffix = truncated ? " (truncated)" : "";
            summary.Text = $"{_hits.Count} hit(s){suffix} — Enter jump · Replace / Replace All · Esc close";
        }
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("ResultsList");
        var replaceBox = this.FindControl<TextBox>("ReplaceBox");
        if (list?.SelectedItem is not FindHit hit || replaceBox == null)
            return;

        if (hit.Tab.IsReadOnly)
            return;

        var replacement = BuildReplacement(
            hit.Tab.Text.Substring(hit.Offset, hit.MatchLength),
            replaceBox.Text ?? "",
            CurrentOptions());

        hit.Tab.Document.Replace(hit.Offset, hit.MatchLength, replacement);
        ReplaceCount++;
        _ = SearchAsync();
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        var replaceBox = this.FindControl<TextBox>("ReplaceBox");
        if (replaceBox == null || _hits.Count == 0)
            return;

        var options = CurrentOptions();
        var replacementTemplate = replaceBox.Text ?? "";
        var total = 0;

        foreach (var group in _hits.GroupBy(h => h.Tab))
        {
            if (group.Key.IsReadOnly)
                continue;

            var ordered = group.OrderByDescending(h => h.Offset).ToList();
            foreach (var hit in ordered)
            {
                if (hit.Offset < 0 || hit.Offset + hit.MatchLength > group.Key.Document.TextLength)
                    continue;

                var slice = group.Key.Document.GetText(hit.Offset, hit.MatchLength);
                var replacement = BuildReplacement(slice, replacementTemplate, options);
                group.Key.Document.Replace(hit.Offset, hit.MatchLength, replacement);
                total++;
            }
        }

        ReplaceCount += total;
        _ = SearchAsync();
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (summary != null && total > 0)
            summary.Text = $"Replaced {total} occurrence(s). {_hits.Count} hit(s) remaining.";
    }

    private string BuildReplacement(string matchedText, string template, TabSearchOptions options)
    {
        if (!options.UseRegex || string.IsNullOrEmpty(_lastPattern))
            return template;

        var flags = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
            flags |= RegexOptions.IgnoreCase;
        var regex = new Regex(_lastPattern, flags);
        return regex.Replace(matchedText, template, 1);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list == null)
            return;

        if (e.Key == Key.Down)
        {
            if (list.SelectedIndex < _hits.Count - 1)
                list.SelectedIndex++;
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (list.SelectedIndex > 0)
                list.SelectedIndex--;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
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
        if (list?.SelectedItem is not FindHit hit)
            return;

        Result = hit;
        Close(hit);
    }
}
