using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using x_pad.ViewModels;

namespace x_pad.Views;

public sealed class FindHit
{
    public required DocumentViewModel Tab { get; init; }
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

    public FindHit? Result { get; private set; }

    public FindInOpenTabsWindow() : this([])
    {
    }

    public FindInOpenTabsWindow(IEnumerable<DocumentViewModel> tabs)
    {
        _tabs = tabs.ToList();
        InitializeComponent();

        var query = this.FindControl<TextBox>("QueryBox");
        var matchCase = this.FindControl<CheckBox>("MatchCaseBox");
        var list = this.FindControl<ListBox>("ResultsList");
        if (query == null || list == null)
            return;

        query.TextChanged += (_, _) => Search();
        if (matchCase != null)
            matchCase.IsCheckedChanged += (_, _) => Search();

        query.KeyDown += OnQueryKeyDown;
        list.KeyDown += OnListKeyDown;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => query.Focus());
    }

    private void Search()
    {
        var query = this.FindControl<TextBox>("QueryBox");
        var matchCase = this.FindControl<CheckBox>("MatchCaseBox");
        var list = this.FindControl<ListBox>("ResultsList");
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (query == null || list == null)
            return;

        var needle = query.Text ?? "";
        _hits = [];
        if (needle.Length > 0)
        {
            var comparison = matchCase?.IsChecked == true
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            foreach (var tab in _tabs)
            {
                var text = tab.Text;
                var title = tab.Title.TrimEnd('*');
                var lineStarts = BuildLineStarts(text);
                var from = 0;
                while (from < text.Length)
                {
                    var index = text.IndexOf(needle, from, comparison);
                    if (index < 0)
                        break;

                    var (line, column) = OffsetToLineColumn(lineStarts, index);
                    var lineText = GetLineText(text, lineStarts, line);
                    _hits.Add(new FindHit
                    {
                        Tab = tab,
                        Line = line,
                        Column = column,
                        MatchLength = needle.Length,
                        Location = $"{title}:{line}",
                        Preview = lineText.Trim()
                    });

                    from = index + Math.Max(1, needle.Length);
                    if (_hits.Count >= 500)
                        break;
                }

                if (_hits.Count >= 500)
                    break;
            }
        }

        list.ItemsSource = _hits;
        if (_hits.Count > 0)
            list.SelectedIndex = 0;

        if (summary != null)
        {
            summary.Text = needle.Length == 0
                ? "Type to search open tabs"
                : $"{_hits.Count} hit(s) — Enter jump · Esc close";
        }
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

    private static List<int> BuildLineStarts(string text)
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

    private static (int Line, int Column) OffsetToLineColumn(List<int> lineStarts, int offset)
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

    private static string GetLineText(string text, List<int> lineStarts, int line)
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
