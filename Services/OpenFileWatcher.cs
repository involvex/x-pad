using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace x_pad.Services;

/// <summary>
/// Watches open file paths and raises change notifications, ignoring our own saves.
/// </summary>
public sealed class OpenFileWatcher : IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _ignoreUntilTicks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private int _disposed;

    public event EventHandler<string>? FileChanged;

    public void Watch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var full = Path.GetFullPath(path);
        if (_watchers.ContainsKey(full))
            return;

        lock (_gate)
        {
            if (_watchers.ContainsKey(full) || Volatile.Read(ref _disposed) != 0)
                return;

            try
            {
                var dir = Path.GetDirectoryName(full);
                var name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
                    return;

                var watcher = new FileSystemWatcher(dir, name)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Renamed += OnRenamed;
                if (!_watchers.TryAdd(full, watcher))
                    watcher.Dispose();
            }
            catch
            {
                // Some paths cannot be watched (network, permissions).
            }
        }
    }

    public void Unwatch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (_watchers.TryRemove(full, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _ignoreUntilTicks.TryRemove(full, out _);
    }

    public void Sync(IEnumerable<string?> paths)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            try
            {
                wanted.Add(Path.GetFullPath(path));
            }
            catch
            {
                // Skip bad paths
            }
        }

        foreach (var existing in _watchers.Keys)
        {
            if (!wanted.Contains(existing))
                Unwatch(existing);
        }

        foreach (var path in wanted)
            Watch(path);
    }

    /// <summary>Suppress change events for a short window after we write the file.</summary>
    public void IgnoreOwnWrite(string? path, int milliseconds = 1500)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var full = Path.GetFullPath(path);
            _ignoreUntilTicks[full] = Environment.TickCount64 + milliseconds;
        }
        catch
        {
            // Ignore
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e) =>
        RaiseIfNeeded(e.FullPath);

    private void OnChanged(object sender, FileSystemEventArgs e) =>
        RaiseIfNeeded(e.FullPath);

    private void RaiseIfNeeded(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (_ignoreUntilTicks.TryGetValue(full, out var until) && Environment.TickCount64 < until)
            return;

        FileChanged?.Invoke(this, full);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var key in _watchers.Keys)
            Unwatch(key);
    }
}
