using System.Collections.Generic;

namespace x_pad.Models;

public sealed class AppSettings
{
    public double FontSize { get; set; } = 13;
    public string FontFamily { get; set; } = "Cascadia Code, JetBrains Mono, Consolas, Courier New, monospace";
    public int TabSize { get; set; } = 4;
    /// <summary>When true, Tab inserts spaces (IndentationSize). When false, inserts a real tab.</summary>
    public bool InsertSpaces { get; set; } = true;
    public bool WordCompletion { get; set; } = true;
    public bool AutoCloseBrackets { get; set; } = true;
    public bool AutoIndent { get; set; } = true;
    public bool IndentGuides { get; set; } = true;
    public bool ShowWhitespace { get; set; }
    public bool TrimTrailingWhitespaceOnSave { get; set; } = true;
    public bool EnsureFinalNewlineOnSave { get; set; } = true;
    public bool BackupOnSave { get; set; } = true;
    /// <summary>Autosave interval in seconds for dirty files with a path. 0 disables.</summary>
    public int AutosaveIntervalSeconds { get; set; }
    public bool WordWrap { get; set; }
    /// <summary>When true, minimizing the main window hides it to the system tray.</summary>
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public List<string> RecentFiles { get; set; } = [];
    public int MaxRecentFiles { get; set; } = 12;
    public Dictionary<string, string> LanguageServers { get; set; } = new()
    {
        ["typescript"] = "typescript-language-server --stdio",
        ["javascript"] = "typescript-language-server --stdio",
        ["python"] = "pyright-langserver --stdio"
    };
}
