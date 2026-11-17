using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using x_pad.ViewModels;

namespace x_pad.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    public DocumentEditor? FindSelectedDocumentEditor()
    {
        return this.GetVisualDescendants()
            .OfType<DocumentEditor>()
            .FirstOrDefault(e => e.IsVisible);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        WireEditors();
        if (DataContext is MainWindowViewModel vm)
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.SelectedTab))
                    WireEditors();
            };
    }

    private void WireEditors()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        foreach (var editor in this.GetVisualDescendants().OfType<DocumentEditor>())
            editor.ConfigureServices(vm.Settings, vm.Lsp);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || DataContext is not MainWindowViewModel vm)
            return;

        e.Cancel = true;
        var ok = await vm.ConfirmCloseAllForExitAsync();
        if (!ok)
            return;

        _forceClose = true;
        Close();
    }
}
