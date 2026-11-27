using Avalonia.Controls;
using Avalonia.Interactivity;

namespace x_pad.Views;

public partial class GoToSymbolQueryWindow : Window
{
    public string Query { get; private set; } = "";

    public GoToSymbolQueryWindow() : this("")
    {
    }

    public GoToSymbolQueryWindow(string initialQuery)
    {
        InitializeComponent();
        var box = this.FindControl<TextBox>("QueryBox");
        if (box != null)
        {
            box.Text = initialQuery ?? "";
            box.Focus();
            box.SelectAll();
        }
    }

    private void OnSearch(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("QueryBox");
        Query = box?.Text?.Trim() ?? "";
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
