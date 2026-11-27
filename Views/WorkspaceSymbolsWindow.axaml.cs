using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using x_pad.Services.Lsp;

namespace x_pad.Views;

public sealed class WorkspaceSymbolHit
{
    public required LspWorkspaceSymbol Symbol { get; init; }
    public required LspLocation Location { get; init; }
    public required string Display { get; init; }
}

public partial class WorkspaceSymbolsWindow : Window
{
    private readonly List<WorkspaceSymbolHit> _hits;

    public WorkspaceSymbolHit? Result { get; private set; }

    public WorkspaceSymbolsWindow() : this([])
    {
    }

    public WorkspaceSymbolsWindow(IReadOnlyList<LspWorkspaceSymbol> symbols)
    {
        InitializeComponent();
        _hits = symbols
            .Select(symbol =>
            {
                var loc = symbol.Location;
                var path = LspNavigationParser.TryUriToLocalPath(loc.Uri) ?? loc.Uri;
                var name = Path.GetFileName(path);
                var container = string.IsNullOrWhiteSpace(symbol.ContainerName)
                    ? ""
                    : $" ({symbol.ContainerName})";
                return new WorkspaceSymbolHit
                {
                    Symbol = symbol,
                    Location = loc,
                    Display = $"{symbol.Name}{container}  ·  {name}:{loc.Line + 1}:{loc.Character + 1}"
                };
            })
            .ToList();

        var list = this.FindControl<ListBox>("ResultsList");
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (list != null)
        {
            list.ItemsSource = _hits;
            if (_hits.Count > 0)
                list.SelectedIndex = 0;
            list.KeyDown += OnListKeyDown;
        }

        if (summary != null)
            summary.Text = _hits.Count == 0
                ? "No symbols"
                : $"{_hits.Count} symbol(s) — Enter jump · Esc close";

        Opened += (_, _) => Dispatcher.UIThread.Post(() => list?.Focus());
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e) => Accept();

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

    private void Accept()
    {
        var list = this.FindControl<ListBox>("ResultsList");
        if (list?.SelectedItem is not WorkspaceSymbolHit hit)
            return;

        Result = hit;
        Close(hit);
    }
}
