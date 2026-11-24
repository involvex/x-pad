# Changelog


## 0.5.6 - 2026-11-24

- feat(ui): fix favorites, explorer home, menus, and unsaved session restore
## Unreleased

- fix(ui): Favorites menu Add/Open, tab context actions, Explorer home/last folder
- feat(ui): split Edit into Edit / Selection / Find; double-click tab strip for new tab
- feat(session): restore unsaved buffers on the next launch

## 0.5.5 - 2026-11-24

- feat(ui): add Hacker/AMOLED/GitHub themes and VS Code shortcuts

## 0.5.4 - 2026-11-23

- fix(ui): restore dock explorer, terminal, and document tabs

## 0.5.3 - 2026-11-23

- feat(ui): add dockable shell and Preferences Shortcuts
- ci(store): gate Store submit via env, not job if secrets

## 0.5.2 - 2026-11-23

- ci(store): submit MSIX to Partner Center on release tags
- feat(publish): add app screenshots
- fix(terminal): await shell launch after emulator is ready
- docs: add GitHub Pages site and deploy workflow
- added workflow permission
- feat(store): add MSIX pack script and skip GitHub updates when packaged

## 0.5.1 - 2026-11-22

- feat(lsp): code actions with Ctrl+. text edits

## 0.5.0 - 2026-11-22

- feat(lsp): Find References with Shift+F12
- feat(lsp): go to definition and document outline

## 0.4.0 - 2026-11-22

- test: add workspace/disk-search smoke script
- feat(workspace): Open Folder, explorer tree, and Find in Folder
- feat(workspace): add WorkspaceService, DiskSearch, and folder CLI args
- Document 0.3 features and approve the 0.4 workspace sketch.

## 0.3.0 - 2026-11-22

- Add search, recovery, and file-fidelity improvements for 0.3.
- Find/replace in open tabs (regex, whole word, replace all); encoding re-decode incl. UTF-16 BE
- Read-only buffers, crash recovery, live file watching, 8 MB large-file guard, per-tab word wrap

## 0.2.4 - 2026-11-22

- test(ClosedTabSnapshot): replace hardcoded Windows path with temp path
- ci: remove --no-build from dotnet test step
- ci: run tests in Release configuration matching build
- ci: add GitHub Actions workflow for build and test
- Restructure preferences and menus, add GitHub Releases updater.

## 0.2.3 - 2026-11-22

- Add release script to bump version, changelog, tag, and push.
- Add per-buffer tab indent, external formatter, and bun link launcher.
