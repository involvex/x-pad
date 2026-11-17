# X-Pad

Lightweight Notepad / Notepad++ / Atom-style editor for daily text editing.

Built with **.NET 10**, **Avalonia UI**, and **AvaloniaEdit** (TextMate highlighting).

## Highlights

- Tabs, dirty close prompts, session restore (files + caret)
- MIT-friendly TextMate syntax highlighting + dark / light / Monokai themes
- Find / replace, find in open tabs, bookmarks, bracket match, multi-occurrence edit
- Completions + optional stdio LSP
- Encoding / BOM / line endings preserved on save
- CLI open-or-create, single-instance file handoff, Send To / Open With helpers
- Backup on save (`.bak`), optional autosave

## Run

```bash
dotnet restore
dotnet build
dotnet run
dotnet test
pwsh scripts/smoke-cli.ps1
```

Open files from the shell:

```bash
dotnet run -- path\to\file.txt
dotnet run -- new-notes.txt    # empty buffer; Save creates the file
xpad --help
```

## Windows publish / Notepad-style integration

```powershell
pwsh scripts/publish-win-x64.ps1
pwsh scripts/install-open-with.ps1 -ExePath .\publish\win-x64\xpad.exe
```

This adds **Send to → X-Pad** and per-user **Open with** for common text/code types. It does **not** replace `notepad.exe` as the system default.

## Settings

Stored under `%APPDATA%\XPad\`:

- `settings.json` — editor prefs, LSP commands, recent files
- `session.json` — last open files + caret positions

## License

Project code is intended as a modern notepad replacement on Avalonia’s MIT stack; check third-party package licenses (Avalonia, TextMateSharp, etc.) for redistribution.
