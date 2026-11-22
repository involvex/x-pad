# X-Pad

Lightweight Notepad / Notepad++ / Atom-style editor for daily text editing.

Built with **.NET 10**, **Avalonia UI**, and **AvaloniaEdit** (TextMate highlighting).

## Highlights

- Tabs, dirty close prompts, session restore (files + caret)
- MIT-friendly TextMate syntax highlighting + dark / light / Monokai themes
- Find / replace, find/replace in open tabs (regex, whole word), bookmarks, bracket match, multi-occurrence edit
- Completions + optional stdio LSP
- Encoding / BOM / line endings preserved on save; re-decode from disk bytes (incl. UTF-16 BE)
- Read-only buffers, crash recovery for dirty/untitled tabs, live external-change watching
- Large-file guard (8 MB+): open as plain text without folding / word completion / LSP
- Per-tab word wrap; per-buffer Tabs / Spaces (detected from the file; status-bar toggle)
- Open Folder / file tree explorer; find/replace in folder (`Ctrl+Shift+H`)
- CLI open-or-create (files) or open folder; single-instance file handoff, Send To / Open With helpers
- Backup on save (`.bak`), optional autosave
- Optional external formatter (`$FILE` command, format on save)

Settings and recovery live under `%APPDATA%\XPad\` (`settings.json`, `session.json`, `recovery/`).

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

### Global `xpad` via Bun link

Point a global `xpad` command at the Release build of this checkout:

```bash
dotnet build -c Release
bun link
xpad path\to\file.txt
```

`scripts/xpad.js` launches `bin/Release/net10.0/xpad.exe`. Rebuild with `dotnet build -c Release` after code changes. A second launch still hands files to the running instance.

## Windows publish / Notepad-style integration

```powershell
pwsh scripts/publish-win-x64.ps1
pwsh scripts/install-open-with.ps1 -ExePath .\publish\win-x64\xpad.exe
```

This adds **Send to → X-Pad** and per-user **Open with** for common text/code types. It does **not** replace `notepad.exe` as the system default.

## Cut a release

```powershell
pwsh scripts/release.ps1              # patch bump (default)
pwsh scripts/release.ps1 -Bump minor
pwsh scripts/release.ps1 -Version 1.0.0
pwsh scripts/release.ps1 -DryRun
```

Updates `<Version>` in `x-pad.csproj` (About panel), `package.json`, and `CHANGELOG.md`, then commits, tags `vX.Y.Z`, and pushes. The [release workflow](.github/workflows/release.yml) builds the zips and creates the GitHub Release for that tag.

## Settings

Stored under `%APPDATA%\XPad\`:

- `settings.json` — editor prefs, LSP commands, recent files, update prefs
- `session.json` — last open files + caret positions
- `recovery/` — dirty/untitled buffer snapshots (cleared on clean exit)

Preferences (`Ctrl+,`) use a sidebar: Editor, Saving, Appearance, Formatter, Language servers, Updates.

## Updates

**Help → Check for Updates** (and Preferences → Updates) query [GitHub Releases](https://github.com/involvex/x-pad/releases/latest). When a newer version is available you can update in place from a published install, or open the release page from a `dotnet run` / `bin\` build.

`pwsh scripts/publish-win-x64.ps1` writes `xpad.install.json` into the publish folder so the updater downloads the matching zip (`xpad-win-x64.zip` or `xpad-win-x64-selfcontained.zip`). Startup update checks are on by default (at most once every 12 hours) and never download until you confirm.

## License

Project code is intended as a modern notepad replacement on Avalonia’s MIT stack; check third-party package licenses (Avalonia, TextMateSharp, etc.) for redistribution.
