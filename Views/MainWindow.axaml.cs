using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using x_pad.ViewModels;

namespace x_pad.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _trayHideInProgress;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Activated += OnActivated;
        Closing += OnClosing;
        PropertyChanged += OnWindowPropertyChanged;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        if (this.FindControl<TerminalPanel>("TerminalHost") is { } terminal)
            terminal.HideRequested += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.IsTerminalVisible = false;
            };
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty)
            return;
        if (WindowState != WindowState.Minimized)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (!vm.Settings.Current.MinimizeToTray)
            return;
        if (_trayHideInProgress)
            return;

        _trayHideInProgress = true;
        try
        {
            // Avoid staying as a minimized taskbar ghost when tray owns the surface.
            WindowState = WindowState.Normal;
            ShowInTaskbar = false;
            Hide();
        }
        finally
        {
            _trayHideInProgress = false;
        }
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CheckSelectedTabForExternalChanges();
    }

    public DocumentEditor? FindSelectedDocumentEditor()
    {
        return this.GetVisualDescendants()
            .OfType<DocumentEditor>()
            .FirstOrDefault(e => e.IsVisible);
    }

    public DocumentEditor? FindDocumentEditor(DocumentViewModel tab)
    {
        return this.GetVisualDescendants()
            .OfType<DocumentEditor>()
            .FirstOrDefault(e => ReferenceEquals(e.DataContext, tab));
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null)
            return;

        foreach (var item in files)
        {
            var path = item.Path.LocalPath;
            if (System.IO.File.Exists(path))
                await vm.OpenFileFromPath(path);
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            return;
        if (sender is not Control control)
            return;
        if (control.DataContext is not DocumentViewModel tab)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        e.Handled = true;
        _ = vm.CloseTabCommand.ExecuteAsync(tab);
    }
}
