using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using x_pad.Models;
using x_pad.Services;
using App = x_pad.App;

namespace x_pad.ViewModels;

public sealed class PreferencesPage
{
    public required string Id { get; init; }
    public required string Title { get; init; }
}

public partial class PreferencesViewModel : ViewModelBase
{
    public static readonly string[] FontChoices =
    [
        "Cascadia Code, JetBrains Mono, Consolas, Courier New, monospace",
        "Consolas, Courier New, monospace",
        "Courier New, monospace",
        "JetBrains Mono, Consolas, monospace",
        "Segoe UI, sans-serif"
    ];

    public static readonly string[] ThemeChoices =
        ["Dark", "Light", "System", "Monokai", "Hacker", "Amoled", "Github"];

    public ObservableCollection<PreferencesPage> Pages { get; } =
    [
        new() { Id = "editor", Title = "Editor" },
        new() { Id = "saving", Title = "Saving" },
        new() { Id = "appearance", Title = "Appearance" },
        new() { Id = "formatter", Title = "Formatter" },
        new() { Id = "lsp", Title = "Language servers" },
        new() { Id = "ai", Title = "AI" },
        new() { Id = "shortcuts", Title = "Shortcuts" },
        new() { Id = "updates", Title = "Updates" }
    ];

    public const string KiloFreeBaseUrl = "https://api.kilo.ai/api/gateway";
    public const string KiloFreeModel = "kilo-auto/free";
    public const string OllamaBaseUrl = "http://127.0.0.1:11434/v1";
    public const string OllamaDefaultModel = "qwen2.5-coder:1.5b";

    public ObservableCollection<ShortcutBindingItem> ShortcutBindings { get; } = [];

    [ObservableProperty]
    private string shortcutConflict = "";

    private readonly Func<Task>? _checkUpdates;

    [ObservableProperty]
    private PreferencesPage? selectedPage;

    [ObservableProperty]
    private double fontSize = 13;

    [ObservableProperty]
    private string fontFamily = FontChoices[0];

    [ObservableProperty]
    private int tabSize = 4;

    [ObservableProperty]
    private bool insertSpaces = true;

    [ObservableProperty]
    private bool wordWrap;

    [ObservableProperty]
    private bool showWhitespace;

    [ObservableProperty]
    private bool indentGuides = true;

    [ObservableProperty]
    private bool wordCompletion = true;

    [ObservableProperty]
    private bool autoCloseBrackets = true;

    [ObservableProperty]
    private bool autoIndent = true;

    [ObservableProperty]
    private bool trimTrailingWhitespaceOnSave = true;

    [ObservableProperty]
    private bool ensureFinalNewlineOnSave = true;

    [ObservableProperty]
    private bool backupOnSave = true;

    [ObservableProperty]
    private int autosaveIntervalSeconds;

    [ObservableProperty]
    private bool minimizeToTray = true;

    [ObservableProperty]
    private string theme = "Dark";

    [ObservableProperty]
    private string formatCommand = "";

    [ObservableProperty]
    private bool formatOnSave;

    [ObservableProperty]
    private string formatExtensions = "";

    [ObservableProperty]
    private string languageServersText = "";

    [ObservableProperty]
    private bool aiCompletionEnabled;

    [ObservableProperty]
    private string aiBaseUrl = "";

    [ObservableProperty]
    private string aiApiKey = "";

    [ObservableProperty]
    private string aiModel = "";

    [ObservableProperty]
    private bool aiAutomatic;

    [ObservableProperty]
    private bool aiDebugMode;

    [ObservableProperty]
    private bool checkForUpdatesOnStartup = true;

    [ObservableProperty]
    private string currentVersion = "0.0.0";

