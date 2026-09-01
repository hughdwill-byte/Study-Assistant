# Study HUD — HANDOFF.md

> Authoritative architecture guide for developers and coding AI continuing this project.
> Keep this updated as the codebase evolves.

---

## Overview

Study HUD is a Windows 11 desktop study-assistant application built in **C# / .NET 8 / WPF**.
It provides an always-on-top transparent HUD overlay, configurable macros, one-gesture screenshot
capture, multi-monitor layouts, and a deterministic Notion-backed question finder.

**The spec is the source of truth**: `Study_HUD_Master_Specification.md` in the project root.

---

## Solution Structure

```
src/
├── StudyHud.App          — WPF entry point, DI wiring, App.xaml, MainWindow
├── StudyHud.Core         — Shared models, service interfaces, events (no WPF dependency)
├── StudyHud.Windows      — Win32/native P/Invoke, MonitorService, ForegroundWindowService
├── StudyHud.Overlay      — WPF transparent overlay windows, OverlayManager
├── StudyHud.Macros       — Macro definitions, profiles, MacroEngine
├── StudyHud.Capture      — One-gesture region capture, CaptureOverlayWindow
├── StudyHud.Notion       — Notion API connector, incremental sync
├── StudyHud.Ocr          — Windows.Media.Ocr wrapper, OcrNormaliser
├── StudyHud.Search       — FeatureExtractor, LocalSearchIndex (SQLite FTS5)
├── StudyHud.Storage      — DatabaseMigrator, SQLite schema
├── StudyHud.Theming      — ThemeService, token system, contrast protection
└── StudyHud.Tests        — xUnit tests (feature extraction, state machine, policy)
```

---

## Architecture Decisions

### ADR-001: Hybrid Multi-HWND Overlay (spec §4, §171)

**Problem**: WPF cannot simultaneously be fully `WS_EX_TRANSPARENT` (click-through) and
have reliably interactive controls in selected regions.

**Chosen approach**: One passive `MonitorOverlayWindow` per physical monitor (transparent
in Ghost mode; WS_EX_TRANSPARENT removed in Active/Edit). Small "interactive island" HWNDs
(`RevealTab`, optional `ControlCapsule`) are created as separate owned top-level windows
with `WS_EX_NOACTIVATE` and exact hit regions.

**Limitation**: Interactive island HWNDs must be positioned via `SetWindowPos` whenever
the panel moves/resizes. OverlayManager coordinates this.

---

### ADR-002: Input Mechanisms (spec §36, §168, §170)

- **RegisterHotKey**: global keyboard shortcuts (Panic Hide, workspace switch)
- **SetWinEventHook (EVENT_SYSTEM_FOREGROUND)**: foreground app tracking — never polls
- **Low-level mouse hook**: only for side-button hold/release (one-gesture capture, macros)
- **Low-level keyboard hook**: observes only keys registered via `WatchKey` (the Hold-to-Interact
  trigger key) — never streams ordinary typing; never suppresses the keystroke
- **SendInput**: keyboard/text injection from macros

Hook callbacks are **latency-critical**. They MUST only: read cached state, update minimal
press/hold/release state, decide pass-through vs suppress, enqueue event via `Channel<T>`.
No OCR, database, WPF, or synchronous work inside callbacks.

Fail-open policy: if the queue is full or the macro system is unhealthy, pass the input through.

---

### ADR-003: SQLite + FTS5 (spec §49, §176)

- WAL mode enabled at startup
- One serialised writer service; small read-only connection pool for searches
- FTS5 virtual table (`note_fts`) with BM25 ranking
- Migrations via `DatabaseMigrator` with `PRAGMA user_version`
- No write transaction held open during OCR or network operations

Database path: `%LOCALAPPDATA%\StudyHud\studyhud.db`

---

### ADR-004: OCR — Windows.Media.Ocr (spec §40)

Primary OCR engine: `Windows.Media.Ocr` (built-in, local, free).
Requires a language pack on the user's machine. Falls back gracefully.
Tesseract can be added as a second implementation of `IOcrService` if Windows OCR is
insufficient (particularly for mathematical notation).

**Never calls generative AI, cloud OCR, or any remote service.**

---

### ADR-005: Assessment Mode Network Policy (spec §182, §183)

`AssessmentPolicyService` is the single enforcement point for ALL Study HUD outbound requests.
Every Study HUD HTTP client checks `IAssessmentPolicyService.IsOperationAllowed()` before making
any network call.

