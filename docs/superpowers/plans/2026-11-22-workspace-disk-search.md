# 0.4 Workspace + Disk Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native) or superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Open a single folder as a workspace with a file tree, find/replace on disk, and LSP `rootUri` retargeted to that folder.

**Architecture:** `WorkspaceService` owns the root path. `DiskSearch` enumerates files (exclude globs + binary/large skip) and matches via `TabSearch`. `FileTreePanel` is a collapsible left sidebar. Replace prefers open dirty tabs, else `FileService` round-trip. `LspManager` prefers workspace root and resets on folder change.

**Tech Stack:** .NET 10, Avalonia, AvaloniaEdit, xUnit, existing `TabSearch` / `FileService` / `LargeFilePolicy`.

**Spec:** [docs/0.4-workspace-disk-search.md](../../0.4-workspace-disk-search.md) (approach B).

## Global Constraints

- One folder at a time; no multi-root / `.xpad-workspace` file in 0.4.
- Default excludes: `.git`, `node_modules`, `bin`, `obj`, `.vs`.
- Match cap 2000; skip files above `LargeFilePolicy.ThresholdBytes` or with null bytes in the first 8 KB.
- User data under `%APPDATA%/XPad/`; persist `LastWorkspacePath` + exclude list in `AppSettings`.
- Degrade gracefully (status text); never crash on missing permissions or LSP.

## Review Focus

- Replace must not overwrite an open dirty tab from disk.
- Directory CLI args must open a folder, not create a fake file tab.
- Excluded dirs under nested paths (e.g. `src/node_modules`) must be skipped.
- Binary detection must not treat UTF-16 text as binary solely for a BOM.
- Changing folder must call `LspManager.ResetAsync` before new clients start.

---

### Task 1: WorkspaceService + settings

**Files:**
- Create: `Services/WorkspaceService.cs`
- Modify: `Models/AppSettings.cs`
- Test: `x-pad.Tests/WorkspaceServiceTests.cs`

**Interfaces:**
- Produces: `WorkspaceService` with `string? Root`, `event EventHandler? Changed`, `bool TryOpen(string path)`, `void Close()`, `IReadOnlyList<string> DefaultExcludeSegmentNames`

- [ ] Add `LastWorkspacePath` and `List<string> WorkspaceExcludeNames` (defaults above) to `AppSettings`
- [ ] Implement `WorkspaceService` (normalize full path; only existing directories; raise `Changed`)
- [ ] Tests: open/close/invalid path; defaults include `.git` and `node_modules`
- [ ] Commit: `feat(workspace): add WorkspaceService and settings`

### Task 2: CliArgs directories

**Files:**
- Modify: `Services/CliArgs.cs`
- Test: `x-pad.Tests/CliArgsTests.cs`

**Interfaces:**
- Produces: `ResolveWorkspacePaths(args)` → existing directories; `ResolveFilePaths` unchanged for files
- Or: return a discriminated result — prefer separate `ResolveWorkspacePaths` to avoid breaking file open-or-create

- [ ] Add `ResolveWorkspacePaths`; update help text for folder args
- [ ] Test: existing temp directory is returned; file paths still via `ResolveFilePaths`
- [ ] Commit: `feat(cli): accept directory args as workspace roots`

### Task 3: DiskSearch

**Files:**
- Create: `Services/DiskSearch.cs`
- Test: `x-pad.Tests/DiskSearchTests.cs`

**Interfaces:**
- Consumes: `TabSearch`, `LargeFilePolicy`, exclude name list
- Produces: `DiskSearch.Search(root, pattern, TabSearchOptions, excludes, maxHits=2000) -> DiskSearchResult`
- Produces: `DiskSearch.IsSkippableFile(path)` / binary heuristic

- [ ] Enumerate files recursively; skip excluded directory names; skip large/binary
- [ ] Match each file text with `TabSearch`; stop at maxHits; set `Truncated`
- [ ] Tests: exclude nested `node_modules`; find hit; truncate; skip null-byte file
- [ ] Commit: `feat(search): add DiskSearch over TabSearch`

### Task 4: Disk replace helper

**Files:**
- Create: `Services/DiskReplace.cs` (or methods on `DiskSearch`)
- Test: `x-pad.Tests/DiskReplaceTests.cs`

**Interfaces:**
- Produces: `ApplyReplacements(path, matches, replacement, useRegex, pattern, FileService, openTabText?)` policy:
  1. If open dirty tab text provided → return updated text for caller to apply to `Document`
  2. Else read/write via `FileService` preserving encoding/EOL

- [ ] Tests: dirty-open path returns in-memory text without writing; clean path rewrites file and preserves UTF-8 BOM
- [ ] Commit: `feat(search): safe disk replace with open-tab preference`

### Task 5: File tree UI + open folder commands

**Files:**
- Create: `ViewModels/FileTreeNode.cs`, `ViewModels/FileTreeViewModel.cs`, `Views/FileTreePanel.axaml(.cs)`
- Modify: `Views/MainWindow.axaml(.cs)`, `ViewModels/MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `WorkspaceService`
- Produces: lazy children; double-click / Enter opens file via `OpenFileFromPath`

- [ ] Left pane visible when workspace open (or View → Explorer toggle)
- [ ] File → Open Folder… / Close Folder; remember `LastWorkspacePath`
- [ ] Wire ctor args: open CLI workspace paths; drop directory sets workspace
- [ ] Commit: `feat(workspace): file tree sidebar and Open Folder`

### Task 6: Find in Folder dialog

**Files:**
- Create: `Views/FindInFolderWindow.axaml(.cs)`
- Modify: `ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `DiskSearch`, `DiskReplace`, open tabs collection
- Gesture: `Ctrl+Shift+H` Find in Folder; keep `Ctrl+Shift+F` for open tabs

- [ ] Same options as Find in Open Tabs + Replace / Replace All
- [ ] Jump opens file and `GoToMatch`; replace uses Task 4 policy
- [ ] Commit: `feat(search): Find/Replace in Folder dialog`

### Task 7: LSP root retarget

**Files:**
- Modify: `Services/Lsp/LspManager.cs`, `ViewModels/MainWindowViewModel.cs`

**Interfaces:**
- Consumes: optional `Func<string?> workspaceRootProvider` or inject `WorkspaceService`
- On workspace `Changed`: `await ResetAsync()`

- [ ] Prefer workspace root over file directory when starting a client
- [ ] Reset clients when folder opens/closes/changes
- [ ] Commit: `feat(lsp): use workspace folder as rootUri`

### Task 8: Docs smoke + finish

**Files:**
- Modify: `README.md`, `AGENTS.md`

- [ ] Document Open Folder, Find in Folder, excludes, `Ctrl+Shift+H`
- [ ] `dotnet test` green
- [ ] Commit: `docs: note 0.4 workspace and disk search`
