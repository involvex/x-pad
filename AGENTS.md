# AGENTS.md - x-pad

## Project Overview

x-pad is a modern cross-platform notepad built with Avalonia UI and AvaloniaEdit.
It targets a lightweight Notepad/Notepad++/Notepad4 replacement with tabs, syntax
highlighting (TextMate), completions, session restore, and optional LSP. See
[README.md](README.md) for the user-facing overview and feature highlights.

## Useful Commands

```bash
# Restore, build, run, test
dotnet restore
dotnet build
dotnet run
dotnet test
dotnet run -c Release

# Open an existing file or create an empty buffer (Save creates the file)
dotnet run -- path\to\file.txt
dotnet run -- missing-new.txt
xpad --help

# Global xpad via Bun link → bin/Release/net10.0/xpad.exe
dotnet build -c Release
bun link
xpad path\to\file.txt

# Publish (Windows x64) — framework-dependent to publish/win-x64
pwsh scripts/publish-win-x64.ps1
# Self-contained single-file to publish/win-x64-sc
pwsh scripts/publish-win-x64.ps1 -Mode selfcontained

# Optional Open-with / SendTo helper
pwsh scripts/install-open-with.ps1 -ExePath .\publish\win-x64\xpad.exe

# Microsoft Store MSIX (self-contained win-x64 folder layout → .msix)
# Needs Windows SDK (makeappx). Defaults to XPad.Dev for local -Install sideload.
pwsh scripts/publish-msix.ps1 -Install
pwsh scripts/publish-msix.ps1 `
  -PackageName "<Partner Center identity name>" `
  -Publisher "<CN=...>" `
  -PublisherDisplayName "<publisher display name>"

# Cut a release (patch bump default): bump csproj/package.json, CHANGELOG, tag, push
# Tag push triggers .github/workflows/release.yml (artifacts + GitHub Release).
# When PARTNER_CENTER_TENANT_ID / SELLER_ID / CLIENT_ID / CLIENT_SECRET are set,
# the same workflow also packs an unsigned Store MSIX and submits product 9P88NMM8F1WL.
# Missing any of those secrets skips the Store submit steps.
pwsh scripts/release.ps1
pwsh scripts/release.ps1 -Bump minor
pwsh scripts/release.ps1 -Version 1.0.0
pwsh scripts/release.ps1 -DryRun
```

PowerShell scripts live in `scripts/`: `publish-win-x64.ps1`,
`publish-msix.ps1`, `install-open-with.ps1`, `smoke-cli.ps1`,
`smoke-single-instance.ps1`, `smoke-workspace.ps1`, `release.ps1`,
`xpad.js` (Bun/npm bin launcher). MSIX manifest template:
`packaging/windows/AppxManifest.xml`.

## Technologies

- **.NET 10.0** (`net10.0`), C# with nullable reference types enabled
- **Avalonia UI 12.1.4** — Desktop, Fluent theme, Inter fonts
- **AvaloniaEdit 12** (`Avalonia.AvaloniaEdit` 12.0.0) + `AvaloniaEdit.TextMate` 12.0.0
- **TextMateSharp.Grammars 2.0.3** — bundled syntax grammars
- **CommunityToolkit.Mvvm 8.4.2** — MVVM helpers
- **Iciclecreek.Avalonia.Terminal 4.1.4** — embedded ConPTY terminal
- **QuestPDF 2026.9.2** — PDF export
- **System.Drawing.Common 9.0.4** — printing (Windows)
- **AvaloniaUI.DiagnosticsSupport 2.2.3** — Debug builds only
- **Tests:** xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4 (in `x-pad.Tests/`)

## Architecture

- **Views/** — MainWindow, DocumentEditor (AvaloniaEdit host), FileTreePanel, Preferences
  (sidebar pages), Find in Open Tabs / Find in Folder dialogs
- **ViewModels/** — MainWindowViewModel, DocumentViewModel (`TextDocument` owned per tab),
  PreferencesViewModel, FileTreeViewModel
- **Services/** — FileService (encoding/BOM/line endings + re-decode), SessionService,
  SettingsService, RecoveryService, OpenFileWatcher, TabSearch, DiskSearch / DiskReplace,
  WorkspaceService, LargeFilePolicy, FormatService / FormatCommandHelper,
  EditorThemeService, SingleInstanceService, UpdateService (GitHub Releases),
  LanguageHelper, Lsp/*
- **Editor/** — completion helpers, IndentStyle detection
- **Models/** — AppSettings, FileSnapshot, LineEndingKind

## Features

- Tabbed editing with dirty close prompts, tab × / middle-click / Ctrl+Tab / move tab / reopen closed (`Ctrl+Shift+T`)
- AvaloniaEdit: line numbers, TextMate highlighting, SearchPanel, folding (fold/unfold all), zoom, indent guides, font family prefs
- Status-bar language override (TextMate) + encoding / EOL / Tabs↔Spaces cycle; external-change prompt on focus, tab switch, and file watcher
- Per-buffer indent: detect tabs vs spaces from leading whitespace; Tab + auto-indent follow the buffer (prefs default for new/empty)
- Per-tab word wrap (`Alt+Z`); Preferences Word Wrap is the default for new tabs only
- Bracket match/jump, word highlight, multi-occurrence edit, select line (`Ctrl+L`), auto-indent, line/block comments (follow language override; HTML/XML `<!-- -->`)
- Bookmarks (`Ctrl+F2` / `F2` / `Shift+F2`), rectangular selection (Alt+drag)
- Command palette (`Ctrl+Shift+P`) for actions + recent files
- Quick Open (`Ctrl+E`) for open tabs + recent files
- Session restore with selected tab + caret line/column
- Crash recovery for dirty/untitled buffers (`%APPDATA%/XPad/recovery/`); clean exit clears snapshots
- Case/sort/join/trim + remove empty/duplicate lines; insert spaces vs tabs pref; trim + final newline on save; optional `.bak` backup on save
- External formatter (`FormatCommand` with `$FILE`, format on save / `Shift+Alt+F` Format Document, optional extension filter)
- Find / replace in open tabs (`Ctrl+Shift+F`): match case, whole word, regex, replace / replace all (500-hit cap)
- Open Folder (`Ctrl+Shift+O`) + explorer sidebar; find/replace in folder (`Ctrl+Shift+H`); excludes `.git` / `node_modules` / `bin` / `obj` / `.vs`; LSP `rootUri` follows the workspace folder
- Optional autosave (prefs: interval seconds; 0 = off)
- Word completion (`Ctrl+Space`) + optional stdio LSP (completion / hover / diagnostics + squiggles; F12 definition; Shift+F12 references; Ctrl+Shift+. outline; Ctrl+. code actions with WorkspaceEdit text edits)
- Encoding + BOM + CRLF/LF/CR preserved on save; re-decode from kept source bytes (UTF-8 / BOM / UTF-16 LE+BE / Latin-1)
- Read-only mode (from file attributes + File menu toggle); status-bar `RO` indicator
- Large-file guard (above 8 MB): plain open, skip folding / word completion / full-text LSP
- Duplicate/move line, show whitespace, print (Windows), always on top
- Session restore (`%APPDATA%/XPad/session.json`)
- Settings / Preferences sidebar (`%APPDATA%/XPad/settings.json`) — Editor / Saving / Appearance / Formatter / LSP / Updates
- View menu: checkable toggles, grouped Split / Zoom / Tabs / Folding / Bookmarks / Theme; Encoding under File; Edit → Transform submenu
- GitHub Releases updater (`Help → Check for Updates`, optional startup check); publish writes `xpad.install.json` for asset matching; MSIX/Store installs skip GitHub updates (`PackageIdentity`)
- Windows single-instance + named-pipe file handoff; CLI open-or-create paths (`xpad --help`)
- `package.json` + `scripts/xpad.js` for `bun link` → Release `xpad.exe`
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
pwsh scripts/smoke-workspace.ps1         # Open Folder / disk search unit smoke
dotnet run -- path\to\file.txt
dotnet run -- missing-new.txt            # empty buffer, Save creates file
dotnet run -- path\to\folder             # open workspace folder
pwsh scripts/install-open-with.ps1 -ExePath .\publish\win-x64\xpad.exe
```

