using System;
using System.IO;
using System.Text.Json;
using x_pad.Models;

namespace x_pad.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "XPad");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        Load();
    }

    public AppSettings Current => _settings;

    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _settings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(_settingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
            Changed?.Invoke();
        }
        catch
        {
            // Ignore persistence failures for MVP
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(_settings);
        Save();
    }
}
