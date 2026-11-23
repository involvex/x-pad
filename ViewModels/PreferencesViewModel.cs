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

    public static readonly string[] ThemeChoices = ["Dark", "Light", "System", "Monokai"];

    public ObservableCollection<PreferencesPage> Pages { get; } =
    [
        new() { Id = "editor", Title = "Editor" },
        new() { Id = "saving", Title = "Saving" },
        new() { Id = "appearance", Title = "Appearance" },
        new() { Id = "formatter", Title = "Formatter" },
        new() { Id = "lsp", Title = "Language servers" },
        new() { Id = "shortcuts", Title = "Shortcuts" },
        new() { Id = "updates", Title = "Updates" }
    ];

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
    public bool IsShortcutsPage => SelectedPage?.Id == "shortcuts";
    public bool IsUpdatesPage => SelectedPage?.Id == "updates";

    partial void OnSelectedPageChanged(PreferencesPage? value)
    {
        OnPropertyChanged(nameof(IsEditorPage));
        OnPropertyChanged(nameof(IsSavingPage));
        OnPropertyChanged(nameof(IsAppearancePage));
        OnPropertyChanged(nameof(IsFormatterPage));
        OnPropertyChanged(nameof(IsLspPage));
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
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            LastUpdateCheckUtc = original.LastUpdateCheckUtc,
            RecentFiles = [.. original.RecentFiles],
            MaxRecentFiles = original.MaxRecentFiles,
            FavoriteFiles = [.. original.FavoriteFiles],
            MaxFavoriteFiles = original.MaxFavoriteFiles,
            KeyBindings = BuildKeyBindingOverrides()
        };
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