Manual: tabs + dirty close, dark theme, syntax highlight, Find/Replace, find in open tabs
(replace), Open Folder + Find in Folder, encoding re-decode, read-only, completions, bookmarks,
`Ctrl+/` comment, Save trim, crash recovery after kill, Open with → X-Pad.

## Best Practices

- Keep `TextDocument` as the source of truth — do not two-way bind large strings.
- Track dirty state via `UndoStack.IsOriginalFile` / `MarkAsOriginalFile()`.
- Route all file I/O through `FileService` so encoding round-trips stay correct.
- Treat LSP servers as optional and configured in settings; missing servers are ignored.
- Keep `x-pad.Tests/**` excluded from the WinExe project globs (`DefaultItemExcludes`).
- Log UI exceptions to `%APPDATA%/XPad/crash.log` and handle them so the process stays up.
- Keep unit tests in `x-pad.Tests/` (xUnit) and run `dotnet test` before considering a change complete.

## Guidelines

- Use PowerShell-compatible commands on Windows (`Get-ChildItem`, `Remove-Item`, `Copy-Item`,
  `Move-Item`); write files with tools rather than shell redirection when content has special characters.
- Preserve the MVVM split: views in `Views/`, state and commands in `ViewModels/`, reusable
  logic in `Services/`, small helpers in `Editor/`, and data types in `Models/`.
- Keep user data under `%APPDATA%/XPad/` (`settings.json`, `session.json`, `recovery/`); do not
  hardcode per-user paths.
- Keep errors non-fatal for the user: degrade gracefully (e.g., missing LSP servers) and log
  unexpected UI failures instead of terminating the process.
- Preserve the existing Dark / Light / System / Monokai theme support when changing the UI,
  and prefer a dark appearance as the default.
- Verify changes with `dotnet test` and the daily-driver smoke checklist above before calling
  work complete.
- Keep this file and `README.md` in sync when scripts, dependencies, or workflows change.
