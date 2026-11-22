using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using x_pad.Services.Lsp;

namespace x_pad.Views;

public sealed class CodeActionListItem
{
    public required LspCodeAction Action { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public bool CanApply => Action.Edit != null;
}

public partial class CodeActionsWindow : Window
{
    private readonly List<CodeActionListItem> _items;

    public LspCodeAction? Result { get; private set; }

    public CodeActionsWindow() : this([])
    {
    }

    public CodeActionsWindow(IReadOnlyList<LspCodeAction> actions)
    {
        InitializeComponent();
        _items = actions.Select(a => new CodeActionListItem
        {
            Action = a,
            Title = a.Title,
            Subtitle = a.Edit != null
                ? (string.IsNullOrWhiteSpace(a.Kind) ? "Apply edit" : a.Kind!)
                : "Command only — not supported yet"
        }).ToList();

        var list = this.FindControl<ListBox>("ActionsList");
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (list != null)
        {
            list.ItemsSource = _items;
            if (_items.Count > 0)
                list.SelectedIndex = 0;
            list.KeyDown += OnListKeyDown;
        }

        if (summary != null)
            summary.Text = _items.Count == 0
                ? "No code actions"
                : $"{_items.Count} action(s) — Enter apply · Esc close";

        Opened += (_, _) => Dispatcher.UIThread.Post(() => list?.Focus());
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e) => Accept();

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
        var list = this.FindControl<ListBox>("ActionsList");
        if (list?.SelectedItem is not CodeActionListItem item)
            return;

        if (!item.CanApply)
            return;

        Result = item.Action;
        Close(item.Action);
    }
}
