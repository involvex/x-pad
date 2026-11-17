using Avalonia.Controls;
using Avalonia.Interactivity;

namespace x_pad.Views;

public enum ConfirmSaveResult
{
    Cancel,
    Save,
    Discard
}

public partial class ConfirmSaveWindow : Window
{
    public ConfirmSaveResult Result { get; private set; } = ConfirmSaveResult.Cancel;

    public ConfirmSaveWindow() : this("Untitled")
    {
    }

    public ConfirmSaveWindow(string documentTitle)
    {
        InitializeComponent();
        var message = this.FindControl<TextBlock>("MessageText");
        if (message != null)
            message.Text = $"Do you want to save changes to \"{documentTitle.TrimEnd('*')}\"?";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Result = ConfirmSaveResult.Save;
        Close(Result);
    }

    private void OnDiscard(object? sender, RoutedEventArgs e)
    {
        Result = ConfirmSaveResult.Discard;
        Close(Result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = ConfirmSaveResult.Cancel;
        Close(Result);
    }
}
