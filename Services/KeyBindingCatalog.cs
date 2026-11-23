using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace x_pad.Services;

public sealed record KeyBindingDefinition(
    string Id,
    string Title,
    string DefaultGesture,
    string CommandName,
    string? AlternateGesture = null);

public static class KeyBindingCatalog
{
    public static IReadOnlyList<KeyBindingDefinition> All { get; } =
    [
        new("NextTab", "Next tab", "Ctrl+Tab", "NextTabCommand"),
        new("PreviousTab", "Previous tab", "Ctrl+Shift+Tab", "PreviousTabCommand"),
        new("MoveTabLeft", "Move tab left", "Ctrl+Shift+PageUp", "MoveTabLeftCommand"),
        new("MoveTabRight", "Move tab right", "Ctrl+Shift+PageDown", "MoveTabRightCommand"),
        new("NewTab", "New tab", "Ctrl+N", "NewTabCommand"),
        new("OpenFile", "Open file", "Ctrl+O", "OpenFileCommand"),
        new("OpenFolder", "Open folder", "Ctrl+Shift+O", "OpenFolderCommand"),
        new("SaveFile", "Save", "Ctrl+S", "SaveFileCommand"),
        new("SaveFileAs", "Save as", "Ctrl+Shift+S", "SaveFileAsCommand"),
        new("CloseTab", "Close tab", "Ctrl+W", "CloseTabCommand"),
        new("ReopenClosedTab", "Reopen closed tab", "Ctrl+Shift+T", "ReopenClosedTabCommand"),
        new("JoinLines", "Join lines", "Ctrl+J", "JoinLinesCommand"),
        new("Find", "Find", "Ctrl+F", "FindCommand"),
        new("Replace", "Replace", "Ctrl+H", "ReplaceCommand"),
        new("FindNext", "Find next", "F3", "FindNextCommand"),
        new("FindPrevious", "Find previous", "Shift+F3", "FindPreviousCommand"),
        new("FindInOpenTabs", "Find in open tabs", "Ctrl+Shift+F", "FindInOpenTabsCommand"),
        new("FindInFolder", "Find in folder", "Ctrl+Shift+H", "FindInFolderCommand"),
        new("GoToLine", "Go to line", "Ctrl+G", "GoToLineCommand"),
        new("GoToDefinition", "Go to definition", "F12", "GoToDefinitionCommand"),
        new("FindReferences", "Find references", "Shift+F12", "FindReferencesCommand"),
        new("ShowDocumentOutline", "Document outline", "Ctrl+Shift+OemPeriod", "ShowDocumentOutlineCommand"),
        new("ShowCodeActions", "Code actions", "Ctrl+OemPeriod", "ShowCodeActionsCommand"),
        new("InsertDateTime", "Insert date/time", "F5", "InsertDateTimeCommand"),
        new("ToggleWordWrap", "Toggle word wrap", "Alt+Z", "ToggleWordWrapCommand"),
        new("OpenPreferences", "Preferences", "Ctrl+OemComma", "OpenPreferencesCommand"),
        new("Print", "Print", "Ctrl+P", "PrintCommand"),
        new("ToggleTerminal", "Toggle terminal", "Ctrl+OemTilde", "ToggleTerminalCommand"),
        new("OpenCommandPalette", "Command palette", "Ctrl+Shift+P", "OpenCommandPaletteCommand"),
        new("OpenQuickOpen", "Quick open", "Ctrl+E", "OpenQuickOpenCommand"),
        new("DuplicateLine", "Duplicate line", "Ctrl+D", "DuplicateLineCommand"),
        new("MoveLineUp", "Move line up", "Alt+Up", "MoveLineUpCommand"),
        new("MoveLineDown", "Move line down", "Alt+Down", "MoveLineDownCommand"),
        new("JumpToMatchingBracket", "Jump to matching bracket", "Ctrl+M", "JumpToMatchingBracketCommand"),
        new("AddNextOccurrence", "Add next occurrence", "Ctrl+Alt+Right", "AddNextOccurrenceCommand"),
        new("SelectAllOccurrences", "Select all occurrences", "Ctrl+Shift+L", "SelectAllOccurrencesCommand"),
        new("SelectCurrentLine", "Select current line", "Ctrl+L", "SelectCurrentLineCommand"),
        new("TriggerCompletions", "Trigger completions", "Ctrl+Space", "TriggerCompletionsCommand"),
        new("ToggleLineComment", "Toggle line comment", "Ctrl+OemQuestion", "ToggleLineCommentCommand", "Ctrl+Divide"),
        new("FormatDocument", "Format document", "Shift+Alt+F", "FormatDocumentCommand"),
        new("UpperCase", "Upper case", "Ctrl+U", "UpperCaseCommand"),
        new("LowerCase", "Lower case", "Ctrl+Shift+U", "LowerCaseCommand"),
        new("ToggleBookmark", "Toggle bookmark", "Ctrl+F2", "ToggleBookmarkCommand"),
        new("NextBookmark", "Next bookmark", "F2", "NextBookmarkCommand"),
        new("PreviousBookmark", "Previous bookmark", "Shift+F2", "PreviousBookmarkCommand"),
        new("ZoomIn", "Zoom in", "Ctrl+OemPlus", "ZoomInCommand", "Ctrl+Add"),
        new("ZoomOut", "Zoom out", "Ctrl+OemMinus", "ZoomOutCommand", "Ctrl+Subtract"),
        new("ZoomReset", "Zoom reset", "Ctrl+D0", "ZoomResetCommand", "Ctrl+NumPad0"),
    ];

