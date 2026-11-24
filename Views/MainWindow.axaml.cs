using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using x_pad.Services;
using x_pad.ViewModels;

namespace x_pad.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;
    private bool _trayHideInProgress;
    private bool _fileTreeWired;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Activated += OnActivated;
        Closing += OnClosing;
        PropertyChanged += OnWindowPropertyChanged;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DoubleTappedEvent, OnDocumentTabStripDoubleTapped, RoutingStrategies.Bubble);

        if (DataContext is MainWindowViewModel vm)
            WireTerminal(vm);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm2)
                WireTerminal(vm2);
        };
    }

    private void WireTerminal(MainWindowViewModel vm)
    {
        vm.TerminalTool.Panel.HideRequested += (_, _) =>
        {
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
        if (DataContext is MainWindowViewModel vm && this.FindControl<DockControl>("DockHost") is { } dock)
            vm.AttachDock(dock);

        WireEditors();
        WireFileTree();
        ApplyKeyBindings();

        if (DataContext is MainWindowViewModel vm2)
            vm2.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.SelectedTab))
                    WireEditors();
            };
    }

    public void ApplyKeyBindings()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        KeyBindingApplier.Apply(this, vm, vm.Settings.Current.KeyBindings);
    }

    private void WireFileTree()
    {
        if (_fileTreeWired)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.ExplorerTool.Panel.FileOpenRequested += async (_, path) =>
        {
            await vm.OpenFileFromPath(path);
        };
        _fileTreeWired = true;
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

        vm.SaveDockLayout();
        _forceClose = true;
        Close();
    }

    private void OnDocumentTabStripDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control source)
            return;

        // Only empty strip area — not an existing tab header.
        if (source.FindAncestorOfType<DocumentTabStripItem>(includeSelf: true) != null)
            return;
        if (source.FindAncestorOfType<DocumentTabStrip>(includeSelf: true) == null)
            return;

        if (DataContext is MainWindowViewModel vm)
            vm.NewTabCommand.Execute(null);

        e.Handled = true;
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
            if (System.IO.Directory.Exists(path))
                vm.OpenWorkspace(path);
            else if (System.IO.File.Exists(path))
                await vm.OpenFileFromPath(path);
        }
    }
}