using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace x_pad.ViewModels;

public partial class FileTreeNode : ObservableObject
{
    private readonly Func<FileTreeNode, System.Collections.Generic.IEnumerable<FileTreeNode>>? _loadChildren;
    private bool _loaded;

    public FileTreeNode(
        string path,
        string name,
        bool isDirectory,
        Func<FileTreeNode, System.Collections.Generic.IEnumerable<FileTreeNode>>? loadChildren = null)
    {
        Path = path;
        Name = name;
        IsDirectory = isDirectory;
        _loadChildren = loadChildren;
        if (isDirectory)
            Children = [new FileTreeNode("", "…", false)];
    }

    public string Path { get; }
    public string Name { get; }
    public bool IsDirectory { get; }

    public ObservableCollection<FileTreeNode>? Children { get; }

    [ObservableProperty]
    private bool isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            EnsureChildren();
    }

    public void EnsureChildren()
    {
        if (!IsDirectory || _loaded || Children == null || _loadChildren == null)
            return;

        _loaded = true;
        Children.Clear();
        foreach (var child in _loadChildren(this))
            Children.Add(child);
    }
}
