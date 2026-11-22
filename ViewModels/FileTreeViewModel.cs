using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using x_pad.Services;

namespace x_pad.ViewModels;

public partial class FileTreeViewModel : ViewModelBase
{
    private readonly WorkspaceService _workspace;
    private readonly Func<IReadOnlyList<string>> _excludeNames;

    public FileTreeViewModel(WorkspaceService workspace, Func<IReadOnlyList<string>> excludeNames)
    {
        _workspace = workspace;
        _excludeNames = excludeNames;
        _workspace.Changed += (_, _) => Reload();
        Reload();
    }

    public ObservableCollection<FileTreeNode> Roots { get; } = new();

    [ObservableProperty]
    private string? rootPath;

    public void Reload()
    {
        Roots.Clear();
        RootPath = _workspace.Root;
        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
            return;

        var root = new FileTreeNode(
            RootPath,
            Path.GetFileName(RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                is { Length: > 0 } name
                ? name
                : RootPath,
            isDirectory: true,
            LoadChildren);
        root.IsExpanded = true;
        Roots.Add(root);
    }

    private IEnumerable<FileTreeNode> LoadChildren(FileTreeNode parent)
    {
        var excludes = new HashSet<string>(_excludeNames(), StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> dirs;
        IEnumerable<string> files;
        try
        {
            dirs = Directory.EnumerateDirectories(parent.Path);
            files = Directory.EnumerateFiles(parent.Path);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (excludes.Contains(name))
                continue;
            yield return new FileTreeNode(dir, name, isDirectory: true, LoadChildren);
        }

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            yield return new FileTreeNode(file, name, isDirectory: false);
        }
    }
}
