using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using x_pad.Models;

namespace x_pad.Views;

public partial class PreferencesWindow : Window
{
    private static readonly string[] FontChoices =
    [
        "Cascadia Code, JetBrains Mono, Consolas, Courier New, monospace",
        "Consolas, Courier New, monospace",
        "Courier New, monospace",
        "JetBrains Mono, Consolas, monospace",
        "Segoe UI, sans-serif"
    ];

    private readonly AppSettings _original;
    public AppSettings? Result { get; private set; }

    public PreferencesWindow() : this(new AppSettings())
    {
    }

    public PreferencesWindow(AppSettings settings)
    {
        _original = settings;
        InitializeComponent();

        var font = this.FindControl<NumericUpDown>("FontSizeBox");
        var fontFamily = this.FindControl<ComboBox>("FontFamilyBox");
        var tab = this.FindControl<NumericUpDown>("TabSizeBox");
        var insertSpaces = this.FindControl<CheckBox>("InsertSpacesBox");
        var completion = this.FindControl<CheckBox>("WordCompletionBox");
        var brackets = this.FindControl<CheckBox>("AutoBracketsBox");
        var autoIndent = this.FindControl<CheckBox>("AutoIndentBox");
        var guides = this.FindControl<CheckBox>("IndentGuidesBox");
        var whitespace = this.FindControl<CheckBox>("WhitespaceBox");
        var trimOnSave = this.FindControl<CheckBox>("TrimOnSaveBox");
        var finalNewline = this.FindControl<CheckBox>("FinalNewlineBox");
        var backupOnSave = this.FindControl<CheckBox>("BackupOnSaveBox");
        var autosave = this.FindControl<NumericUpDown>("AutosaveBox");
        var lsp = this.FindControl<TextBox>("LspBox");

        if (font != null) font.Value = (decimal)settings.FontSize;
        if (fontFamily != null)
        {
            fontFamily.ItemsSource = FontChoices;
            fontFamily.SelectedItem = FontChoices.Contains(settings.FontFamily)
                ? settings.FontFamily
                : FontChoices[0];
        }
        if (tab != null) tab.Value = settings.TabSize;
        if (insertSpaces != null) insertSpaces.IsChecked = settings.InsertSpaces;
        if (completion != null) completion.IsChecked = settings.WordCompletion;
        if (brackets != null) brackets.IsChecked = settings.AutoCloseBrackets;
        if (autoIndent != null) autoIndent.IsChecked = settings.AutoIndent;
        if (guides != null) guides.IsChecked = settings.IndentGuides;
        if (whitespace != null) whitespace.IsChecked = settings.ShowWhitespace;
        if (trimOnSave != null) trimOnSave.IsChecked = settings.TrimTrailingWhitespaceOnSave;
        if (finalNewline != null) finalNewline.IsChecked = settings.EnsureFinalNewlineOnSave;
        if (backupOnSave != null) backupOnSave.IsChecked = settings.BackupOnSave;
        if (autosave != null) autosave.Value = settings.AutosaveIntervalSeconds;
        if (lsp != null) lsp.Text = FormatServers(settings.LanguageServers);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var font = this.FindControl<NumericUpDown>("FontSizeBox");
        var fontFamily = this.FindControl<ComboBox>("FontFamilyBox");
        var tab = this.FindControl<NumericUpDown>("TabSizeBox");
        var insertSpaces = this.FindControl<CheckBox>("InsertSpacesBox");
        var completion = this.FindControl<CheckBox>("WordCompletionBox");
        var brackets = this.FindControl<CheckBox>("AutoBracketsBox");
        var autoIndent = this.FindControl<CheckBox>("AutoIndentBox");
        var guides = this.FindControl<CheckBox>("IndentGuidesBox");
        var whitespace = this.FindControl<CheckBox>("WhitespaceBox");
        var trimOnSave = this.FindControl<CheckBox>("TrimOnSaveBox");
        var finalNewline = this.FindControl<CheckBox>("FinalNewlineBox");
        var backupOnSave = this.FindControl<CheckBox>("BackupOnSaveBox");
        var autosave = this.FindControl<NumericUpDown>("AutosaveBox");
        var lsp = this.FindControl<TextBox>("LspBox");

        Result = new AppSettings
        {
            FontSize = (double)(font?.Value ?? 13),
            FontFamily = fontFamily?.SelectedItem as string
                         ?? FontChoices[0],
            TabSize = (int)(tab?.Value ?? 4),
            InsertSpaces = insertSpaces?.IsChecked != false,
            WordCompletion = completion?.IsChecked == true,
            AutoCloseBrackets = brackets?.IsChecked == true,
            AutoIndent = autoIndent?.IsChecked == true,
            IndentGuides = guides?.IsChecked == true,
            ShowWhitespace = whitespace?.IsChecked == true,
            TrimTrailingWhitespaceOnSave = trimOnSave?.IsChecked == true,
            EnsureFinalNewlineOnSave = finalNewline?.IsChecked == true,
            BackupOnSave = backupOnSave?.IsChecked == true,
            AutosaveIntervalSeconds = (int)(autosave?.Value ?? 0),
            WordWrap = _original.WordWrap,
            Theme = _original.Theme,
            RecentFiles = [.. _original.RecentFiles],
            MaxRecentFiles = _original.MaxRecentFiles,
            LanguageServers = ParseServers(lsp?.Text)
        };
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private static string FormatServers(Dictionary<string, string> servers)
    {
        var sb = new StringBuilder();
        foreach (var pair in servers.OrderBy(p => p.Key))
            sb.AppendLine(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}");
        return sb.ToString();
    }

    private static Dictionary<string, string> ParseServers(string? text)
    {
        var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
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
