using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace x_pad.Views;

public partial class PrintWindow : Window
{
    public string? SelectedPrinter { get; private set; }

    public PrintWindow() : this([])
    {
    }

    public PrintWindow(IReadOnlyList<string> printers)
    {
        InitializeComponent();
        var box = this.FindControl<ComboBox>("PrinterBox");
        if (box == null)
            return;

        box.ItemsSource = printers;
        if (printers.Count > 0)
            box.SelectedIndex = 0;
    }

    private void OnPrint(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<ComboBox>("PrinterBox");
        SelectedPrinter = box?.SelectedItem as string ?? box?.SelectedItem?.ToString();
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
