using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace x_pad.Views;

public sealed class PaletteEntry
{
    public required string Label { get; init; }
    public string? Shortcut { get; init; }
    public required string Group { get; init; }
    public required Func<Task> InvokeAsync { get; init; }

    public string Detail =>
        !string.IsNullOrEmpty(Shortcut) ? Shortcut! : Group;
}

public partial class CommandPaletteWindow : Window
{
    private readonly List<PaletteEntry> _all;
    private List<PaletteEntry> _filtered = [];

    public PaletteEntry? Result { get; private set; }

    public CommandPaletteWindow() : this([])
    {
    }

    public CommandPaletteWindow(IEnumerable<PaletteEntry> entries)
    {
        _all = entries.ToList();
        InitializeComponent();

        var filter = this.FindControl<TextBox>("FilterBox");
        var list = this.FindControl<ListBox>("ResultsList");
        if (filter == null || list == null)
            return;

        filter.TextChanged += (_, _) => ApplyFilter(filter.Text);
        filter.KeyDown += OnFilterKeyDown;
        list.KeyDown += OnListKeyDown;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => filter.Focus());

        ApplyFilter("");
    }

    private void ApplyFilter(string? query)
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list == null)
            return;

        var q = (query ?? "").Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _all.ToList()
            : _all.Where(e =>
                    e.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.Group.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (e.Shortcut?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        list.ItemsSource = _filtered;
        if (_filtered.Count > 0)
            list.SelectedIndex = 0;
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list == null)
            return;

        if (e.Key == Key.Down)
        {
            if (list.SelectedIndex < _filtered.Count - 1)
                list.SelectedIndex++;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (list.SelectedIndex > 0)
                list.SelectedIndex--;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list?.SelectedItem is not PaletteEntry entry)
            return;

        Result = entry;
        Close(entry);
    }
}