**Assessment Mode does NOT disable Windows networking or affect any other application.**
It only prevents Study HUD itself from making prohibited calls.

---

### ADR-006: Packaging — MSIX preferred (spec §180)

Not yet configured. Plan: MSIX for production builds (per-user, clean uninstall, Start Menu).
If MSIX restrictions break low-level hooks or startup behaviour, fall back to a signed
ClickOnce or Squirrel installer. Decision must be validated with actual hook testing.

---

### ADR-007: Settings + Layout Persistence & Coordination (spec §19, §22, §29, §71)

**Problem**: Panel layouts, the Hold-to-Interact trigger, theme, course, and Assessment Mode
had nowhere to persist, and nothing coordinated the "workspace + layout + macro profile"
switch the spec requires (§134). `ILayoutService` and a `WorkspaceCoordinator` were referenced
but did not exist.

**Chosen approach**: Local-first JSON under `%LOCALAPPDATA%\StudyHud`:
- `settings.json` ← `JsonSettingsStore : ISettingsStore` (`StudyHudSettings` record).
- `layouts\workspace-<Workspace>.json` ← `LayoutService : ILayoutService` (list of `PanelLayout`).

Both write atomically (temp file + `File.Move`) and fall back to defaults on a missing/corrupt
file (§69). No secrets are ever written here — the Notion token stays in the credential store (§46).

`WorkspaceCoordinator` (in `StudyHud.Overlay`) subscribes to `ApplicationState.StateChanged`.
On a workspace change it persists the outgoing layout, restores the incoming layout (recovering
panels whose monitor is gone, §160), and switches the associated macro profile via the small
`IMacroProfileSwitcher` abstraction (implemented by `MacroEngine`) — so the overlay layer never
references the macro engine assembly directly. `App` starts it after overlays init and calls
`SaveCurrentAsync()` on exit.

**Assessment Mode is now single-source-of-truth**: `AssessmentPolicyService` subscribes to
`ApplicationState` and mirrors `AssessmentModeActive`. Previously the settings-window toggle
updated only `ApplicationState`, so the enforcement point (`IsOperationAllowed`) never changed
and blocked nothing (§41, §182). The sync is one-way; the policy never writes back.

---

## Module Responsibilities

| Module | Owns |
|--------|------|
| `StudyHud.Core` | `ApplicationState`, `MonitorInfo`, `PanelLayout`, all service interfaces |
| `StudyHud.Windows` | Win32 P/Invoke, `MonitorService`, `ForegroundWindowService`, input hook |
| `StudyHud.Overlay` | `MonitorOverlayWindow`, `OverlayManager`, panel rendering |
| `StudyHud.Macros` | `MacroDefinition`, `MacroProfile`, `MacroEngine` |
| `StudyHud.Capture` | `CaptureService`, `CaptureOverlayWindow` |
| `StudyHud.Ocr` | `WindowsOcrService`, `OcrNormaliser` |
| `StudyHud.Search` | `FeatureExtractor`, `LocalSearchIndex` |
| `StudyHud.Storage` | `DatabaseMigrator`, SQLite schema, `JsonSettingsStore`, `LayoutService` |
| `StudyHud.Notion` | `NotionConnector`, `WindowsCredentialStore` |
| `StudyHud.Theming` | `ThemeService`, `ThemeTokenSet` |
| `StudyHud.App` | DI wiring, `App.xaml.cs`, `MainWindow` |

---

## HUD State Machine (spec §127)

```
GHOST  ←→  ACTIVE   (Hold-to-Interact pressed/released)
 ↓                    ↑
EDIT  ←  user enters  (deliberate Edit Mode entry)
 ↓
GHOST  (deliberate Edit Mode exit)
```

Key rule: releasing Hold-to-Interact does **not** exit Edit Mode.
Implemented in `ApplicationStateService.SetHudInteractionState()`.

---

## Panel Lifecycle (spec §130)

Each panel has four independent states:

1. **Interaction state**: Ghost / Active / Edit
2. **Visibility state**: Expanded / EdgeCollapsed / Hidden
3. **Responsive state**: Compact / Normal / Expanded layout
4. **Dock state**: Floating / Docked / MemberOfDockGroup

These must never be collapsed into one variable.

---

## Coordinate System (spec §85)

- Physical pixels: Win32 screen coordinates (what BitBlt uses)
- WPF DIPs: 96dpi-normalised device-independent units
- Monitor-normalised: 0.0–1.0 within a monitor's work area (for layout persistence)

