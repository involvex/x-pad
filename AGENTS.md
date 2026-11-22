# AGENTS.md - x-pad

## Project Overview

x-pad is a modern cross-platform notepad built with Avalonia UI and AvaloniaEdit.
It targets a lightweight Notepad/Notepad++/Notepad4 replacement with tabs, syntax
highlighting (TextMate), completions, session restore, and optional LSP.

## Useful Commands

```bash
dotnet restore
dotnet build
dotnet run
dotnet test
dotnet run -c Release

# Publish (Windows)
pwsh scripts/publish-win-x64.ps1
pwsh scripts/publish-win-x64.ps1 -Mode selfcontained

# Optional Open-with / SendTo helper
pwsh scripts/install-open-with.ps1 -ExePath .\publish\win-x64\xpad.exe
```

## Technologies

- **.NET 10.0**
- **Avalonia UI 12.1.4** + Fluent theme
- **AvaloniaEdit 12** + **AvaloniaEdit.TextMate** / **TextMateSharp.Grammars**
- **CommunityToolkit.Mvvm 8.4.2**

## Architecture

- **Views/** — MainWindow, DocumentEditor (AvaloniaEdit host), dialogs
- **ViewModels/** — MainWindowViewModel, DocumentViewModel (`TextDocument` owned per tab)
- **Services/** — FileService (encoding/BOM/line endings), SessionService, SettingsService,
  EditorThemeService, SingleInstanceService, LanguageHelper, Lsp/*
- **Editor/** — completion helpers
- **Models/** — AppSettings, FileSnapshot, LineEndingKind

## Features

- Tabbed editing with dirty close prompts, tab × / middle-click / Ctrl+Tab / move tab / reopen closed (`Ctrl+Shift+T`)
- AvaloniaEdit: line numbers, TextMate highlighting, SearchPanel, folding (fold/unfold all), zoom, indent guides, font family prefs
- Status-bar language override (TextMate) + encoding / EOL cycle; external-change prompt on tab switch and window focus
- Bracket match/jump, word highlight, multi-occurrence edit, select line (`Ctrl+L`), auto-indent, line comments
- Bookmarks (`Ctrl+F2` / `F2` / `Shift+F2`), rectangular selection (Alt+drag)
- Command palette (`Ctrl+Shift+P`) for actions + recent files
- Quick Open (`Ctrl+E`) for open tabs + recent files
- Session restore with selected tab + caret line/column
- Case/sort/join/trim + remove empty/duplicate lines; insert spaces vs tabs pref; trim + final newline on save; optional `.bak` backup on save
- Find in open tabs (`Ctrl+Shift+F`)
- Optional autosave (prefs: interval seconds; 0 = off)
- Word completion (`Ctrl+Space`) + optional stdio LSP (completion / hover / diagnostics + squiggles)
- Encoding + BOM + CRLF/LF/CR preserved on save (status bar cycle)
- Duplicate/move line, show whitespace, print (Windows), always on top
- Session restore (`%APPDATA%/XPad/session.json`)
- Settings / Preferences (`%APPDATA%/XPad/settings.json`)
- Windows single-instance + named-pipe file handoff; CLI open-or-create paths (`xpad --help`)
- Copy full path / Reveal in Explorer; status bar shows file path
- Tools: SendTo shortcut + per-user Open With (common text/code types)
- Reload from disk (prompts when dirty)
- Themes: Dark / Light / System / Monokai (TextMate)
- Minimize to tray (pref) with Recent submenu; editor context menu (invert select, Search Web)
- Tab Open With (VS Code / Cursor / Windows Terminal); Favorites (File menu + tab context); Export as PDF; embedded ConPTY terminal (Ctrl+`)

## Daily-driver smoke checklist

```bash
pwsh scripts/smoke-cli.ps1
pwsh scripts/smoke-single-instance.ps1   # skips if xpad already running
dotnet run -- path\to\file.txt
dotnet run -- missing-new.txt          # empty buffer, Save creates file
pwsh scripts/install-open-with.ps1 -ExePath .\publish\win-x64\xpad.exe
```

Manual: tabs + dirty close, dark theme, syntax highlight, Find/Replace, completions,
bookmarks, `Ctrl+/` comment, Save trim, Open with → X-Pad.

## Best Practices

- Keep `TextDocument` as the source of truth — do not two-way bind large strings
- Dirty state via `UndoStack.IsOriginalFile` / `MarkAsOriginalFile()`
- File I/O goes through `FileService` so encoding round-trips stay correct
- LSP servers are optional and configured in settings; missing servers are ignored
- Exclude `x-pad.Tests/**` from the WinExe project globs (`DefaultItemExcludes`)
- UI exceptions are logged to `%APPDATA%/XPad/crash.log` and handled so the process stays up
