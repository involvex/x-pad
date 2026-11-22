using System;
using System.Collections.Generic;
using System.IO;

namespace x_pad.Services;

/// <summary>Single-folder workspace root for the file tree and disk search.</summary>
public sealed class WorkspaceService
{
    public static IReadOnlyList<string> DefaultExcludeNames { get; } =
        [".git", "node_modules", "bin", "obj", ".vs"];

    public string? Root { get; private set; }

    public event EventHandler? Changed;

    public bool TryOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!Directory.Exists(full))
            return false;

        if (string.Equals(Root, full, StringComparison.OrdinalIgnoreCase))
            return true;

        Root = full;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Close()
    {
        if (Root == null)
            return;

        Root = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
