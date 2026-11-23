# X-Pad

Lightweight Notepad / Notepad++ / Atom-style editor for daily text editing.

Built with **.NET 10**, **Avalonia UI**, and **AvaloniaEdit** (TextMate highlighting).

**Documentation:** [involvex.github.io/x-pad](https://involvex.github.io/x-pad/) — preview locally by opening [`site/index.html`](site/index.html).

## Highlights

- Tabs, dirty close prompts, session restore (files + caret)
- MIT-friendly TextMate syntax highlighting + Dark / Light / System / Monokai / Hacker / AMOLED / GitHub themes
- VS Code-style shortcuts by default (`Ctrl+P` Quick Open, `Ctrl+D` multi-cursor, `Ctrl+B` Explorer; Preferences → Shortcuts → VS Code)
- Find / replace, find/replace in open tabs (regex, whole word), bookmarks, bracket match, multi-occurrence edit
- Completions + optional stdio LSP (hover, F12 definition, Shift+F12 references, outline, Ctrl+. code actions / text edits)
- Encoding / BOM / line endings preserved on save; re-decode from disk bytes (incl. UTF-16 BE)
- Read-only buffers, crash recovery for dirty/untitled tabs, live external-change watching
- Large-file guard (8 MB+): open as plain text without folding / word completion / LSP
- Per-tab word wrap; per-buffer Tabs / Spaces (detected from the file; status-bar toggle)
- Open Folder / file tree explorer; find/replace in folder (`Ctrl+Shift+H`)
- CLI open-or-create (files) or open folder; single-instance file handoff, Send To / Open With helpers
- Backup on save (`.bak`), optional autosave
- Optional external formatter (`$FILE` command, format on save)

Settings and recovery live under `%APPDATA%\XPad\` (`settings.json`, `session.json`, `layout.json`, `recovery/`). The main window uses a dockable layout (explorer, editor tabs, terminal); splitter positions and tool visibility are saved to `layout.json` on exit. Open document tabs come from `session.json` / your session, not from the dock layout file.

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

## Microsoft Store (MSIX)

Create an **MSIX- oder PWA-App** product in Partner Center (not EXE/MSI). Copy **Product identity** values (package name, `CN=…` publisher, publisher display name), then pack:

```powershell
# Local sideload (default XPad.Dev identity)
pwsh scripts/publish-msix.ps1 -Install

# Store upload — use identity from Partner Center
pwsh scripts/publish-msix.ps1 `
  -PackageName "<identity name>" `
  -Publisher "<CN=... from Partner Center>" `
  -PublisherDisplayName "<publisher display name>"
```

Requires the Windows 10/11 SDK (`makeappx` / `signtool`). Output: `publish/xpad-<version>-win-x64.msix`. The Store re-signs the package; do not attach the MSIX to GitHub Releases (zip releases stay unchanged). A Store/MSIX install skips the GitHub updater and is updated by the Store. Listing still needs screenshots, 1:1 box art, a privacy-policy URL ([site/privacy.html](https://involvex.github.io/x-pad/privacy.html)), and the IARC age rating.

A `v*` tag (or a manual run of the [release workflow](.github/workflows/release.yml)) also packs that MSIX and submits product **9P88NMM8F1WL** via the Microsoft Store Developer CLI when these repository secrets are set: `PARTNER_CENTER_TENANT_ID`, `PARTNER_CENTER_SELLER_ID`, `PARTNER_CENTER_CLIENT_ID`, `PARTNER_CENTER_CLIENT_SECRET`. If any secret is missing, Store submit steps are skipped; zip GitHub Releases are unchanged. An in-progress submission started in the Partner Center UI blocks the release Store job until that draft is deleted in the dashboard (the submission API cannot remove portal-created drafts).

## Cut a release

```powershell
pwsh scripts/release.ps1              # patch bump (default)
pwsh scripts/release.ps1 -Bump minor
pwsh scripts/release.ps1 -Version 1.0.0
pwsh scripts/release.ps1 -DryRun
```

Updates `<Version>` in `x-pad.csproj` (About panel), `package.json`, and `CHANGELOG.md`, then commits, tags `vX.Y.Z`, and pushes. The [release workflow](.github/workflows/release.yml) builds the zips, creates the GitHub Release for that tag, and (when the Partner Center secrets above are set) submits the Store MSIX.

## Settings

Stored under `%APPDATA%\XPad\`:

- `settings.json` — editor prefs, LSP commands, recent files, update prefs
- `session.json` — last open files + caret positions
- `layout.json` — dock splitter positions and tool panel state (not open file tabs)
- `recovery/` — dirty/untitled buffer snapshots (cleared on clean exit)

Preferences (`Ctrl+,`) use a sidebar: Editor, Saving, Appearance, Formatter, Language servers, Shortcuts, Updates. Shortcut overrides are stored in `settings.json` (`KeyBindings`); blank/missing entries keep the built-in defaults.

## Updates

**Help → Check for Updates** (and Preferences → Updates) query [GitHub Releases](https://github.com/involvex/x-pad/releases/latest). When a newer version is available you can update in place from a published install, or open the release page from a `dotnet run` / `bin\` build. Microsoft Store / MSIX installs skip this path and show that the Store handles updates.

`pwsh scripts/publish-win-x64.ps1` writes `xpad.install.json` into the publish folder so the updater downloads the matching zip (`xpad-win-x64.zip` or `xpad-win-x64-selfcontained.zip`). Startup update checks are on by default (at most once every 12 hours) and never download until you confirm.

## License

Project code is intended as a modern notepad replacement on Avalonia’s MIT stack; check third-party package licenses (Avalonia, TextMateSharp, etc.) for redistribution.
