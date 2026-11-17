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

- Tabbed editing with dirty close prompts
- AvaloniaEdit: line numbers, TextMate highlighting, SearchPanel, folding, zoom
- Word completion + optional stdio LSP (completion / hover / diagnostics)
- Encoding + BOM + CRLF/LF/CR preserved on save
- Session restore (`%APPDATA%/XPad/session.json`)
- Settings (`%APPDATA%/XPad/settings.json`)
- Windows single-instance + named-pipe file handoff
- Themes: Dark / Light / System / Monokai (TextMate)

## Best Practices

- Keep `TextDocument` as the source of truth — do not two-way bind large strings
- Dirty state via `UndoStack.IsOriginalFile` / `MarkAsOriginalFile()`
- File I/O goes through `FileService` so encoding round-trips stay correct
- LSP servers are optional and configured in settings; missing servers are ignored
- Exclude `x-pad.Tests/**` from the WinExe project globs (`DefaultItemExcludes`)