    public PreferencesViewModel(AppSettings settings, Func<Task>? checkUpdates = null)
    {
        _checkUpdates = checkUpdates;
        SelectedPage = Pages[0];
        LoadFrom(settings);
        CurrentVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public bool IsEditorPage => SelectedPage?.Id == "editor";
    public bool IsSavingPage => SelectedPage?.Id == "saving";
    public bool IsAppearancePage => SelectedPage?.Id == "appearance";
    public bool IsFormatterPage => SelectedPage?.Id == "formatter";
    public bool IsLspPage => SelectedPage?.Id == "lsp";
    public bool IsAiPage => SelectedPage?.Id == "ai";
    public bool IsShortcutsPage => SelectedPage?.Id == "shortcuts";
    public bool IsUpdatesPage => SelectedPage?.Id == "updates";

    /// <summary>Same binding as Shortcuts → Trigger AI completion; edited from the AI page too.</summary>
    public ShortcutBindingItem? AiTriggerShortcut =>
        ShortcutBindings.FirstOrDefault(b => b.Id == "TriggerAiCompletion");

    partial void OnSelectedPageChanged(PreferencesPage? value)
    {
        OnPropertyChanged(nameof(IsEditorPage));
        OnPropertyChanged(nameof(IsSavingPage));
        OnPropertyChanged(nameof(IsAppearancePage));
        OnPropertyChanged(nameof(IsFormatterPage));
        OnPropertyChanged(nameof(IsLspPage));
        OnPropertyChanged(nameof(IsAiPage));
        OnPropertyChanged(nameof(IsShortcutsPage));
        OnPropertyChanged(nameof(IsUpdatesPage));
    }

    public void LoadFrom(AppSettings settings)
    {
        FontSize = settings.FontSize;
        FontFamily = FontChoices.Contains(settings.FontFamily) ? settings.FontFamily : FontChoices[0];
        TabSize = settings.TabSize;
        InsertSpaces = settings.InsertSpaces;
        WordWrap = settings.WordWrap;
        ShowWhitespace = settings.ShowWhitespace;
        IndentGuides = settings.IndentGuides;
        WordCompletion = settings.WordCompletion;
        AutoCloseBrackets = settings.AutoCloseBrackets;
        AutoIndent = settings.AutoIndent;
        TrimTrailingWhitespaceOnSave = settings.TrimTrailingWhitespaceOnSave;
        EnsureFinalNewlineOnSave = settings.EnsureFinalNewlineOnSave;
        BackupOnSave = settings.BackupOnSave;
        AutosaveIntervalSeconds = settings.AutosaveIntervalSeconds;
        MinimizeToTray = settings.MinimizeToTray;
        Theme = ThemeChoices.Contains(settings.Theme) ? settings.Theme : "Dark";
        FormatCommand = settings.FormatCommand;
        FormatOnSave = settings.FormatOnSave;
        FormatExtensions = settings.FormatExtensions;
        LanguageServersText = FormatServers(settings.LanguageServers);
        AiCompletionEnabled = settings.AiCompletionEnabled;
        AiBaseUrl = settings.AiBaseUrl ?? "";
        AiApiKey = settings.AiApiKey ?? "";
        AiModel = settings.AiModel ?? "";
        AiAutomatic = settings.AiAutomatic;
        AiDebugMode = settings.AiDebugMode;
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        LoadShortcuts(settings.KeyBindings);
    }

    public AppSettings ToSettings(AppSettings original)
    {
        return new AppSettings
        {
            FontSize = FontSize,
            FontFamily = FontFamily,
            TabSize = TabSize,
            InsertSpaces = InsertSpaces,
            WordWrap = WordWrap,
            ShowWhitespace = ShowWhitespace,
            IndentGuides = IndentGuides,
            WordCompletion = WordCompletion,
            AutoCloseBrackets = AutoCloseBrackets,
            AutoIndent = AutoIndent,
            TrimTrailingWhitespaceOnSave = TrimTrailingWhitespaceOnSave,
            EnsureFinalNewlineOnSave = EnsureFinalNewlineOnSave,
            BackupOnSave = BackupOnSave,
            AutosaveIntervalSeconds = AutosaveIntervalSeconds,
            MinimizeToTray = MinimizeToTray,
            Theme = Theme,
            FormatCommand = FormatCommand.Trim(),
            FormatOnSave = FormatOnSave,
            FormatExtensions = FormatExtensions.Trim(),
            LanguageServers = ParseServers(LanguageServersText),
            AiCompletionEnabled = AiCompletionEnabled,
            AiBaseUrl = AiBaseUrl.Trim(),
            AiApiKey = AiApiKey.Trim(),
            AiModel = AiModel.Trim(),
            AiAutomatic = AiAutomatic,
            AiDebugMode = AiDebugMode,
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            LastUpdateCheckUtc = original.LastUpdateCheckUtc,
            RecentFiles = [.. original.RecentFiles],
            MaxRecentFiles = original.MaxRecentFiles,
            FavoriteFiles = [.. original.FavoriteFiles],
            MaxFavoriteFiles = original.MaxFavoriteFiles,
            LastWorkspacePath = original.LastWorkspacePath,
            WorkspaceExcludeNames = [.. original.WorkspaceExcludeNames],
            KeyBindings = BuildKeyBindingOverrides()
        };
    }

    [RelayCommand]
    private void ApplyKiloFreePreset()
    {
        AiCompletionEnabled = true;
        AiBaseUrl = KiloFreeBaseUrl;
        AiModel = KiloFreeModel; // kilo-auto/free — always free with a Gateway API key
        AiAutomatic = false;
    }

    [RelayCommand]
    private void ApplyOllamaPreset()
    {
        AiCompletionEnabled = true;
        AiBaseUrl = OllamaBaseUrl;
        if (string.IsNullOrWhiteSpace(AiModel) ||
            string.Equals(AiModel, KiloFreeModel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(AiModel, "kilo-auto/small", StringComparison.OrdinalIgnoreCase))
            AiModel = OllamaDefaultModel;
        AiAutomatic = true;
    }

    public string? ValidateShortcuts()
    {
        var error = KeyBindingCatalog.ValidateOverrides(BuildKeyBindingOverrides());
        ShortcutConflict = error ?? "";
        return error;
    }

    [RelayCommand]
    private void ResetAllShortcuts()
    {
        foreach (var item in ShortcutBindings)
            item.Reset();
        ShortcutConflict = "";
    }

    [RelayCommand]
    private void ApplyVsCodeShortcuts()
    {
        foreach (var item in ShortcutBindings)
        {
            if (KeyBindingCatalog.VsCodePreset.TryGetValue(item.Id, out var gesture))
                item.Gesture = gesture;
            else
                item.Reset();
        }

        ValidateShortcuts();
    }

    [RelayCommand]
    private void ResetShortcut(ShortcutBindingItem? item)
    {
        item?.Reset();
        ValidateShortcuts();
    }

    private void LoadShortcuts(Dictionary<string, string>? overrides)
    {
        ShortcutBindings.Clear();
        foreach (var def in KeyBindingCatalog.All)
        {
            var resolved = KeyBindingCatalog.ResolveGesture(def.Id, overrides);
            ShortcutBindings.Add(ShortcutBindingItem.FromDefinition(def, resolved));
        }

        ShortcutConflict = "";
        OnPropertyChanged(nameof(AiTriggerShortcut));
    }

    private Dictionary<string, string> BuildKeyBindingOverrides()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in ShortcutBindings)
        {
            var gesture = item.Gesture?.Trim() ?? "";
            if (gesture.Length == 0)
                continue;
            if (string.Equals(gesture, item.DefaultGesture, StringComparison.OrdinalIgnoreCase))
                continue;
            map[item.Id] = gesture;
        }

        return map;
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (_checkUpdates == null)
            return;
        await _checkUpdates();
    }

    private static string FormatServers(Dictionary<string, string> servers)
    {
        var sb = new StringBuilder();
        foreach (var pair in servers.OrderBy(p => p.Key))
            sb.AppendLine(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}");
        return sb.ToString();
    }

    private static Dictionary<string, string> ParseServers(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var idx = line.IndexOf('=');
            if (idx <= 0)
                continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
                result[key] = value;
        }

        return result;
    }
}
