using Avalonia.Controls;
using Avalonia.Interactivity;

namespace x_pad.Views;

public partial class RenameSymbolWindow : Window
{
    public string? NewName { get; private set; }

    public RenameSymbolWindow() : this("")
    {
    }

    public RenameSymbolWindow(string placeholder)
    {
        InitializeComponent();
        var box = this.FindControl<TextBox>("NameBox");
        if (box != null)
        {
            box.Text = placeholder ?? "";
            box.Focus();
            box.SelectAll();
        }
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("NameBox");
        var name = box?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Close(false);
            return;
        }

        NewName = name;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
