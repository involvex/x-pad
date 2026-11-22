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
    /// <summary>
    /// External format command. Use <c>$FILE</c> for the path (e.g. <c>bunx prettier --write $FILE</c>).
    /// Empty disables formatting.
    /// </summary>
    public string FormatCommand { get; set; } = "";
    /// <summary>When true, run <see cref="FormatCommand"/> after a successful save (including autosave).</summary>
    public bool FormatOnSave { get; set; }
    /// <summary>
    /// Comma-separated extensions to format (e.g. <c>.js,.ts,.json,.md</c>).
    /// Empty means every saved file with a path.
    /// </summary>
    public string FormatExtensions { get; set; } = "";
    public bool WordWrap { get; set; }
    /// <summary>When true, minimizing the main window hides it to the system tray.</summary>
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    /// <summary>When true, check GitHub Releases for a newer version shortly after startup.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    /// <summary>UTC timestamp of the last successful or attempted update check (ISO-8601).</summary>
    public string? LastUpdateCheckUtc { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public int MaxRecentFiles { get; set; } = 12;
    public List<string> FavoriteFiles { get; set; } = [];
    public int MaxFavoriteFiles { get; set; } = 40;
    /// <summary>Last opened workspace folder (Open Folder).</summary>
    public string? LastWorkspacePath { get; set; }
    /// <summary>Directory names excluded from the file tree and disk search.</summary>
    public List<string> WorkspaceExcludeNames { get; set; } =
    [
        ".git", "node_modules", "bin", "obj", ".vs"
    ];
    public Dictionary<string, string> LanguageServers { get; set; } = new()
    {
        ["typescript"] = "typescript-language-server --stdio",
        ["javascript"] = "typescript-language-server --stdio",
        ["python"] = "pyright-langserver --stdio"
    };
}
