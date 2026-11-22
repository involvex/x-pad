using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using x_pad.Services.Lsp;

namespace x_pad.Views;

public sealed class ReferenceHit
{
    public required LspLocation Location { get; init; }
    public required string Path { get; init; }
    public required string Display { get; init; }
}

public partial class FindReferencesWindow : Window
{
    private readonly List<ReferenceHit> _hits;

    public ReferenceHit? Result { get; private set; }

    public FindReferencesWindow() : this([])
    {
    }

    public FindReferencesWindow(IReadOnlyList<LspLocation> locations)
    {
        InitializeComponent();
        _hits = locations
            .Select(loc =>
            {
                var path = LspNavigationParser.TryUriToLocalPath(loc.Uri) ?? loc.Uri;
                var name = Path.GetFileName(path);
                return new ReferenceHit
                {
                    Location = loc,
                    Path = path,
                    Display = $"{name}:{loc.Line + 1}:{loc.Character + 1}  —  {path}"
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
                ? "No references"
                : $"{_hits.Count} reference(s) — Enter jump · Esc close";

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
        if (list?.SelectedItem is not ReferenceHit hit)
            return;

        Result = hit;
        Close(hit);
    }
}
