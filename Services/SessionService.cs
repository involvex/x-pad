using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace x_pad.Services;

public sealed class SessionTabState
{
    public string Path { get; set; } = "";
    public int Line { get; set; } = 1;
    public int Column { get; set; } = 1;
}

public sealed class SessionState
{
    public List<SessionTabState> Tabs { get; set; } = [];
    public string? SelectedPath { get; set; }

    /// <summary>Legacy flat path list from older builds.</summary>
    public List<string> Files { get; set; } = [];
}

public class SessionService
{
    private readonly string _sessionPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SessionService() : this(null)
    {
    }

    public SessionService(string? sessionPath)
    {
        if (!string.IsNullOrWhiteSpace(sessionPath))
        {
            _sessionPath = sessionPath;
            var dir = Path.GetDirectoryName(sessionPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var xpadDir = Path.Combine(appData, "XPad");
        Directory.CreateDirectory(xpadDir);
        _sessionPath = Path.Combine(xpadDir, "session.json");
    }

    public void SaveSession(SessionState state)
    {
        try
        {
            // Keep Files in sync for older readers / quick inspection.
            state.Files = state.Tabs.Select(t => t.Path).ToList();
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(_sessionPath, json);
        }
        catch
        {
            // Ignore session write failures
        }
    }

    public SessionState LoadSession()
    {
        try
        {
            if (!File.Exists(_sessionPath))
                return new SessionState();

            var json = File.ReadAllText(_sessionPath);
            var state = JsonSerializer.Deserialize<SessionState>(json) ?? new SessionState();

            if (state.Tabs.Count == 0 && state.Files.Count > 0)
            {
                state.Tabs = state.Files
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => new SessionTabState { Path = f })
                    .ToList();
            }

            return state;
        }
        catch
        {
            return new SessionState();
        }
    }

    /// <summary>Backward-compatible helper used by older call sites/tests.</summary>
    public List<string> LoadSessionPaths() =>
        LoadSession().Tabs.Select(t => t.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
}