Conversion helpers are in `MonitorService`. Always use the monitor's `ScaleFactor`
for conversion — do NOT assume 96dpi.

---

## OCR + Search Pipeline (spec §50, §54)

```
NOTION IMAGE → download → cache on disk → OCR (local) → OcrNormaliser → FeatureExtractor
→ SQLite FTS5 insert (one writer, batched transactions)

QUESTION CAPTURE → BitBlt → OCR (local) → OcrNormaliser → FeatureExtractor
→ LocalSearchIndex.SearchAsync → deterministic BM25 + variable/unit boost → results
```

Search results include per-result `MatchExplanation` so the user sees exactly which
terms, variables, and expressions matched (spec §56). The score is never called "AI confidence".

**Write/ingest path (`ISearchIndex.IndexItemsAsync` etc.)**: the sync/OCR layer hands the index
`IndexableNoteItem`s (already OCR'd + normalised). `LocalSearchIndex` upserts them into
`note_items` and rebuilds their `note_fts` rows through a single controlled writer, batched in one
transaction, with a bounded `SQLITE_BUSY` retry (spec §49). `GetContentHashesAsync` lets the sync
layer diff by content hash and skip unchanged items (spec §47). `note_fts` was migrated (migration 2)
from an external-content FTS5 table keyed on the integer rowid to a **standalone FTS5 table carrying
an UNINDEXED `note_item_id`**, so it is populated/deleted by the TEXT id and joins cleanly to
`note_items.id` (the old external-content design mismatched the read path's join).

Note: `DatabaseMigrator` and the index writer drive transactions with raw `BEGIN`/`COMMIT` or an
explicit `command.Transaction = tx`, because Microsoft.Data.Sqlite rejects a command executed under
an active `SqliteTransaction` it is not associated with.

**Pre-indexing pipeline (`INoteIndexer` → `NoteIndexer`)**: turns `RawNoteSource`s (image bytes or
text blocks) into indexed items — OCR once via `IOcrService`, hash the source to skip unchanged
content (spec §47), flag low-confidence/failed OCR for the library UI (§59, §60), and write the
batch through `ISearchIndex`. Local + non-generative throughout. A Notion sync (Phase 7) will
produce the `RawNoteSource`s that feed this.

**Notion sync (`NotionConnector` + `NotionBlockParser`)**: `NotionBlockParser` deterministically turns
a page's block children into `ParsedNoteBlock`s — headings maintain a running breadcrumb, image blocks
resolve their (temporary) URL, text blocks concatenate `rich_text` (spec §44). `NotionConnector`
fetches block children (paginated), parses, downloads image bytes promptly before the signed URLs
expire (§45), and feeds `INoteIndexer` (`SyncNotionPageAsync`). All blocked in Assessment Mode via the
policy. Still pending: the `courses.notion_root_page_id` → page mapping (the connector currently treats
the courseId as the page id), and the real DPAPI/credential-manager `ICredentialStore` (still a stub —
see below), so live sync is inert until a token can be stored.

**Question Finder runtime (`IQuestionFinder` → `QuestionFinder`)**: the query-time pipeline (§38, §54)
— local OCR of the captured region → `FeatureExtractor` → `ISearchIndex.SearchAsync` → ranked
results with explanations, plus the detected text/confidence so the UI can offer correction (§59).
It never answers the question and has no remote/generative code path. `QuestionFinderPanel` now calls
`ICaptureService.CaptureRegionAsync` → `IQuestionFinder.FindFromImageAsync` (replacing the old
placeholder), threaded through `OverlayManager` → `PanelHost`. Course scoping in the panel is still
a placeholder until the library populates real course ids (Phase 7).

---

## Assessment Mode Compliance (spec §41, §89, §182)

**Study HUD local-only operations (always allowed in Assessment Mode)**:
- Local OCR (`PolicyOperation.LocalOcr`)
- Local search (`PolicyOperation.LocalSearch`)
- Local indexing from already-downloaded content (`PolicyOperation.LocalIndex`)

**Blocked in Assessment Mode**:
- `NotionSync`, `LlmRequest`, `EmbeddingRequest`, `CloudOcrRequest`, `WebSearch`,
  `CapturedQuestionUpload`, `UpdateCheck`

Test that attempting any blocked operation returns `IsOperationAllowed() == false`
with a non-empty `GetBlockReason()`. See `StudyHud.Tests/CoreTests.cs`.

---

## Build Instructions

### Requirements

- .NET 8 SDK (x64)
- Windows 11 / Windows 10 (64-bit)
- Visual Studio 2022 17.8+ or JetBrains Rider 2024+

### Build

```powershell
cd src
dotnet restore
dotnet build -c Debug -a x64
```

### Run tests

```powershell
cd src
dotnet test StudyHud.Tests/StudyHud.Tests.csproj
```

### Run the app

```powershell
cd src
dotnet run --project StudyHud.App/StudyHud.App.csproj
```

---

## Implementation Status by Phase

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Foundation: solution, DI, logging, monitor service, overlay HWND | ✅ Complete |
| 2 | HUD Engine: Ghost/Active/Edit, Hold-to-Interact, panels, persistence | 🟡 In progress — settings + layout persistence done; Hold-to-Interact works for both mouse side-buttons and keyboard keys |
| 3 | Docking: snapping, dock graph, edge collapse | 🔲 Planned |
| 4 | Workspaces: switching, control capsule | 🟡 In progress — `WorkspaceCoordinator` saves/restores layout + switches macro profile on workspace change; control capsule still basic |
| 5 | Macro Engine: input, triggers, actions, profiles, editor | 🔲 Planned |
| 6 | Capture: custom region, clipboard, multi-monitor DPI | 🔲 Planned |
| 7 | Study Library: courses, SQLite, Notion full connector, cache | 🔲 Planned |
| 8 | OCR: full indexing, status, correction | 🔲 Planned |
| 9 | Search: full deterministic pipeline, ranking, explanations | 🔲 Planned |
| 10 | Question Finder: UI, capture-to-results | 🔲 Planned |
| 11 | Assessment Mode: policy enforcement, network, tests | 🔲 Planned |
| 12 | Themes: full token engine, colour wheel, future-theme hooks | 🔲 Planned |
| 13 | Polish: animation, onboarding, accessibility, QA | 🔲 Planned |

---

## Known Limitations / TODOs

- `WindowsCredentialStore` uses a stub — replace with real CredWrite/CredRead P/Invoke
  or the `CredentialManagement` NuGet package before Phase 7.
- `NotionConnector.SyncCourseInternalAsync` is scaffolded — full block traversal and
  image download pipeline are Phase 7 work.
- `WindowsOcrService` uses reflection to call WinRT — replace with direct SDK calls
  using `Microsoft.Windows.CsWinRT` in Phase 8.
- Interactive island HWNDs (RevealTab, ControlCapsule) are architecturally planned but
  not yet created — Phase 3/4 work.
- `MacroEngine` does not yet install low-level hooks — Phase 5 adds `IGlobalInputService`
  implementation with the hook layer.
- Keyboard-key Hold-to-Interact now works: `GlobalInputService` installs a `WH_KEYBOARD_LL` hook
  that reports down/up **only** for keys registered via `WatchKey` (the configured trigger), so
  ordinary typing is never observed or enqueued. `HoldToInteractService` watches/unwatches the
  trigger key as it changes. Note: the Caps Lock default still toggles caps state (the hook
  observes, it does not suppress) — a different default key or opt-in suppression could be offered
  later. Mouse side-button triggers also work end-to-end.
- Named/saved layouts (spec §21, e.g. "Lecture", "Dual Monitor Study") are not yet exposed in the
  UI. `ILayoutService` already supports arbitrary layout ids; `WorkspaceCoordinator` currently uses
  one auto-layout per workspace (`workspace-<Workspace>`). A layout manager UI is the next step.
- The settings window now has a `SettingsView` editor for the common `StudyHudSettings` fields
  (theme, Hold-to-Interact trigger, snap distance, control-capsule, exclusions, battery/fullscreen,
  auto-switch profile, assessment-startup). Save applies theme + Hold-to-Interact live. Still not
  surfaced in the UI: per-hotkey rebinding for panic/edit, and the workspace→macro-profile map
  (the model supports both).
- DPI handling in `CaptureOverlayWindow.WpfToPhysical` uses primary monitor scale as
  an approximation — needs per-monitor DPI at the capture point (Phase 6).

---

## Files NOT to Modify Without Reading This First

- `NativeMethods.cs` — P/Invoke must use `LibraryImport` (source-gen), not `DllImport`,
  and all structs must match Windows ABI exactly.
- `ApplicationStateService.SetHudInteractionState` — the Edit Mode guard is deliberate.
- `AssessmentPolicyService._blockedInAssessment` — must match spec §41 exactly.
- `DatabaseMigrator.ApplyMigration1Async` — schema changes require a new migration, not
  modification of existing migrations.