    public static string ResolveGesture(string id, IReadOnlyDictionary<string, string>? overrides)
    {
        var def = All.FirstOrDefault(d => d.Id == id)
                  ?? throw new ArgumentException($"Unknown key binding id '{id}'", nameof(id));
        if (overrides != null &&
            overrides.TryGetValue(id, out var over) &&
            !string.IsNullOrWhiteSpace(over))
        {
            return over.Trim();
        }

        return def.DefaultGesture;
    }

    public static bool TryParseGesture(string? text, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            gesture = KeyGesture.Parse(text.Trim());
            return gesture != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns null when the override map is valid; otherwise a human-readable conflict/error.
    /// Empty override values mean "use default".
    /// </summary>
    public static string? ValidateOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        overrides ??= new Dictionary<string, string>();
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in All)
        {
            var primary = ResolveGesture(def.Id, overrides);
            if (!TryParseGesture(primary, out _))
                return $"Invalid gesture for {def.Title}: '{primary}'";

            var key = NormalizeGestureKey(primary);
            if (used.TryGetValue(key, out var owner))
                return $"Gesture '{primary}' is used by both {owner} and {def.Title}";
            used[key] = def.Title;

            // Alternates only apply when the command still uses its default primary.
            if (!string.IsNullOrWhiteSpace(def.AlternateGesture) &&
                string.Equals(primary, def.DefaultGesture, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseGesture(def.AlternateGesture, out _))
                    return $"Invalid alternate gesture for {def.Title}: '{def.AlternateGesture}'";

                var altKey = NormalizeGestureKey(def.AlternateGesture);
                if (used.TryGetValue(altKey, out var altOwner))
                    return $"Gesture '{def.AlternateGesture}' is used by both {altOwner} and {def.Title}";
                used[altKey] = def.Title;
            }
        }

        foreach (var key in overrides.Keys)
        {
            if (All.All(d => d.Id != key))
                return $"Unknown key binding id '{key}'";
        }

        return null;
    }

    public static string NormalizeGestureKey(string gesture) =>
        string.Join("+", gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant()));

    public static string FormatGestureForDisplay(string gesture)
    {
        // Prefer readable labels in Preferences / menus.
        return gesture
            .Replace("OemComma", ",", StringComparison.OrdinalIgnoreCase)
            .Replace("OemPeriod", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("OemQuestion", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("OemTilde", "`", StringComparison.OrdinalIgnoreCase)
            .Replace("OemPlus", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("OemMinus", "-", StringComparison.OrdinalIgnoreCase);
    }
}
