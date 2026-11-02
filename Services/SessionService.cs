using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace x_pad.Services;

public class SessionService
{
    private readonly string _sessionPath;

    public SessionService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var xpadDir = Path.Combine(appData, "XPad");
        Directory.CreateDirectory(xpadDir);
        _sessionPath = Path.Combine(xpadDir, "session.json");
    }

    public void SaveSession(List<string> files)
    {
        try
        {
            var session = new SessionData { Files = files };
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_sessionPath, json);
        }
        catch
        {
            // Ignore errors for MVP
        }
    }

    public List<string> LoadSession()
    {
        try
        {
            if (!File.Exists(_sessionPath)) return new List<string>();
            var json = File.ReadAllText(_sessionPath);
            var session = JsonSerializer.Deserialize<SessionData>(json);
            return session?.Files ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private class SessionData
    {
        public List<string> Files { get; set; } = new();
    }
}
