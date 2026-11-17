using Avalonia.Controls;
using Avalonia.Interactivity;

namespace x_pad.Views;

public partial class GoToLineWindow : Window
{
    public int? LineNumber { get; private set; }

    public GoToLineWindow() : this(1)
    {
    }

    public GoToLineWindow(int currentLine)
    {
        InitializeComponent();
        var box = this.FindControl<TextBox>("LineBox");
        if (box != null)
        {
            box.Text = currentLine.ToString();
            box.Focus();
            box.SelectAll();
        }
    }

    private void OnGo(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("LineBox");
        if (box != null && int.TryParse(box.Text, out var line) && line > 0)
        {
            LineNumber = line;
            Close(true);
            return;
        }
        Close(false);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
