using System;
using System.IO;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer.SystemTextJson;

namespace x_pad.Services;

public sealed class DockLayoutService
{
    private readonly string _path;
    private readonly DockSerializer _serializer = new();

    public DockLayoutService()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XPad",
            "layout.json");
    }

    public string LayoutPath => _path;

    public bool TrySave(IDockable? layout)
    {
        if (layout == null)
            return false;

        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var stream = File.Create(_path);
            _serializer.Save(stream, layout);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IRootDock? TryLoadRoot()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            using var stream = File.OpenRead(_path);
            return _serializer.Load<IRootDock>(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch
        {
            // ignore
        }
    }
}
