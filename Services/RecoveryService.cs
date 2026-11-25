using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace x_pad.Services;

public sealed class RecoveryEntry
{
    public string Id { get; set; } = "";
    public string? FilePath { get; set; }
    public string Title { get; set; } = "Untitled";
    public string Text { get; set; } = "";
    public bool WasDirty { get; set; }
    public string EncodingName { get; set; } = "UTF-8";
    public bool HasBom { get; set; }
    public string LineEnding { get; set; } = "LF";
    public bool InsertSpaces { get; set; } = true;
    public string? LanguageOverrideExtension { get; set; }
    public int Line { get; set; } = 1;
    public int Column { get; set; } = 1;
    public DateTime SavedUtc { get; set; }
}

/// <summary>Persists dirty/untitled buffers under %APPDATA%/XPad/recovery.</summary>
public sealed class RecoveryService
{
    private readonly string _recoveryDir;
    private readonly string _cleanExitMarker;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RecoveryService() : this(null)
    {
    }

    public RecoveryService(string? recoveryDir)
    {
        if (!string.IsNullOrWhiteSpace(recoveryDir))
        {
            _recoveryDir = recoveryDir;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _recoveryDir = Path.Combine(appData, "XPad", "recovery");
        }

        Directory.CreateDirectory(_recoveryDir);
        _cleanExitMarker = Path.Combine(Path.GetDirectoryName(_recoveryDir)!, "clean-exit");
    }

    public string RecoveryDirectory => _recoveryDir;

    public void MarkDirtyExit()
    {
        try
        {
            if (File.Exists(_cleanExitMarker))
                File.Delete(_cleanExitMarker);
        }
        catch
        {
            // Ignore
        }
    }

    public void MarkCleanExit()
    {
        try
        {
            File.WriteAllText(_cleanExitMarker, DateTime.UtcNow.ToString("O"));
            ClearAll();
        }
        catch
        {
            // Ignore
        }
    }

    public bool HasPendingRecovery()
    {
        if (File.Exists(_cleanExitMarker))
            return false;

        try
        {
            return Directory.EnumerateFiles(_recoveryDir, "*.json").Any();
        }
        catch
        {
            return false;
        }
    }

    public void DeleteEntry(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        try
        {
            var path = Path.Combine(_recoveryDir, id + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore
        }
    }

    public IReadOnlyList<RecoveryEntry> ListEntries()
    {
        try
        {
            return Directory.EnumerateFiles(_recoveryDir, "*.json")
                .Select(TryLoad)
                .Where(e => e != null)
                .Cast<RecoveryEntry>()
                .OrderBy(e => e.SavedUtc)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void SaveEntry(RecoveryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
            entry.Id = Guid.NewGuid().ToString("N");

        entry.SavedUtc = DateTime.UtcNow;
        var path = Path.Combine(_recoveryDir, entry.Id + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(entry, JsonOptions), Encoding.UTF8);
            MarkDirtyExit();
        }
        catch
        {
            // Ignore recovery write failures
        }
    }

    /// <summary>Replaces all recovery files with the current dirty/untitled set.</summary>
    public void ReplaceAll(IEnumerable<RecoveryEntry> entries)
    {
        try
        {
            ClearAll();
            var list = entries.ToList();
            if (list.Count == 0)
                return;

            foreach (var entry in list)
                SaveEntry(entry);
        }
        catch
        {
            // Ignore
        }
    }

    public void ClearAll()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_recoveryDir, "*.json"))
                File.Delete(file);
        }
        catch
        {
            // Ignore
        }
    }

    private static RecoveryEntry? TryLoad(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var entry = JsonSerializer.Deserialize<RecoveryEntry>(json);
            if (entry == null)
                return null;
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Path.GetFileNameWithoutExtension(path);
            return entry;
        }
        catch
        {
            return null;
        }
    }
}
