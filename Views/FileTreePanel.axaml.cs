using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using x_pad.ViewModels;

namespace x_pad.Views;

public partial class FileTreePanel : UserControl
{
    public event EventHandler<string>? FileOpenRequested;

    public FileTreePanel()
    {
        InitializeComponent();
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e) =>
        TryOpenSelected(sender as TreeView);

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryOpenSelected(sender as TreeView);
            e.Handled = true;
        }
    }

    private void TryOpenSelected(TreeView? tree)
    {
        if (tree?.SelectedItem is not FileTreeNode node)
            return;
        if (node.IsDirectory)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.Path))
            FileOpenRequested?.Invoke(this, node.Path);
    }
}
