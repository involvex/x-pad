using System;
using Dock.Model.Avalonia.Controls;
using x_pad.ViewModels;
using x_pad.Views;

namespace x_pad.ViewModels.Dock;

public sealed class ExplorerToolViewModel : Tool
{
    public FileTreePanel Panel { get; } = new();

    public FileTreeViewModel? FileTree
    {
        get => Panel.DataContext as FileTreeViewModel;
        set => Panel.DataContext = value;
    }

    public ExplorerToolViewModel()
    {
        Id = "Explorer";
        Title = "Explorer";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
        // Dock renders Tool.Content. Application data templates are not used for tools.
        Content = new Func<IServiceProvider, object>(_ => Panel);
    }
}
