using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using x_pad.Services.Lsp;

namespace x_pad.Views;

public partial class DocumentOutlineWindow : Window
{
    public LspDocumentSymbol? Result { get; private set; }

    public DocumentOutlineWindow() : this([])
    {
    }

    public DocumentOutlineWindow(IReadOnlyList<LspDocumentSymbol> symbols)
    {
        InitializeComponent();
        var tree = this.FindControl<TreeView>("SymbolTree");
        var summary = this.FindControl<TextBlock>("SummaryBox");
        if (tree != null)
        {
            tree.ItemsSource = symbols;
            tree.KeyDown += OnTreeKeyDown;
        }

        if (summary != null)
            summary.Text = symbols.Count == 0
                ? "No symbols from language server"
                : $"{CountSymbols(symbols)} symbol(s) — Enter jump · Esc close";

        Opened += (_, _) => Dispatcher.UIThread.Post(() => tree?.Focus());
    }

    private static int CountSymbols(IReadOnlyList<LspDocumentSymbol> symbols)
    {
        var n = 0;
        foreach (var s in symbols)
        {
            n++;
            n += CountSymbols(s.Children);
        }
        return n;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
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
        var tree = this.FindControl<TreeView>("SymbolTree");
        if (tree?.SelectedItem is not LspDocumentSymbol symbol)
            return;

        Result = symbol;
        Close(symbol);
    }
}
