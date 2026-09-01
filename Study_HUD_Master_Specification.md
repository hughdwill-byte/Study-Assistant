# Study HUD — Complete Implementation Specification

> **Purpose:** This is the authoritative product, UX, architecture, implementation, and acceptance specification for the Windows Study HUD application. Treat every numbered section as a requirement unless it is explicitly marked optional, experimental, future, or suggested.
>
> **Implementation principle:** Do not simplify away requested behaviour merely because another implementation is easier. If Windows imposes a real technical limitation, implement the closest reliable alternative, document the limitation, and preserve the intended user workflow as closely as possible.

---

## 1. TARGET PLATFORM

Build the application primarily for:

- Windows 11
- 64-bit systems
- Multiple monitors
- Mixed monitor resolutions
- Mixed DPI/scaling percentages
- Taskbar pinning
- Desktop use rather than mobile/web use

Preferred implementation:

- C#
- .NET 8 or newer stable version
- WPF unless there is a compelling technical reason to use another Windows-native framework

WPF is preferred because the application requires extensive control over:

- HWND behaviour
- transparent windows
- always-on-top overlays
- click-through windows
- no-activate windows
- custom input handling
- per-monitor overlay windows
- low-level keyboard/mouse interaction
- DPI handling

The architecture should nevertheless isolate Windows-native functionality behind services so it could be migrated later if required.

---

## 2. CORE PRODUCT MODEL

Think of the application as:

```text
STUDY HUD

├── WORKSPACES
│   ├── Note Taking
│   ├── Question Finder
│   └── Future Workspaces
│
├── ACTIONS
│   ├── Macros
│   ├── Screenshot Capture
│   ├── Keyboard/Text Actions
│   └── Global Shortcuts
│
├── LIBRARY
│   ├── Notion Synchronisation
│   ├── OCR
│   ├── Local Index
│   ├── Courses
│   └── Weeks / Sections
│
└── PRESENTATION
    ├── HUD Panels
    ├── Docking
    ├── Multi-Monitor Layouts
    ├── Themes
    └── Visibility / Interaction
```

Every feature should belong cleanly to one of these systems.

Avoid creating a monolithic application where UI components contain application logic.

---

## 3. OVERALL APPLICATION STRUCTURE

The application should consist of two major UI layers.

### 3.1 HUD

This is the always-available overlay shown while studying.

It consists of movable, resizable, dockable HUD panels.

The HUD should feel lightweight and unobtrusive.

### 3.2 Main Settings / Management Window

Configuration should occur inside a normal desktop application window rather than inside the overlay itself.

This window manages:

- Courses
- Notion connection
- Library/index status
- Macros
- Macro profiles
- Workspaces
- Themes
- HUD panels
- Monitor layouts
- Keybindings
- OCR settings
- Assessment Mode
- Application exclusions
- Diagnostics
- Backup/export/import
- Updates if implemented later

The HUD should therefore remain focused on runtime actions rather than administration.

---

## 4. HUD WINDOW ARCHITECTURE

Use a **hybrid multi-HWND architecture per monitor** so the HUD can remain genuinely click-through while a few small controls remain reliably interactive.

The preferred structure is:

1. **Passive monitor overlay HWND** — one transparent rendering surface per monitor for Ghost-mode panels, snap previews, capture visuals, and other non-interactive HUD rendering.
2. **Interactive island HWNDs** — small owned top-level helper HWNDs for controls that must remain clickable independently of the passive overlay, particularly edge reveal tabs and, when configured, the control capsule.
3. **Logical HUD panel model** — panels, docking groups, workspaces, layouts, and themes remain coordinated in one application model even if several HWNDs are used to render/hit-test them.

Do **not** create an independent top-level application-style window for every ordinary control. The additional HWNDs exist only where Windows/WPF hit-testing behaviour makes an independently interactive surface technically valuable.

Prefer owned top-level helper HWNDs rather than relying on child HWNDs that inherit clipping or transparency behaviour from a passive parent. These helper HWNDs must use tightly bounded hit regions, no taskbar entries, consistent ownership/Z-order, and `WS_EX_NOACTIVATE` where appropriate so clicking a reveal tab does not unnecessarily steal focus from the study application underneath.

This architecture is intended to avoid forcing one WPF HWND to be simultaneously fully `WS_EX_TRANSPARENT` and reliably interactive in selected areas.

Benefits include:

- reliable Ghost-mode click-through
- reliable reveal-tab/control-capsule interaction
- easier docking and group movement through a shared logical model
- controlled Z-order
- simpler theme coordination
- easier snap previews
- easier multi-panel animation
- fewer WPF hit-testing edge cases

Each monitor overlay must correctly understand:

- monitor bounds
- Windows work area
- taskbar position
- DPI scaling
- resolution
- orientation
- monitor identity

The app should respond correctly if:

- a monitor is disconnected
- a monitor is connected
- resolution changes
- scaling changes
- orientation changes
- primary display changes
- taskbar location changes

---

## 5. HUD INTERACTION MODEL

A permanent overlay becomes irritating if it constantly intercepts input.

Therefore HUD interaction must use clearly separated states.

Each panel must support:

### 5.1 Ghost Mode

Default studying state.

The panel is visible but mouse interaction passes through it to the application underneath.

The panel must not unnecessarily steal keyboard focus.

### 5.2 Active Mode

The HUD becomes interactive.

Buttons, selectors and other controls can be clicked.

### 5.3 Edit Mode

The panel becomes editable.

In Edit Mode the user can:

- move panels
- resize panels
- dock panels
- detach panels
- collapse panels
- rearrange docked groups
- change panel-specific settings
- add/remove panels

Movement and resizing controls should not clutter the HUD outside Edit Mode.

---

## 6. HOLD-TO-INTERACT

Implement a configurable global **Hold-to-Interact** input.

Normal state:

HUD = Ghost / click-through.

While the selected interaction key or button is held:

HUD = Active.

When released:

HUD immediately returns to Ghost.

The user must be able to configure this trigger.

Do not permanently hard-code Caps Lock or another specific key.

Potential triggers include:

- keyboard key
- modifier combination
- mouse side button
- supported special input

If the selected trigger conflicts with another binding, warn the user.

Also provide an optional traditional toggle shortcut for users who prefer temporarily locking the HUD in Active Mode.

---

## 7. PANIC / HIDE HUD COMMAND

Provide a configurable global shortcut that instantly hides the complete HUD.

Pressing it again restores the HUD.

This is useful when:

- screen sharing
- presenting
- watching videos
- playing games
- entering full-screen applications
- temporarily needing a clean desktop

The hide action should occur immediately.

---

## 8. PANEL SYSTEM

HUD panels are reusable UI modules.

Examples could eventually include:

- macro panel
- Question Finder
- current course
- quick tools
- formula shortcuts
- timer
- clipboard tools
- custom future widgets

Panels should not behave like unrelated floating desktop windows.

They are components of one coordinated HUD system.

---

## 9. PANEL MOVEMENT AND RESIZING

In Edit Mode, every eligible panel must support:

- drag repositioning
- resize from appropriate edges/corners
- minimum size
- optional maximum size
- snap preview
- monitor-edge snapping
- panel-to-panel snapping

Panels should never become permanently inaccessible outside monitor bounds.

If an old saved position becomes invalid, clamp or recover the panel automatically.

Provide:

- **Reset Panel Position**
- **Reset Current Layout**

---

## 10. RESPONSIVE PANEL DESIGN

A core requirement is that resizing must never create ugly layouts such as:

- half-visible words
- text clipped through the middle
- controls cut in half
- unusably compressed buttons
- labels partially visible

Every panel must define responsive states.

At minimum:

### Compact

Minimal useful information.

### Normal

Primary working layout.

### Expanded

Additional detail and controls.

Do not simply scale the full interface down.

Instead:

- change layout
- hide lower-priority information
- switch text to icons where intuitive
- wrap text cleanly
- use ellipsis only where appropriate
- rearrange controls
- alter spacing
- maintain minimum readable sizes

Responsive behaviour should feel deliberately designed.

---

## 11. PANEL SNAPPING

Panels should magnetically snap to:

- monitor edges
- other panel edges
- aligned corners
- compatible dock positions

Use a configurable snap distance approximately around 10–20 logical pixels by default.

While dragging near a snap destination:

- show a subtle preview
- visually indicate intended docking
- smoothly magnetise into alignment

Snapping must not feel aggressive.

Users should still be able to deliberately place panels nearby without docking if desired.

---

## 12. PERSISTENT DOCKING

Snapping two panels together should optionally create a persistent relationship rather than merely lining up coordinates.

Represent docking internally as relationships or a dock graph.

A docked group should support:

- moving the entire group
- resizing compatible sections
- detaching one panel
- replacing a panel
- hiding a panel
- collapsing a panel
- collapsing the group if enabled
- remembering geometry
- preserving adjacency

Example:

A long bottom panel can dock to a vertical right-side panel.

Their edges should remain perfectly connected while resized.

---

## 13. EDGE-COLLAPSE SYSTEM

Panels attached to a monitor edge must optionally support sliding almost completely off-screen.

Example:

```text
Expanded:
[ HUD PANEL ][ > ]

After collapse to right:
                             [ < ]
```

Only a small reveal tab remains visible.

Clicking the reveal tab restores the panel.

Support all directions.

### Left edge

Panel disappears left.

Leave a `>` style reveal control.

### Right edge

Panel disappears right.

Leave a `<` style reveal control.

### Top edge

Panel disappears upward.

Leave a downward-facing reveal control.

### Bottom edge

Panel disappears downward.

Leave an upward-facing reveal control.

The visual arrow itself does not need to literally be a text character; themes can replace it.

---

## 14. EDGE-COLLAPSE REQUIREMENTS

The reveal tab must:

- remain always on top
- be very small
- stay within the visible work area
- inherit the active theme
- support hover feedback
- support click feedback
- restore with one click
- remain attached to its collapse location
- remember the panel's original size
- remember the panel's original position
- remember dock relationships
- remember workspace
- remember monitor
- remember responsive layout
- work correctly with mixed DPI monitors
- optionally be repositioned in Edit Mode
- optionally be disabled per panel

The hidden section of the panel must not intercept input.

Collapsing a panel must not destroy it.

It is a presentation state.

Panel visibility states should therefore be separate from interaction states.

Visibility states:

- Expanded
- Edge Collapsed
- Hidden

Interaction states:

- Ghost
- Active
- Edit

---

## 15. EDGE-COLLAPSE INTERACTION OPTIONS

Allow two behaviours.

### Default — Always Interactive Reveal Tab

Even while the HUD is Ghost/click-through, the remaining arrow tab can be clicked.

This is the preferred default.

### Optional — Respect Hold-to-Interact

The reveal tab becomes interactive only while Hold-to-Interact is active.

Expose this as a user preference.

---

## 16. COLLAPSE ANIMATION

Collapse/restore should use a short functional animation around approximately:

100–180 ms.

The panel should visibly slide behind or out from the corresponding monitor edge.

Avoid dramatic easing or excessive effects.

The goal is to feel mechanically attached to the desktop.

---

## 17. COLLAPSE MACRO ACTIONS

Expose:

- CollapsePanel
- ExpandPanel
- TogglePanelCollapse

as actions available to:

- keyboard shortcuts
- mouse controls
- macro sequences
- HUD controls

---

## 18. DOCK GROUP COLLAPSE

Default behaviour:

collapsing one panel affects only that panel.

Provide an optional setting:

**Collapse entire dock group together**

for users who want a connected side/bottom HUD structure to disappear as one object.

---

## 19. PANEL LAYOUT STORAGE

Save layout information persistently.

Avoid relying solely on absolute pixel coordinates.

Store enough information to restore a layout intelligently across:

- resolution changes
- DPI changes
- monitor changes

Where practical use monitor-relative normalized coordinates combined with logical sizing.

Save:

- panel identifier
- workspace
- monitor identifier
- relative position
- logical dimensions
- responsive state
- dock relationships
- visibility state
- collapse direction
- reveal-tab position
- z-order if required
- theme overrides if supported

---

## 20. MULTI-MONITOR SUPPORT

The system must be designed around multiple monitors from the beginning.

Do not add this as an afterthought.

The user commonly uses two monitors.

Panels must be able to:

- appear independently on either monitor
- move between monitors
- maintain correct scaling
- use monitor-specific layouts
- dock only where geometrically valid

Allow layouts such as:

### Monitor 1

Lecture/PDF/browser.

Small screenshot/macro HUD.

### Monitor 2

Notion.

Larger note-taking controls.

---

## 21. SAVED LAYOUTS

Users should be able to create named layouts.

Examples:

- Lecture
- Assignment
- Note Taking
- Question Finder
- Minimal
- Monitor 1 Only
- Dual Monitor Study

Switching layout should be fast and smooth.

Layouts may be associated with workspaces.

---

## 22. WORKSPACES

The HUD should support different workspaces rather than showing every tool simultaneously.

Initial workspaces:

1. Note Taking
2. Question Finder

The architecture must allow future workspaces.

Workspace switching should be available through:

- control capsule
- global hotkey
- optional macro
- configurable shortcut

Workspace switching should occur quickly, ideally under roughly 100 ms once resources are loaded.

---

## 23. SMALL CONTROL CAPSULE

Provide an optional small persistent controller snapped to an edge.

Conceptually similar to:

```text
[ Notes | Question | Settings ]
```

Exact visual design depends on the current theme.

It should provide quick access to:

- current workspace
- current course
- assessment/compliance status
- basic HUD controls
- settings
- optional sync status

It should consume very little screen space.

The user should be able to disable it.

---

## 24. NOTE TAKING WORKSPACE

The Note Taking workspace is designed to remove repetitive input while taking notes.

Its main systems are:

- configurable macros
- screenshot capture
- rapid text insertion
- key sequences
- app-aware automation
- course/profile context

It should not become a giant toolbar.

Keep the interface compact and allow users to create only the controls they need.

---

## 25. MACRO ENGINE

Do not implement macros as a simple list of shortcuts.

Use a general architecture:

```text
TRIGGER
↓
CONDITIONS
↓
ACTION
↓
ACTION
↓
ACTION
```

Each macro should be represented as structured data.

Example conceptual schema:

- ID
- Name
- Enabled
- Trigger
- Trigger Type
- Conditions
- Actions[]
- Cooldown
- Allowed Applications
- Blocked Applications
- Workspace/Profile
- Failure behaviour
- Optional description

---

## 26. MACRO TRIGGERS

Support configurable trigger types such as:

- keyboard shortcut
- function key
- mouse side button
- mouse button hold
- mouse button press
- key chord
- possibly double press where reliable

Do not globally steal ordinary left mouse input by default.

Prefer:

- side mouse buttons
- keyboard shortcuts
- explicit chords

Detect conflicts where practical.

---

## 27. MACRO CONDITIONS

Macros should optionally restrict execution based on conditions such as:

- active workspace
- foreground application
- excluded application
- current profile
- current course
- HUD state
- whether capture mode is already active

This prevents macros from interfering with unrelated applications.

---

## 28. MACRO ACTION TYPES

Support an extensible action system.

Initial actions should include:

- KeyDown
- KeyUp
- KeyPress
- Shortcut
- TypeText
- Delay
- OpenURL
- LaunchProgram
- RunCommand where safely permitted
- CaptureRegion
- CopyToClipboard
- Paste
- CollapsePanel
- ExpandPanel
- TogglePanelCollapse
- SwitchWorkspace
- SwitchMacroProfile
- HideHUD
- ShowHUD
- ToggleHUD

Use Windows SendInput or appropriate native APIs for keyboard injection.

For Unicode text:

- support Unicode input
- support clipboard-assisted insertion when appropriate

---

## 29. MACRO PROFILES

Macros should be organised into profiles.

Example:

### Normal Profile

Mouse buttons behave normally.

### Note Taking Profile

Mouse 4 → configured text action  
Mouse 5 hold → screenshot region  
F8 → common formula  
F9 → heading shortcut

### Question Finder Profile

Mouse 5 → capture question  
F8 → open top result

Profiles can be manually switched.

A workspace may automatically activate its associated profile.

The user should be able to disable automatic switching.

---

## 30. MACRO CONFIGURATION UI

Build a polished macro editor in the main settings window.

It should allow:

1. Name macro
2. Capture/select trigger
3. Configure optional conditions
4. Add actions
5. Drag actions to reorder
6. Configure action properties
7. Test macro
8. Save
9. Enable/disable

The UI should make complex macros understandable without exposing raw JSON to normal users.

Advanced users may optionally inspect/export the underlying configuration.

---

## 31. SCREENSHOT SYSTEM

Do not rely primarily on Windows Win+Shift+S.

Windows Snipping Tool can remain available as an optional standard macro.

However the application's primary workflow must use its own selection overlay.

Reason:

It is not reliable to trigger Win+Shift+S using one mouse-down event and then reuse that same physical event as the drag event inside the separate Windows Snipping Tool process.

Build the selection interaction directly.

---

## 32. ONE-GESTURE SCREENSHOT CAPTURE

Preferred interaction:

Hold configured screenshot trigger.

Example:

Hold Mouse Button 5.

Immediately:

- cursor becomes selection crosshair
- capture overlay activates

While holding:

- move pointer
- rectangular selection grows

On release:

- selected region is captured
- screenshot is copied to clipboard
- overlay disappears

Thus one gesture performs the entire region capture.

Trigger must be configurable.

---

## 33. SCREENSHOT OVERLAY

The screenshot selection overlay should:

- support every monitor
- capture across the correct display
- darken non-selected area subtly
- show clear selection boundary
- show dimensions if useful
- cancel using Esc
- cancel by configurable action
- work with mixed DPI scaling
- have extremely low perceived delay
- avoid unnecessarily activating unrelated windows

---

## 34. SCREENSHOT DESTINATIONS

Initial reliable destination:

- Clipboard

Also architect for optional future destinations:

- Save to file
- Clipboard + save
- Current foreground app
- Pinned application
- Notion

Do not overpromise automatic app pasting.

Automatic targeted paste may be offered as an experimental/advanced feature if reliable in testing.

Clipboard capture must remain the dependable baseline.

---

## 35. OPTIONAL CAPTURE-TO-NOTES WORKFLOW

An advanced workflow may eventually support:

Monitor 1:

lecture/PDF.

Monitor 2:

Notion notes.

User captures a region.

App:

1. captures image
2. copies it
3. optionally switches/focuses configured note destination
4. pastes image
5. optionally restores original focus

This must only be enabled if real-world testing demonstrates acceptable reliability.

Foreground/focus restrictions and Windows security boundaries must be respected.

Never use unsafe hacks to bypass protected Windows behaviour.

---

## 36. INPUT AND WINDOWS SECURITY

Use the least intrusive Windows input mechanism that satisfies each requirement:

- `RegisterHotKey` for ordinary global keyboard shortcuts where possible
- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` for event-driven foreground-window tracking
- Raw Input where device-level observation is useful and suppression is not required
- low-level keyboard hooks only when the required semantics cannot be achieved with `RegisterHotKey`
- low-level mouse hooks only for global mouse-button/hold/release semantics that genuinely require them
- `SendInput` for supported keyboard/text injection
- `GetWindowLongPtr`
- `SetWindowLongPtr`
- `SetWindowPos`

Low-level hook callbacks are a **latency-critical path**. They must never perform OCR, database access, process discovery, synchronous logging, UI work, network activity, file I/O, or macro execution directly. A hook callback may only inspect already-cached state, update minimal input state, decide whether the event must be suppressed, and enqueue a compact event to a worker using a lock-minimal mechanism such as `Channel<T>`, a bounded concurrent queue, or an equivalent design.

The normal suppression decision must be possible from cached state without waiting for another thread. If Study HUD cannot make a safe decision immediately, prefer a **fail-open** behaviour that passes the original input through rather than freezing or swallowing normal system input, except when an already-active capture gesture requires ownership until release/cancel.

Be aware of Windows UIPI/elevation restrictions.

A non-elevated Study HUD may not be able to inject input into an elevated application.

Do not attempt to bypass this security model.

If injection fails:

- report it clearly
- optionally explain elevation mismatch
- fail gracefully

Never attempt automation on:

- Windows secure desktop
- UAC secure prompts
- lock screen
- credential interfaces

---

## 37. APPLICATION EXCLUSIONS

Allow users to configure applications where:

- HUD automatically hides
- macros are disabled
- capture tools are disabled
- interaction is restricted

Provide options such as:

**Hide HUD in fullscreen applications**

and an application exclusion list.

Useful cases include:

- games
- video playback
- presentations
- screen sharing
- secure software

---

## 38. QUESTION FINDER WORKSPACE

The Question Finder lets me draw a rectangle around a question shown anywhere on screen.

The system then:

1. captures the question
2. performs OCR
3. extracts deterministic search features
4. searches my locally indexed Notion notes
5. ranks the most relevant note sections
6. shows the results
7. lets me open the relevant note

The Question Finder must not automatically solve the question.

Its purpose is to direct me to my own relevant notes.

---

## 39. STRICT NON-GENERATIVE-AI REQUIREMENT

The Question Finder must not use:

- ChatGPT
- Claude
- Gemini
- LLM APIs
- local LLMs
- generative AI
- multimodal generative models
- embeddings
- vector embeddings generated by ML models
- VLMs
- answer-generation systems
- semantic cloud AI services

Do not send captured questions to generative AI services.

Do not implement hidden AI fallbacks.

The matching pipeline must be deterministic and explainable.

---

## 40. OCR POLICY

The application may use conventional OCR to convert note images and captured questions into text.

Potential implementations include:

- Tesseract
- Windows OCR

Prefer local OCR.

However, OCR technology itself can fall under different definitions of AI/ML depending on an institution's rules.

Therefore clearly communicate inside documentation that:

> **Non-generative does not automatically mean permitted in every assessment. The user must comply with their institution's assessment rules.**

Do not claim formal assessment compliance merely because the app does not use LLMs.

---

## 41. ASSESSMENT / COMPLIANCE MODE

Implement a dedicated **Assessment Mode**.

When enabled, the system should enforce a stricter operating profile.

Potential status display:

```text
NON-AI MODE
LOCAL SEARCH ONLY
```

Assessment Mode should be capable of blocking:

- LLM APIs
- generative AI connections
- embedding services
- cloud OCR
- captured-question uploads
- automatic answer generation
- web searches
- unapproved network search

Question retrieval should use only:

- prebuilt local index
- approved local resources

Settings should not casually allow prohibited systems to be re-enabled during an active Assessment Mode session.

A deliberate exit process is acceptable.

---

## 42. ASSESSMENT MODE NETWORK BOUNDARY

Design the application so Question Finder can operate completely offline after notes have been synchronised and indexed.

The preferred assessment workflow is:

Before assessment:

Notion → sync → download → OCR → build local index.

During assessment:

Question screenshot → local OCR → local search → local ranked results.

No Notion connection should be required during local search.

---

## 43. QUESTION FINDER COURSE CONTEXT

Before searching, the user should be able to choose a course/class.

This significantly reduces the search space.

Examples:

- Engineering Mathematics
- Systems Engineering
- Materials
- Mechanics

Course selection may be associated with:

- workspace
- layout
- macro profile

The currently selected course should be clearly visible but not visually dominant.

---

## 44. NOTION NOTES STRUCTURE

My Notion notes commonly have:

- different pages for each course
- different pages/areas for each week
- a table of contents at the top
- headings at different hierarchy levels
- most actual notes stored as images rather than normal Notion text

Therefore merely reading Notion page text is not sufficient.

The system must also index images contained inside my notes.

---

## 45. NOTION SYNCHRONISATION

Create a Notion integration layer.

It should:

1. connect using a user-provided authorised integration/token
2. read allowed pages
3. identify course structure
4. retrieve page hierarchy
5. identify headings
6. find image/file blocks
7. download images as required
8. OCR new/changed images
9. update local database
10. retain links back to source pages

Respect Notion API:

- authentication
- rate limits
- pagination
- error handling
- temporary file URLs

Notion-hosted image URLs may expire.

Download required content promptly and cache appropriately.

---

## 46. NOTION TOKEN SECURITY

Do not store the Notion token as plaintext.

Use appropriate Windows credential protection such as:

- DPAPI
- Windows Credential Manager

Do not write the token to logs.

---

## 47. INCREMENTAL NOTION SYNC

Do not completely OCR the entire library after every launch.

Maintain incremental state using values such as:

- last edited time
- source block ID
- page ID
- content hash
- image hash
- cached OCR status

Only reprocess changed or new content.

---

## 48. LOCAL NOTE LIBRARY

Build a local representation of my notes.

Suggested hierarchy:

```text
Course
→ Week
→ Page
→ Section / Heading
→ Note Image / Text Block
```

Each searchable item should retain useful metadata such as:

- course
- week
- page name
- heading
- heading hierarchy
- source text
- OCR text
- OCR confidence
- source type
- source page ID
- source block ID
- Notion page URL
- local cache identifier
- image hash
- last indexed time

---

## 49. LOCAL DATABASE

Use SQLite with FTS5 for deterministic full-text search unless testing demonstrates a materially better local alternative.

Enable **Write-Ahead Logging (WAL)** where supported so Question Finder reads can continue while background OCR/indexing commits updates. Configure a sensible SQLite busy timeout and keep transactions deliberately bounded.

Implementation rules:

- use one controlled write path/service rather than many unrelated concurrent writers
- batch OCR/index updates into transactions instead of committing every token or field individually
- use short-lived read transactions for Question Finder searches
- do not hold a write transaction open while performing OCR, downloading images, or doing other slow work
- checkpoint WAL deliberately during safe/idle maintenance periods rather than on the latency-critical search path
- use parameterised SQL exclusively
- handle `SQLITE_BUSY`/locked states with bounded retry/backoff rather than freezing the UI
- prevent schema migrations, destructive rebuilds, and incompatible maintenance from running concurrently with normal indexing/search writes

Separate:

- metadata storage
- searchable text
- index state
- synchronisation state

Design versioned migrations properly so future application versions can evolve the database safely. Interrupted writes or WAL recovery after a crash must not require deleting the whole Study Library.

---

## 50. PRE-INDEXING

Do the expensive OCR work ahead of time.

Do not OCR every page every time a question is captured.

Pipeline:

```text
NOTION
↓
SYNC
↓
DOWNLOAD NOTE IMAGES
↓
OCR ONCE
↓
NORMALISE FEATURES
↓
LOCAL INDEX
↓
CACHE
```

Question-time pipeline:

```text
SCREENSHOT
↓
OCR
↓
FEATURE EXTRACTION
↓
LOCAL DATABASE QUERY
↓
RANK
↓
SHOW RESULTS
```

This should make retrieval feel immediate.

---

## 51. DETERMINISTIC FEATURE EXTRACTION

Question Finder must separate useful information into feature categories.

Example:

Question:

`Determine the maximum bending stress when M = 3.2 kNm...`

Extract:

### WORDS

- determine
- maximum
- bending
- stress

### VARIABLES / SYMBOLS

- M

### NUMBERS

- 3.2

### UNITS

- kNm

Where possible, recognise mathematical expressions separately.

This is particularly important for engineering material.

---

## 52. MATH-AWARE SEARCH

Do not rely purely on ordinary English keyword search.

Engineering notes may contain:

- σ
- ε
- M
- I
- E
- F
- A
- My/I
- F/A
- derivatives
- integrals
- units
- formulas

Search ranking should give increased importance to exact overlap involving:

- variables
- units
- uncommon symbols
- equations
- expressions

---

## 53. OCR NORMALISATION

OCR commonly confuses mathematical symbols.

Examples:

σ → o  
I → l  
0 → O  
1 → l

Implement conservative deterministic normalisation rules where they improve reliability.

Do not aggressively rewrite text in ways that generate false matches.

Retain original OCR output alongside normalised text for debugging.

---

## 54. SEARCH PIPELINE

A reasonable deterministic search pipeline is:

1. Unicode normalisation
2. lowercase where appropriate
3. punctuation handling
4. tokenisation
5. variable extraction
6. number extraction
7. unit extraction
8. symbol extraction
9. expression extraction where feasible
10. deterministic stemming/lemmatisation if useful
11. SQLite FTS/BM25 search
12. heading boost
13. exact phrase boost
14. variable boost
15. expression boost
16. course constraint
17. optional week constraint
18. OCR-confidence weighting
19. small deterministic fuzzy fallback

Avoid opaque ranking algorithms.

---

## 55. SEARCH RESULT SCORING

Ranking should remain deterministic.

The score can combine things like:

```text
Base BM25 relevance
+ heading relevance
+ exact variable overlap
+ exact symbol overlap
+ formula/expression overlap
+ exact phrase bonus
+ course filter
+ OCR confidence adjustment
```

Do not label the resulting value as "AI confidence."

If shown as a percentage, call it something such as:

- Match Score
- Retrieval Confidence
- Relevance

and make clear it is derived from deterministic ranking.

---

## 56. EXPLAINABLE RESULTS

Every result should be able to show why it matched.

Examples:

Matched:

- "bending"
- "stress"
- variable M
- variable I
- expression My/I
- heading "Flexure Formula"

This is important for:

- transparency
- debugging
- user trust
- proving the system is acting as a search tool rather than answering

---

## 57. QUESTION FINDER UI

Keep the default Question Finder panel extremely simple.

Before capture, approximately:

```text
QUESTION FINDER

Course: Engineering Mechanics

[ Draw Question ]
```

Avoid displaying unnecessary technical controls.

After capture, transform the panel into results.

Show approximately the top three results by default.

Example:

```text
WEEK 6
BENDING → FLEXURE FORMULA

Matched:
M • I • stress

Retrieval match: 92%

[ OPEN ]
```

Allow expanding for:

- more context
- OCR text
- reason for match
- additional results

---

## 58. OPENING NOTION RESULTS

When the user selects a result, open the relevant Notion location.

Exact block-level deep links may not always be possible.

Therefore use a robust fallback:

1. Open relevant Notion page.
2. Show the section/heading breadcrumb in Study HUD.
3. Optionally show a small preview.
4. If an exact source link exists, use it.
5. Otherwise provide page + heading context.

Do not make exact block navigation a hard dependency.

---

## 59. OCR FAILURE HANDLING

Question OCR will occasionally fail, particularly with equations.

If OCR confidence is low:

- clearly show the detected text
- highlight uncertain areas/tokens if available
- provide quick correction
- allow search anyway
- allow recapture

The correction interaction should be fast.

Do not show a large modal dialog unless necessary.

---

## 60. LIBRARY MANAGEMENT UI

The main application should include a visible Study Library.

Example:

```text
Engineering Mathematics

14 weeks
37 pages
684 note images
681 indexed
3 need review
Last sync: 4 minutes ago
```

Allow browsing:

```text
Course
→ Week
→ Section
→ Images
```

Show useful states:

- indexed
- waiting
- OCR failed
- low confidence
- source unavailable
- changed
- needs reprocessing

This prevents the indexing system from becoming a black box.

---

## 61. REPROCESSING

Allow users to:

- re-OCR one item
- re-index one item
- re-index a page
- re-index a week
- re-index a course
- rebuild complete index

Require appropriate confirmation before expensive destructive operations.

---

## 62. THEMING SYSTEM

Themes should be a first-class system rather than hard-coded colours.

Future themes may include:

- Retro
- Microsoft Paint inspired
- Old Macintosh inspired
- minimal modern
- custom colour theme

Do not hard-code theme logic into panel behaviour.

---

## 63. THEME TOKENS

Themes should support categories such as:

### Colour

- background
- surface
- secondary surface
- accent
- text
- secondary text
- border
- warning
- error
- success

### Typography

- title font
- body font
- monospace font
- scale
- weights

### Geometry

- corner radius
- border width
- button height
- spacing
- panel padding
- control padding

### Effects

- shadows
- opacity
- textures
- animations
- highlight style

### Assets

- icons
- cursors
- window controls
- reveal arrows
- resize handles

A theme should change presentation, not functionality.

---

## 64. COLOUR WHEEL

Provide a colour wheel or equivalent picker allowing the user to quickly customise the theme accent.

Include:

- colour wheel
- RGB/HEX entry if practical
- recent colours
- reset to theme default

Automatically protect readability.

If the user selects a light accent, choose appropriate contrasting text/icon colour.

Allow advanced overrides later if desired.

---

## 65. FUTURE DETAILED THEMES

Theme architecture must be able to support substantial skins later.

For example:

### Old Macintosh

- period-inspired borders
- monochrome/pixel-like assets
- Macintosh-style controls

### Microsoft Paint Inspired

- chunky controls
- classic tool-style components
- theme-specific reveal tab

### Retro Terminal

- terminal-inspired typography
- sharp geometry

These should be achievable without rewriting the underlying panel logic.

---

## 66. MOTION DESIGN

Animations should be restrained.

The program is a productivity tool.

Suggested behaviour:

Panel snap:  
~100–150 ms.

Panel collapse:  
~100–180 ms.

Workspace switch:  
short fade/slide.

HUD activation:  
slight opacity/brightness response.

Panel minimise/collapse:  
short functional transition.

Avoid:

- long bouncy animations
- unnecessary blur
- dramatic motion
- excessive effects

The HUD should feel mechanically integrated into Windows.

---

## 67. PERFORMANCE TARGETS

Treat these as engineering goals rather than absolute guarantees, and verify them with repeatable profiling rather than intuition.

HUD response to interaction:  
approximately under 50 ms perceived latency.

Panel drag/snap:  
target fluid 60 FPS behaviour.

Typical snap processing:  
within a frame where practical.

Macro activation:  
approximately under 20 ms before action begins where technically achievable.

Search after OCR:  
approximately under 100 ms for typical local indexes.

Question capture → OCR → results:  
target around 1.5 seconds or less on a typical region and modern computer where feasible.

Workspace/layout switch:  
approximately under 100 ms once resources are available.

Idle Ghost-mode CPU usage:  
target near-zero/measurement-noise CPU usage on a modern system when no sync/index job is active. Avoid continuous high-frequency polling.

Steady-state private working set with HUD loaded but OCR/sync idle:  
target **below approximately 150 MB where practical**. Treat this as a profiling target, not a reason to compromise reliability or accessibility.

Global hook callback work:  
target comfortably below 1 ms in normal cases; callbacks must never wait for UI/database/network work.

Do not block the UI thread with:

- OCR
- Notion synchronisation
- database rebuild
- image processing
- long-running file operations

---

## 68. BACKGROUND TASKS

Use cancellable background operations for:

- Notion syncing
- image downloads
- OCR indexing
- database maintenance

Background work must be controlled by a **resource scheduler** rather than allowing an unbounded queue of OCR/download tasks to saturate the machine.

Implementation requirements:

- use a bounded queue/channel for OCR/index jobs
- cap OCR concurrency; a reasonable initial default is 1-2 concurrent OCR workers, then tune from profiling
- allow Pause, Resume, and Cancel for long indexing jobs
- query Windows power state (for example through `GetSystemPowerStatus` or an equivalent supported API) and reduce concurrency or optionally pause heavy indexing while on battery/Battery Saver
- provide a user override such as `Pause heavy indexing on battery`
- decoded image buffers must be disposed/released immediately after each item is processed
- do not preload the complete Notion image library into memory
- use backpressure so Notion download speed cannot create an unlimited in-memory OCR backlog
- assign lower scheduling priority to bulk indexing than interactive capture/search/HUD work; never use real-time thread priority
- a newly submitted foreground Question Finder OCR job must be able to jump ahead of bulk background indexing jobs

Show quiet progress status.

Do not cover study material with large progress dialogs.

Prefer:

- subtle toast
- status in control capsule
- status inside management window

---

## 69. ERROR HANDLING

Handle failures cleanly.

Examples:

Notion:

- 401
- 403
- 429
- missing page
- expired file URL

OCR:

- unreadable image
- unsupported format
- low confidence

Macros:

- conflicting trigger
- input injection denied
- elevated target
- missing application

Panels:

- invalid monitor
- off-screen location
- corrupted layout

Database:

- migration failure
- corrupted cache

The application should recover where possible rather than crash.

---

## 70. LOGGING

Provide structured **local-only** logs useful for debugging and crash diagnosis. No diagnostics are automatically uploaded.

Do not log:

- Notion authentication tokens
- unnecessary full note content
- captured questions by default
- sensitive clipboard contents

Use log levels.

On an unhandled application/background exception, generate a sanitised local crash record containing only information needed to diagnose the failure, such as:

- Study HUD version/build
- UTC/local timestamp
- Windows version
- failing module/subsystem
- exception type/message/stack trace
- recent sanitised diagnostic events
- high-level active workspace/layout/mode identifiers where non-sensitive

Do not include Notion tokens, clipboard contents, screenshot pixels, captured-question text, full note text, passwords, or authentication headers.

Optional memory/minidump capture must be **disabled by default** because process memory can contain sensitive note or clipboard data. If added later, require explicit user action/consent and warn about its contents.

Provide an easy way to inspect and manually export a diagnostics bundle without exposing credentials.

---

## 71. PRIVACY

The application should follow a local-first philosophy.

Prefer:

- local OCR
- local database
- local screenshots
- local ranking
- local settings

Do not upload note content unless explicitly required for a user-enabled external integration.

No generative AI services should receive Question Finder information.

---

## 72. ONBOARDING

Create a short first-run onboarding flow.

Suggested sequence:

1. Welcome
2. Choose monitors / confirm layout
3. Select Hold-to-Interact trigger
4. Configure screenshot trigger
5. Create first course
6. Connect Notion
7. Choose pages to sync
8. Build first index
9. Create/choose initial macro profile
10. Try HUD
11. Explain Ghost / Active / Edit states
12. Explain Assessment Mode

Do not force the user to configure every advanced feature during onboarding.

---

## 73. EDIT MODE UX

Edit Mode should make panel configuration obvious.

When entering Edit Mode:

- HUD should visually indicate editing state
- drag handles may appear
- resize handles appear where useful
- snap previews become visible
- panel settings button becomes available

When leaving Edit Mode:

- handles disappear
- layout becomes locked
- accidental movement is impossible

---

## 74. DOCKING FEEDBACK

During dragging:

- show target edge
- show expected panel alignment
- subtly highlight compatible docking destination

On successful docking:

- brief animation
- no distracting sound by default

Allow users to disable animations if desired.

---

## 75. DETACHING PANELS

A docked panel should detach when dragged beyond a sensible detachment threshold.

Avoid accidental detachment from minor pointer movement.

The behaviour should feel similar to magnetic UI components rather than rigid window snapping.

---

## 76. FULLSCREEN DETECTION

Provide configurable behaviour for fullscreen applications.

Options:

- do nothing
- hide all HUD
- collapse HUD
- disable macros
- use per-application behaviour

This must be configurable.

---

## 77. COURSE PROFILES

Consider allowing courses to remember associated:

- workspace
- macro profile
- HUD layout
- Notion source
- preferred monitor

Example:

Switching to Engineering Mathematics may automatically:

- activate Note Taking workspace
- load Mathematics macros
- select Mathematics Notion index
- restore Mathematics panel layout

The user should be able to disable automatic behaviour.

---

## 78. STATUS INDICATORS

Keep status displays minimal.

Useful statuses:

- current workspace
- current course
- index ready
- syncing
- Assessment Mode active
- HUD locked/editing

Do not permanently display irrelevant diagnostic information.

---

## 79. ACCESSIBILITY

Ensure:

- keyboard navigation where practical
- readable text scaling
- good contrast
- focus indicators
- reduced motion option
- controls remain usable at Windows accessibility scaling
- user can change activation shortcut if physical input is difficult

Do not sacrifice usability for visual styling.

---

## 80. CONFIGURATION STORAGE

Store settings in a structured versioned format.

Possible approach:

- JSON for user-visible configuration
- SQLite for content/index state

Configuration should be migration-safe.

Support future:

- export settings
- import settings
- backup
- restore defaults

---

## 81. PROJECT ARCHITECTURE

Use a modular architecture.

Potential projects/modules:

```text
StudyHud.App
StudyHud.Core
StudyHud.Windows
StudyHud.Overlay
StudyHud.Macros
StudyHud.Capture
StudyHud.Notion
StudyHud.Ocr
StudyHud.Search
StudyHud.Storage
StudyHud.Theming
StudyHud.Tests
```

Exact project names can differ.

The important requirement is clean separation of responsibilities.

---

## 82. SERVICE ABSTRACTIONS

Create interfaces around systems likely to change.

Examples:

- IOcrService
- INoteSource
- ISearchIndex
- IGlobalInputService
- IMacroExecutor
- ICaptureService
- IMonitorService
- IThemeService
- ILayoutService
- ICredentialStore

Avoid tightly coupling UI components to Tesseract, Notion, SQLite or Windows API calls.

---

## 83. WINDOWS OVERLAY DETAILS

Investigate and appropriately use native concepts such as:

- `WS_EX_TOPMOST`
- `WS_EX_NOACTIVATE`
- `WS_EX_TRANSPARENT` when Ghost click-through is required
- `WS_EX_TOOLWINDOW` for helper HWNDs that must not appear as normal taskbar/application windows
- `SetWindowPos`
- `GetWindowLongPtr`
- `SetWindowLongPtr`
- WPF `HwndSource`/native interop where a dedicated helper HWND is required

Do not blindly apply `WS_EX_TRANSPARENT` to every HWND.

The overlay needs distinct modes.

Ghost:  
pass-through.

Active/Edit:  
interactive.

For controls that must remain interactive while the main monitor overlay is Ghost/click-through, prefer a **small dedicated owned helper HWND** with an exact hit region rather than trying to create a clickable island inside a fully transparent WPF hit-test surface.

Initial dedicated interactive islands should be limited to cases such as:

- edge-collapse reveal tabs
- optional always-interactive control capsule
- any future micro-control proven by testing to require independent hit-testing

These HWNDs must:

- never create a large invisible input-blocking rectangle
- remain visually/logically attached to their owning panel
- follow monitor/DPI/layout changes
- use no-activate behaviour where feasible
- stay in the correct topmost Z-order relative to Study HUD
- be created/destroyed deterministically as panel visibility changes
- not appear in Alt-Tab/taskbar as separate applications

The user must still perceive one Study HUD, not a collection of independent windows.

---

## 84. DPI AWARENESS

Use Per-Monitor V2 DPI awareness.

Test at combinations including:

- 100%
- 125%
- 150%
- 175%
- 200%

Movement between monitors must not cause:

- incorrect panel size
- jumping
- blurred rendering
- incorrect capture coordinates
- docking gaps

---

## 85. SCREEN CAPTURE COORDINATES

Be extremely careful about:

- physical pixels
- WPF device-independent units
- DPI conversions
- multi-monitor virtual desktop coordinates

The screenshot the user sees inside the selection rectangle must match the pixels actually captured.

Write reusable coordinate conversion helpers and test them.

---

## 86. TESTING REQUIREMENTS

Create automated tests wherever practical.

At minimum test logic for:

- snapping
- docking graph
- normalized layout restoration
- responsive panel states
- macro parsing
- action sequencing
- course/profile switching
- search feature extraction
- deterministic ranking
- query normalization
- Notion incremental sync decisions
- database migrations

---

## 87. INTEGRATION TESTING

Test:

- screenshot capture
- global hotkeys
- mouse side button capture
- Unicode text injection
- clipboard actions
- Notion sync
- OCR
- opening note results

Use abstractions/mocks where native automation is unsuitable for unit testing.

---

## 88. MANUAL QA MATRIX

Manually test:

- Single monitor
- Dual monitors
- Monitors with different resolutions
- Monitors with different DPI scaling
- Primary monitor changed
- Monitor unplugged while app is running
- Taskbar top
- Taskbar bottom
- Taskbar left/right where Windows configuration allows
- Fullscreen app
- Elevated app
- Notion offline
- Notion rate-limited
- Invalid Notion token
- OCR failure
- Thousands of note images
- Long macros
- Conflicting global shortcuts
- Panel collapse on every screen edge
- Dock group collapse
- Question Finder in offline Assessment Mode

---

## 89. NO-AI ENFORCEMENT TESTS

Include tests ensuring Question Finder cannot silently fall back to prohibited services.

Assessment Mode should have an explicit policy layer.

Test that attempts to call:

- LLM provider
- embedding provider
- cloud AI OCR
- web search

are blocked when the policy forbids them.

The application should be able to explain which policy prevented an action.

---

## 90. INITIAL PRODUCT SCOPE

The first polished version should prioritise:

1. Overlay engine
2. Ghost / Active / Edit
3. Hold-to-Interact
4. Multi-monitor support
5. Movable/resizable panels
6. Responsive panel layouts
7. Snapping
8. Persistent docking
9. Edge-collapse arrows
10. Workspace system
11. Macro engine
12. Macro profiles
13. One-gesture screenshot system
14. Course management
15. Notion sync
16. OCR image indexing
17. Local deterministic search
18. Question Finder results
19. Assessment Mode
20. Theme framework
21. Colour picker
22. Settings application
23. Library/index viewer

Build the foundation correctly before adding novelty widgets.

---

## 91. IMPORTANT V1 RELIABILITY PRIORITIES

Some functionality is inherently more fragile on Windows.

The following must be treated as baseline dependable features:

- screenshot to clipboard
- global keyboard macro
- global mouse side-button macro
- local search
- Notion pre-indexing
- panel layout
- docking
- edge collapse

Automatic focus switching and pasting into arbitrary applications should be treated as optional/advanced until proven reliable.

Do not make the entire note-taking workflow depend on fragile foreground-window automation.

---

## 92. USER EXPERIENCE STANDARD

The application should feel comparable to a high-quality commercial utility.

Avoid:

- default-looking WPF controls
- inconsistent spacing
- giant settings pages
- random animations
- technical terminology where user-friendly terminology exists
- modal dialogs for routine actions
- controls appearing/disappearing unpredictably
- clutter

Use clear hierarchy and progressive disclosure.

Advanced options belong behind advanced sections.

---

## 93. VISUAL PHILOSOPHY

Do not design the app around one particular theme yet.

Create a neutral polished default design.

It should:

- have consistent spacing
- strong visual hierarchy
- support light/dark style if appropriate
- maintain excellent readability
- use subtle borders/shadows
- remain compact
- look intentional at every panel size

The theming engine should later allow substantially different visual styles.

---

## 94. INTERACTION PHILOSOPHY

The best HUD is one that feels absent until needed.

Therefore normal study workflow should be:

Study normally.

HUD information remains visible if wanted.

Pointer passes through HUD.

Need HUD:

hold activation trigger.

Interact.

Release.

HUD immediately becomes passive again.

Need more screen space:

click edge-collapse arrow.

Panel slides away.

Need it back:

click remaining arrow.

Need to rearrange:

enter Edit Mode.

Need a screenshot:

hold screenshot trigger.

drag.

release.

Done.

Need question notes:

switch Question Finder.

draw question.

open relevant note.

The user should rarely need to enter the main settings window during normal study.

---

## 95. QUESTION FINDER INTERACTION PHILOSOPHY

Question Finder must behave as a **navigation and retrieval assistant**, not a solver.

The experience should be:

Select course.

Draw question.

See relevant notes.

See why each note matched.

Open note.

The user performs the actual academic reasoning.

---

## 96. NOTION SEARCH PERFORMANCE

The system should comfortably support:

- multiple courses
- many weeks per course
- hundreds or thousands of note images

Search performance should remain primarily dependent on the local index rather than requiring linear scanning.

Optimise database queries appropriately.

---

## 97. INDEX CACHE

Store OCR results so the same image does not need repeated OCR.

Use image/content hashes.

If content hash is unchanged:

reuse cached OCR.

If OCR engine/configuration version changes:

provide a controlled reprocessing path.

---

## 98. LIBRARY HEALTH

Provide an optional health summary.

Example:

```text
Engineering Mechanics

Index Ready

1,238 searchable items

5 low-confidence OCR items

2 source files unavailable

Last successful sync: 10:32 AM
```

This should help diagnose incomplete search results.

---

## 99. QUIET NOTIFICATIONS

Use small non-blocking notifications.

Examples:

- "Notes synced."
- "3 new images indexed."
- "Macro unavailable in this application."
- "Capture cancelled."
- "Notion connection lost — local search still available."

Notifications should disappear automatically unless user action is required.

---

## 100. SECURITY AND COMMAND MACROS

If macros support shell commands/program execution:

- clearly distinguish them from ordinary safe actions
- require deliberate configuration
- never execute arbitrary content extracted from OCR
- never allow a note/question to generate and execute a command
- validate paths
- avoid command injection

OCR/search content must always be treated as untrusted data.

---

## 101. THREADING

Never perform heavyweight work synchronously on the UI thread.

Use async operations and cancellation tokens for:

- syncing
- downloads
- OCR
- search rebuilds
- large database writes

Keep panel dragging and HUD interactions isolated from background processing.

---

## 102. CRASH RECOVERY

Persist critical layout/configuration changes safely.

Use atomic file writes where practical.

If the application crashes:

- previous valid settings should survive
- index should remain recoverable
- one corrupt cache entry should not destroy the entire library

---

## 103. STARTUP

Startup should be fast.

Do not block initial HUD availability while performing a full Notion sync.

Recommended behaviour:

1. Load configuration.
2. Load local index.
3. Show HUD.
4. Perform permitted background sync afterwards.

If local data exists, Question Finder should remain usable offline immediately.

---

## 104. SYSTEM TRAY

Provide an optional system tray icon.

Useful commands:

- Show Settings
- Hide/Show HUD
- Workspace
- Assessment Mode status
- Exit

Do not make the tray icon the only way to access the app.

It must also work correctly when pinned/launched from Windows taskbar/start menu.

---

## 105. APPLICATION LIFECYCLE

Closing the settings window should not necessarily terminate the HUD.

Use clear behaviour:

- Close settings = settings window closes
- Exit Study HUD = application terminates

Provide an option for users who prefer close to exit.

---

## 106. FUTURE EXTENSIBILITY

Architect the system so additional panels could later be created without redesigning the application.

Possible future modules:

- timer
- task list
- formula library
- calculator
- clipboard history
- study schedule
- quick file access
- flashcards
- reference lookup

Do not implement these now unless needed for core architecture.

---

## 107. DO NOT IMPLEMENT GENERATIVE ANSWERING

Do not add:

- "Solve Question"
- "Ask AI"
- "Generate Answer"
- automatic solution generation

into the Question Finder.

If AI features are ever added elsewhere in the product in the future, they must be completely separated from Question Finder and completely disabled by Assessment Mode.

For this implementation, focus on the non-generative design defined here.

---

## 108. CODING QUALITY

Use:

- clear naming
- nullable reference types
- dependency injection
- asynchronous programming correctly
- cancellation support
- sensible logging
- configuration validation
- clean exception handling
- meaningful comments where needed
- XML documentation for public/core APIs where useful

Avoid:

- giant classes
- static global state
- magic numbers
- duplicated Windows interop code
- business logic inside click handlers
- arbitrary sleeps
- fragile UI automation where a proper API exists

---

## 109. DOCUMENTATION

Create internal project documentation explaining:

- architecture
- module responsibilities
- HUD/window model
- coordinate systems
- DPI strategy
- docking model
- macro model
- OCR pipeline
- search ranking
- Notion sync
- Assessment Mode policy
- configuration format
- build/run instructions

Create a HANDOFF.md suitable for another coding AI or developer to understand the complete project.

Keep it updated as the architecture changes.

---

## 110. IMPLEMENTATION APPROACH

Do not attempt to build every system simultaneously in one giant uncontrolled pass.

Develop in stable vertical phases.

Suggested order:

### Phase 1 — Foundation

- solution structure
- settings/configuration
- logging
- dependency injection
- monitor service
- basic overlay

### Phase 2 — HUD Engine

- Ghost/Active/Edit
- Hold-to-Interact
- panels
- resizing
- monitor movement
- persistence

### Phase 3 — Docking

- snapping
- dock relationships
- groups
- layout save/restore
- edge collapse

### Phase 4 — Workspaces

- workspace switching
- control capsule
- workspace layouts

### Phase 5 — Macro Engine

- global input
- triggers
- actions
- profiles
- macro editor

### Phase 6 — Capture

- custom region capture
- clipboard output
- multi-monitor/DPI correctness

### Phase 7 — Study Library

- courses
- SQLite
- Notion connector
- syncing
- cache

### Phase 8 — OCR

- local OCR service
- indexing
- OCR status
- correction/reprocessing

### Phase 9 — Search

- deterministic parser
- mathematical features
- FTS/BM25
- deterministic ranking
- explanations

### Phase 10 — Question Finder

- capture
- OCR
- search
- results
- open Notion

### Phase 11 — Assessment Mode

- policy enforcement
- network restrictions
- UI indicator
- tests

### Phase 12 — Themes

- full token engine
- colour wheel
- default theme
- future-theme hooks

### Phase 13 — Polish

- animation
- onboarding
- error states
- accessibility
- performance
- QA

Each phase should leave the application runnable.

---

## 111. DO NOT FAKE FEATURES

Do not create buttons that only display placeholder messages and then call them implemented.

If a system cannot yet be completed:

- clearly mark it incomplete
- create clean interfaces where appropriate
- document the missing implementation

Prefer fewer real features over many fake ones.

---

## 112. VALIDATE ASSUMPTIONS

For native Windows behaviour that is uncertain:

1. research/document the Windows behaviour
2. create the smallest viable technical test
3. confirm behaviour
4. then build it into the product

This is especially important for:

- click-through windows
- no-activate behaviour
- global mouse capture
- foreground app pasting
- mixed-DPI screen capture
- elevated applications
- edge reveal tabs

Do not rely on assumptions.

---

## 113. ACCEPTANCE CRITERIA — HUD

A successful HUD implementation should demonstrate:

- panel remains above normal windows
- Ghost mode does not block underlying clicks
- Hold-to-Interact makes HUD usable
- release returns it to click-through
- Edit Mode allows movement/resizing
- panels cannot accidentally move outside Edit Mode
- panels remain crisp across mixed DPI displays
- layouts survive restart
- monitor removal does not permanently lose panels

---

## 114. ACCEPTANCE CRITERIA — RESPONSIVE PANELS

Resize a panel through all supported dimensions.

At no point should:

- half a word appear
- a button become partly clipped
- content overlap
- text render outside panel bounds
- controls become impossible to click

Compact/Normal/Expanded transitions must appear intentional.

---

## 115. ACCEPTANCE CRITERIA — DOCKING

Demonstrate:

- side panel docking to bottom panel
- connected edges remain aligned
- dock relationship persists after restart
- moving group preserves relationship
- individual panel can detach
- resizing does not introduce visible gaps
- cross-monitor invalid docks are handled safely

---

## 116. ACCEPTANCE CRITERIA — EDGE COLLAPSE

For every screen edge:

1. dock panel
2. click collapse arrow
3. panel slides off-screen
4. only reveal tab remains
5. underlying application remains clickable
6. click reveal tab
7. panel returns to exact previous geometry

Repeat across monitors with different DPI scaling.

---

## 117. ACCEPTANCE CRITERIA — MACRO SYSTEM

Demonstrate:

- keyboard shortcut macro
- mouse side-button macro
- multi-action sequence
- Unicode text
- delays
- app-restricted macro
- profile switching
- conflict warning
- graceful failure against elevated application

---

## 118. ACCEPTANCE CRITERIA — SCREENSHOT

Using one held trigger:

1. press/hold trigger
2. drag region
3. release
4. image immediately appears in clipboard

Test on every monitor.

The selected rectangle must match captured pixels.

---

## 119. ACCEPTANCE CRITERIA — NOTION INDEX

Demonstrate:

- authorised Notion connection
- selected course/page sync
- image blocks discovered
- images locally processed
- OCR stored
- second sync skips unchanged images
- changed image reprocesses correctly
- offline search still works after disconnect

---

## 120. ACCEPTANCE CRITERIA — QUESTION FINDER

Given a captured engineering question:

- OCR produces usable text/features
- course filter is respected
- results appear quickly
- top results contain likely relevant sections
- matched terms/variables are shown
- no generative response is produced
- result opens corresponding notes
- offline mode works

---

## 121. ACCEPTANCE CRITERIA — ASSESSMENT MODE

When Assessment Mode is enabled:

- Question Finder still uses local index
- prohibited Study HUD network/AI systems cannot be accessed
- no question is uploaded
- Study HUD does not disable the Windows network adapter or block unrelated applications
- institution-required proctoring/network software can continue to use the network normally
- status is clearly visible
- blocked Study HUD operations explain why they were blocked
- local notes remain usable

---

## 122. DESIGN PRIORITY ORDER

When requirements compete, prioritise in this order:

1. Reliability
2. Non-interference with normal computer use
3. Data/privacy protection
4. Assessment/compliance boundaries
5. Speed
6. UI clarity
7. Visual polish
8. Advanced automation
9. Novelty

A beautiful feature that regularly interferes with studying is a failure.

---

## 123. IMPORTANT PRODUCT PRINCIPLE

The application should behave like part of the desktop environment rather than another application demanding attention.

The ideal experience is:

- Invisible when unnecessary.
- Instant when required.
- Consistent.
- Fast.
- Predictable.
- Customisable.
- Explainable.
- Local-first.

---

## 124. FINAL DELIVERABLE EXPECTATION

Implement this as a real maintainable software project rather than a prototype mock-up.

For every substantial feature:

- implement the functional backend
- connect it to the real UI
- handle errors
- persist required state
- test it
- document it

Keep the codebase organised enough that a new developer or coding AI can continue development from HANDOFF.md without needing to reverse engineer the project.

Before considering a phase complete:

1. build the project
2. fix compiler errors
3. run applicable tests
4. manually test the actual workflow
5. resolve obvious UX defects
6. update HANDOFF.md
7. commit changes if working in version control

Do not remove existing working functionality to make a new feature easier.

Do not silently change the requested behaviour.

If a requirement is genuinely impossible or heavily constrained by Windows, preserve the user's intended workflow as closely as possible, implement the strongest technically reliable alternative, and document exactly why the original behaviour could not be achieved.

The final result should be a highly polished, modular Windows Study HUD that materially improves study speed without getting in the way of the applications the user is actually working in.

---

# Coordinated Processes and State Behaviour

The following sections make the runtime processes explicit so the implementation behaves as one coherent product rather than a set of disconnected features.

---

## 125. SINGLE COHERENT STUDY TOOL — GLOBAL REQUIREMENT

Study HUD must not feel like a collection of unrelated floating widgets, shortcuts, macros, and utilities.

It must behave as **one coordinated study environment**.

The following systems must understand and react to shared application state:

- Current Workspace
- Current Course
- Current Macro Profile
- Current HUD Layout
- Current Monitor Configuration
- Current Assessment/Compliance State
- HUD Visibility State
- HUD Interaction State
- Application Exclusion State
- Current Theme

Do not make each panel maintain an unrelated version of this state.

Use a central, well-structured application/session state model with appropriate services/events so changes propagate predictably.

For example:

```text
Note Taking → Question Finder

Save current Note Taking panel state
↓
Change current Workspace
↓
Load Question Finder layout
↓
Activate Question Finder macro profile if automatic profiles are enabled
↓
Preserve or apply the appropriate current Course
↓
Update the control capsule
↓
Render the Question Finder panels
↓
Keep Assessment Mode restrictions active if Assessment Mode is already enabled
```

The user should experience this as **one workspace change**, not several independent operations.

---

## 126. GLOBAL STATE PRIORITY

Some application states override others.

Use a clear priority model.

A sensible priority is:

1. Application Exit
2. Panic/Hide HUD
3. Security / Assessment policy
4. Application exclusions / fullscreen rules
5. Capture mode
6. Edit Mode
7. Temporary Hold-to-Interact Active state
8. Normal Ghost state

Example:

If the HUD is in Edit Mode, releasing the Hold-to-Interact key must not unexpectedly return the HUD to Ghost Mode.

If the Panic Hide command is activated while editing, the HUD must hide.

If Assessment Mode blocks a macro action, changing macro profile must not bypass the restriction.

Avoid state conflicts caused by individual components independently toggling native window behaviour.

---

## 127. HUD INTERACTION STATE MACHINE

Implement Ghost, Active and Edit as an explicit state machine rather than scattered boolean flags.

Normal sequence:

```text
GHOST

User presses and holds interaction trigger.
↓
ACTIVE

User interacts with HUD.

User releases interaction trigger.
↓
GHOST
```

Edit Mode is different:

```text
GHOST or ACTIVE

User deliberately enters Edit Mode.
↓
EDIT
```

The user may:

- move panels
- resize panels
- dock panels
- detach panels
- reposition reveal tabs
- change panel configuration

The user deliberately exits Edit Mode.

Return to the normal configured state, normally:

`GHOST`

Do not allow ordinary mouse movement over the HUD to accidentally activate it.

---

## 128. INPUT PASS-THROUGH PROCESS

One of the most important requirements is that Study HUD must not unnecessarily interfere with applications underneath it.

When a panel is in Ghost Mode:

- normal pointer input must reach the underlying application
- the panel must not steal keyboard focus
- scrolling over the panel should normally reach the underlying application
- the HUD must not activate merely because the pointer passes across it

When Hold-to-Interact is activated:

- HUD hit testing becomes enabled
- interaction happens with the HUD
- underlying applications should not receive HUD clicks

When Hold-to-Interact is released:

- normal pass-through behaviour resumes immediately

The transition should feel instantaneous.

Do not create an invisible input-blocking rectangle covering an entire monitor.

---

## 129. ALWAYS-AVAILABLE MICRO CONTROLS

Certain very small controls may intentionally remain interactive even while the main HUD is in Ghost Mode.

These include:

- edge-collapse reveal tabs
- optionally the control capsule

For each of these, support:

### Always Interactive

The control remains clickable while the rest of the HUD is click-through.

### Respect Hold-to-Interact

The control becomes interactive only when the user's HUD interaction trigger is active.

Reveal tabs should default to **Always Interactive**.

The control capsule may have its own preference.

These controls must have tightly constrained hit-test regions so they do not create large invisible input barriers.

---

## 130. PANEL LIFECYCLE PROCESS

Treat the following as independent concepts.

### Interaction State

- Ghost
- Active
- Edit

### Visibility State

- Expanded
- Edge Collapsed
- Hidden

### Responsive State

- Compact
- Normal
- Expanded Layout

### Docking State

- Floating
- Docked
- Member of Dock Group

These must not be collapsed into one variable.

Example:

A panel may simultaneously be:

```text
Ghost
+
Edge Collapsed
+
Compact
+
Member of Right-Side Dock Group
```

This separation makes behaviour predictable.

---

## 131. PANEL EDGE-COLLAPSE PROCESS

When an eligible edge-attached panel is collapsed:

1. Record its current geometry.
2. Record its dock relationships.
3. Record its responsive state.
4. Determine its collapse direction.
5. Animate the main panel toward the monitor edge.
6. Move/hide the panel so it no longer intercepts input.
7. Leave only the reveal tab inside the monitor work area.
8. Keep all stored panel state intact.

When the reveal tab is activated:

1. Confirm the panel still has a valid monitor.
2. Recalculate geometry if DPI/resolution has changed.
3. Restore the panel from the same edge.
4. Animate it into its previous location.
5. Restore its dock relationship.
6. Restore responsive layout.
7. Maintain the current interaction state.

Do not reconstruct a new panel when expanding it.

Collapse is temporary presentation state only.

---

## 132. DOCKED GROUP BEHAVIOUR PROCESS

Snapping must be more than coordinate alignment.

When the user intentionally completes a snap:

```text
Panel A edge
↓
compatible Panel B edge
↓
show docking preview
↓
release
↓
create persistent docking relationship
```

The relationship should define enough geometry that panels stay connected.

When moving a docked group:

- preserve relationships
- prevent visible gaps
- treat the group as a coherent structure

When resizing:

- propagate compatible dimensional changes
- respect minimum sizes
- preserve responsive breakpoints

When detaching:

- require movement beyond a sensible detachment threshold
- remove the docking relationship
- leave the remaining group valid

Minor pointer movement must not accidentally detach panels.

---

## 133. RESPONSIVE PANEL PROCESS

Every panel must implement explicit responsive behaviour.

```text
Determine the available panel dimensions
↓
Choose Compact / Normal / Expanded
↓
Render the layout designed for that state
```

Do **not** solve small-panel layouts by clipping the normal layout.

Example:

An Expanded Question Finder result might display:

- Week
- Page
- Full section name
- Matched keywords
- Variables
- Score
- Preview
- Open button

Normal may display:

- Week
- Section
- Key matches
- Score
- Open

Compact may display:

- Week
- Short section title
- Score
- Open icon

Information priority changes with available space.

Words must never simply be sliced in half because the panel became narrower.

---

## 134. WORKSPACE SWITCHING PROCESS

A workspace is more than a collection of visible panels.

Each workspace may define:

- panel set
- layout
- macro profile
- preferred controls
- course behaviour
- capture behaviour

When switching workspace:

1. Finish or safely cancel incompatible transient operations.
2. Persist the current workspace layout.
3. Change current workspace.
4. Determine associated layout.
5. Load the target panel arrangement.
6. Determine associated macro profile.
7. Switch macro profile if automatic profile switching is enabled.
8. Maintain current course unless the workspace explicitly specifies another.
9. Apply current Assessment Mode restrictions.
10. Update the control capsule.
11. Show the new workspace.

Avoid destroying and recreating every expensive service during workspace switching.

The change should feel immediate.

---

## 135. MACRO PROFILE COORDINATION

Macro Profiles must work with Workspaces rather than behaving as an unrelated shortcut manager.

Example:

### Normal Workspace/Profile

Mouse buttons behave normally.

### Note Taking Workspace

Automatically activates:

`Note Taking Macro Profile`

if automatic profile switching is enabled.

Possible mappings:

Mouse 4 → note-formatting action  
Mouse 5 Hold → screenshot capture  
F8 → insert common equation  
F9 → heading shortcut

### Question Finder Workspace

Automatically activates:

`Question Finder Macro Profile`

Possible mappings:

Mouse 5 → Draw Question  
F8 → Open highest-ranked result

If automatic switching is disabled, the user can control profiles manually.

Current active profile should always be deterministically known.

---

## 136. MACRO EXECUTION PROCESS

Macro execution must use a proper pipeline.

When an input event occurs:

```text
Input detected
↓
Identify matching triggers
↓
Check whether relevant macro/profile is enabled
↓
Check Workspace condition
↓
Check application allow/deny rules
↓
Check Assessment Mode policy
↓
Check cooldown/re-entry protection
↓
Execute actions sequentially
↓
Report success/failure quietly
```

Actions must execute in defined order.

Example:

```text
Trigger: F8
↓
Check Note Taking workspace
↓
Check active application
↓
Type "σ = F/A"
```

Another example:

```text
Trigger: Mouse 4
↓
KeyDown Ctrl
↓
KeyDown Alt
↓
KeyPress 2
↓
KeyUp Alt
↓
KeyUp Ctrl
↓
Type configured text
↓
Press Enter
```

A failure in one action must follow a defined policy, such as:

- Stop Macro
- Continue
- Retry where explicitly safe

Default should generally be **Stop Macro and report failure**.

---

## 137. MACRO INPUT CONSUMPTION

Study HUD should avoid breaking normal mouse and keyboard behaviour.

If an input does not match an enabled macro:

- let it behave normally

If a macro is disabled by its conditions:

- normally let the original input continue to the target application unless the trigger specifically requires suppression

If Study HUD claims a trigger:

- suppress the underlying action only when necessary to prevent duplicate behaviour

Example:

Mouse Button 5 is configured as Hold-to-Capture in Note Taking Mode.

In Note Taking Mode:

Study HUD may consume it.

Outside Note Taking Mode:

Mouse Button 5 should perform its normal system/application behaviour unless another active macro uses it.

This is essential to making the application non-intrusive.

---

## 138. MACRO TRIGGER CONFIGURATION PROCESS

When assigning a macro trigger:

1. Enter trigger-capture mode.
2. Detect the next supported input.
3. Display exactly what was captured.
4. Detect conflicts with:
   - Study HUD bindings
   - existing macros
   - workspace controls
   - known reserved/system combinations where practical
5. Explain the conflict.
6. Allow the user to:
   - cancel
   - replace conflicting binding
   - choose another trigger
   - deliberately keep both only where the behaviour is deterministic

Support trigger semantics including:

- Press
- Hold
- Release where useful
- Chord
- Double Press only if implemented reliably

Avoid ambiguous trigger combinations.

---

## 139. ONE-GESTURE SCREENSHOT PROCESS

The primary screenshot mechanism must operate as a single continuous gesture.

Example using Mouse Button 5:

### Trigger Down

1. Detect Mouse Button 5 press.
2. Record the pointer location as capture start position.
3. Enter Capture Mode immediately.
4. Display the capture overlay.
5. Change pointer presentation to selection crosshair.
6. Suppress the trigger from the underlying application if necessary.

### Pointer Movement While Held

7. Update selection rectangle continuously.
8. Correctly convert logical coordinates to physical screen pixels.
9. Render the selected region accurately.

### Trigger Release

10. Finalise the rectangle.
11. Capture exactly those screen pixels.
12. Copy the captured image to clipboard.
13. Exit Capture Mode.
14. Restore normal pointer/HUD state.
15. Show a very small success indication if enabled.

This must not require:

- first clicking a screenshot button
- then pressing another mouse button
- launching Snipping Tool
- then beginning another independent drag

The goal is:

`hold → drag → release → captured`

---

## 140. SCREENSHOT CANCELLATION PROCESS

While Capture Mode is active:

`Esc`

must immediately cancel.

Cancellation should:

- capture nothing
- remove capture overlay
- restore input state
- restore pointer appearance
- restore previous HUD interaction state
- leave clipboard unchanged

If the trigger is pressed but movement remains below an appropriate threshold, the app should avoid generating accidental tiny screenshots.

Design this threshold carefully so deliberate small selections remain possible.

---

## 141. CAPTURE DESTINATION PROCESS

Clipboard is the guaranteed baseline.

Default:

```text
Capture
↓
Clipboard
```

Advanced destinations may include:

```text
Capture
↓
Clipboard
↓
Optional configured destination
```

Potential destinations:

- Current application
- Pinned application
- Notion
- File

Automatic destination pasting must never make screenshot capture dependent on fragile focus automation.

If automatic paste fails:

- keep the image safely in clipboard
- report that capture succeeded but paste failed

Never discard a successful capture merely because a secondary action failed.

---

## 142. CAPTURE-TO-NOTES ADVANCED WORKFLOW

Provide architecture for the following optional workflow:

Monitor 1:

Lecture/PDF.

Monitor 2:

Notion.

User performs one-gesture screenshot.

↓

Study HUD captures image.

↓

Image enters clipboard.

↓

If a reliable configured note destination exists:

attempt to paste into that destination.

↓

Optionally restore previous application focus.

This should be labelled as an advanced or experimental workflow until Windows focus behaviour is proven reliable.

The dependable fallback must always remain:

**Screenshot safely available in clipboard.**

---

## 143. QUESTION FINDER COMPLETE PROCESS

The complete Question Finder workflow must be:

```text
Select Course
↓
Activate Draw Question
↓
Draw rectangle around question
↓
Capture selected pixels
↓
Perform LOCAL OCR
↓
Determine OCR confidence
↓
Normalise OCR output
↓
Extract deterministic features
↓
Separate words, variables, symbols, numbers, units and expressions
↓
Apply course constraint
↓
Search LOCAL pre-generated note index
↓
Generate deterministic scores
↓
Rank results
↓
Generate explanation for each match
↓
Display approximately top 3 results
↓
User opens relevant notes
```

The process ends there.

Do not solve the question.

Do not generate an answer.

---

## 144. QUESTION FINDER FAILURE PATHS

If capture fails:

- offer immediate recapture

If OCR confidence is poor:

show the detected content and allow:

- quick correction
- search anyway
- recapture

If no strong result exists:

Do not fabricate one.

Instead show something like:

`No strong matches found.`

Then allow:

- view weaker matches
- change course
- edit detected text
- recapture

If the local index is unavailable:

explain why.

Do not silently switch to internet search, AI, or another prohibited service.

---

## 145. QUESTION RESULT EXPLANATION PROCESS

Each result should be able to answer:

**Why did this result appear?**

For example:

```text
Week 6 — Flexure Formula

Matched:
- bending
- stress
- M
- I
- My/I

Heading Match:
Flexure Formula

Retrieval Score:
92%
```

The percentage must be generated from the deterministic ranking model.

Do not claim that the system "understands" the question.

Do not describe this value as AI confidence.

---

## 146. MATH-AWARE FEATURE EXTRACTION PROCESS

For every captured question and indexed note section, attempt to derive separate deterministic feature sets.

Example input:

`σ = My/I, determine maximum bending stress for M = 3.2 kNm`

Possible representation:

### Words

- determine
- maximum
- bending
- stress

### Variables

- σ
- M
- y
- I

### Numbers

- 3.2

### Units

- kNm

### Expressions

- σ = My/I
- My/I

Ranking should consider the feature classes separately.

Rare variable/expression agreement may receive stronger weighting than ordinary common-word overlap.

Do not combine everything into one uncontrolled text string and rely solely on basic keyword frequency.

---

## 147. OCR CORRECTION PROCESS

Preserve:

- raw OCR output
- normalised OCR output
- OCR confidence where available

If OCR returns:

`o = My/l`

but nearby evidence suggests mathematical notation, deterministic correction rules may generate alternative searchable forms such as:

`σ = My/I`

However:

- preserve the original result
- avoid aggressive replacement
- make corrections explainable
- permit user correction

Do not use an LLM to repair OCR.

---

## 148. NOTION PRE-INDEXING PROCESS

Notion search must operate through a local pre-generated index.

The normal process is:

### Initial Setup

```text
Authorise Notion
↓
Select permitted course/page sources
↓
Read Notion hierarchy
↓
Discover page text
↓
Discover headings
↓
Discover note images/files
↓
Download required images
↓
OCR images locally
↓
Extract deterministic features
↓
Build SQLite/FTS index
↓
Store source metadata
```

### Normal Subsequent Sync

```text
Check source metadata
↓
Identify changed/new/deleted items
↓
Download only required changed items
↓
Reuse cached OCR where hash unchanged
↓
OCR only changed/new images
↓
Update index transactionally
↓
Record successful sync state
```

This process must not rebuild the complete course unnecessarily.

---

## 149. STARTUP PROCESS

Application startup should prioritise immediate usability.

Startup sequence:

1. Load secure configuration.
2. Load layout configuration.
3. Initialise monitor state.
4. Load existing local database/index.
5. Start HUD.
6. Make local Question Finder available.
7. Start allowed background Notion sync afterwards.

Do not show a blank unusable HUD while waiting for Notion.

If the internet is unavailable:

- local library still loads
- existing search still works
- HUD still works
- macros still work
- screenshot still works

---

## 150. STUDY LIBRARY HEALTH PROCESS

The Study Library should make indexing understandable.

For each course show states such as:

- Ready
- Syncing
- OCR processing
- Needs review
- Partially indexed
- Offline — local copy available

Example:

```text
Engineering Mathematics

14 weeks
37 pages
684 note images
681 indexed
3 need review
Last successful sync: 10:32 AM
```

Users should be able to drill down:

```text
Course
↓
Week
↓
Page
↓
Heading
↓
Image
```

At image level, show:

- source
- OCR state
- confidence if available
- last processed
- reprocess option

---

## 151. ASSESSMENT MODE ENTRY PROCESS

Assessment Mode must be treated as an operating mode with explicit policy enforcement.

When entering Assessment Mode:

Assessment Mode applies to **Study HUD's own capabilities and outbound requests**. It must not disable Windows networking or interfere with unrelated/proctoring applications.

1. Check whether required local course indexes exist.
2. Warn if selected course is not fully indexed.
3. Disable prohibited AI/generative integrations.
4. Disable cloud OCR.
5. Disable Question Finder web/network fallback.
6. Disable captured-question upload.
7. Disable automatic answer-generation functionality if any future modules ever contain it.
8. Prevent Question Finder from initiating Notion sync.
9. Switch Question Finder to local-only retrieval.
10. Display a persistent but unobtrusive Assessment/Non-AI indicator.

Potential indicator:

`● LOCAL / NON-GENERATIVE MODE`

Do not state that the mode guarantees institutional permission.

It only guarantees the configured technical restrictions.

---

## 152. ASSESSMENT MODE QUERY PROCESS

When Assessment Mode is active:

```text
Capture Question
↓
Local Capture
↓
Local OCR
↓
Local Feature Extraction
↓
Local SQLite/FTS Search
↓
Local Ranking
↓
Display Note Results
```

There must be no network dependency in this pipeline.

If a prohibited operation is attempted:

```text
Policy Layer
↓
BLOCK
↓
quietly explain why
```

Never silently disable Assessment Mode to complete an action.

---

## 153. ASSESSMENT MODE EXIT PROCESS

Leaving Assessment Mode should require a deliberate user action.

Avoid accidental exit caused by:

- workspace switching
- macro profile switching
- restarting a panel
- connecting Notion
- changing layout

Assessment Mode is global policy state.

Once enabled, it remains enabled until deliberately disabled.

---

## 154. COURSE CONTEXT PROCESS

Course context should coordinate multiple systems.

Changing current course may update:

- Question Finder index
- Notion source
- macro profile
- saved layout
- workspace defaults

depending on user configuration.

Example:

`Engineering Mathematics`

may map to:

- Mathematics local index
- Mathematics Note Taking macro profile
- Mathematics HUD layout
- Mathematics Notion source

The automatic behaviour must be configurable.

Do not unexpectedly replace the current workspace unless course configuration explicitly requests it.

---

## 155. APPLICATION EXCLUSION PROCESS

Before executing an intrusive HUD or macro operation, use the already cached event-driven foreground context from `ForegroundWindowService` rather than performing a slow foreground/process lookup inside the input callback:

1. Read cached foreground application context.
2. Check global exclusions.
3. Check fullscreen policy.
4. Check macro-specific allowed/blocked application rules.
5. Check security/elevation restrictions.
6. Continue only if permitted.

Example:

If a game is fullscreen and configuration says:

`Hide HUD + disable macros`

then Study HUD should do both.

When the user exits the game:

restore the previous appropriate HUD state.

---

## 156. QUIET ERROR PROCESS

Routine failures must not interrupt studying with large modal windows.

Use small status/toast feedback for events such as:

- Macro blocked in this app
- Screenshot cancelled
- Screenshot copied
- Notion offline
- Local index still available
- OCR confidence low
- Paste failed — image remains in clipboard

Reserve modal dialogs for situations requiring an immediate decision.

---

## 157. THEME APPLICATION PROCESS

Themes must render the same underlying functionality differently.

Application logic should request semantic resources such as:

- PanelBackground
- PanelBorder
- Accent
- PrimaryText
- RevealTab
- CompactSpacing

rather than checking:

`if theme == Macintosh`

inside business logic.

Changing a theme should:

1. load theme tokens
2. validate required resources
3. calculate readable foreground colours
4. update visual resources
5. redraw affected HUD surfaces
6. preserve all panel/workspace state

Theme switching must never recreate or lose functional data.

---

## 158. CONTRAST PROTECTION PROCESS

When the user chooses an accent using the colour wheel:

1. calculate contrast with candidate foreground colours
2. select a readable foreground/icon colour automatically
3. warn if an advanced override creates poor readability
4. preserve accessibility at different opacity levels

This lets highly customised themes remain usable.

---

## 159. MOTION BEHAVIOUR PRINCIPLE

Motion must communicate physical state changes.

Use animation primarily to explain:

- where a panel docked
- where a panel collapsed
- which workspace replaced another
- when the HUD became interactive

Do not animate merely for decoration.

The intended feeling is:

**the HUD is attached to the operating system and moves mechanically with it.**

---

## 160. MONITOR REMOVAL PROCESS

If a monitor disappears while Study HUD is running:

1. detect display topology change
2. identify affected panels
3. preserve their saved monitor assignment
4. temporarily relocate inaccessible visible panels to a valid monitor
5. keep the layout recoverable
6. prevent reveal tabs from remaining on nonexistent coordinates

If the original monitor later returns:

optionally restore the original layout depending on configuration.

Never permanently overwrite a carefully configured layout simply because a monitor was temporarily disconnected.

---

## 161. LAYOUT LOCK PRINCIPLE

Outside Edit Mode:

- panel positions are locked
- resize handles are absent
- docking cannot accidentally change
- ordinary clicks cannot drag panels

This is essential.

Study HUD should not slowly drift around the desktop due to accidental interactions.

---

## 162. CONTROL CAPSULE ROLE

The control capsule is the small persistent representation of the **entire Study HUD system**, not another independent widget.

It may show concise information such as:

`Notes | Engineering Maths | Local`

or an appropriately themed equivalent.

From it the user should be able to access:

- Workspace
- Course
- HUD show/hide
- Edit Mode
- Assessment status
- Settings

Keep it small.

Do not turn it into a full toolbar.

---

## 163. COMPLETE NORMAL NOTE-TAKING WORKFLOW

A successful normal study session should be possible like this:

1. Launch Study HUD.
2. Existing HUD layout appears.
3. HUD defaults to Ghost Mode.
4. User opens lecture material on Monitor 1.
5. User opens Notion on Monitor 2.
6. Pointer moves normally through HUD without interference.
7. User needs a HUD control.
8. Hold interaction trigger.
9. HUD becomes interactive.
10. Use control.
11. Release trigger.
12. HUD becomes click-through again.
13. User needs lecture screenshot.
14. Hold screenshot macro trigger.
15. Drag region.
16. Release.
17. Screenshot enters clipboard.
18. Paste into notes.
19. User needs more screen space.
20. Click side-panel collapse arrow.
21. Panel slides off-screen.
22. Continue working.
23. Click remaining reveal tab.
24. Panel returns exactly where it was.

This workflow should require almost no interaction with the settings application.

---

## 164. COMPLETE QUESTION FINDER WORKFLOW

A successful Question Finder session should be:

1. Select Question Finder workspace.
2. Associated layout appears.
3. Appropriate macro profile becomes active if enabled.
4. Current course is displayed.
5. Select/change course if needed.
6. Activate Draw Question.
7. Draw region around question.
8. Local OCR runs.
9. Deterministic parser extracts useful features.
10. Local course index is searched.
11. Approximately three strongest results appear.
12. Each result explains its match.
13. User selects one.
14. Relevant Notion page/section context opens.
15. User uses their notes to solve the question.

At no point should Question Finder automatically answer the question.

---

## 165. COMPLETE ASSESSMENT WORKFLOW

A compliant technical workflow should be:

### Before assessment

1. Select course.
2. Synchronise approved notes.
3. Complete OCR/indexing.
4. Review Library Health.
5. Resolve important OCR failures.
6. Enable Assessment Mode.

### During assessment

1. Study HUD uses local index only.
2. Draw question.
3. OCR locally.
4. Search locally.
5. Open locally known note reference/source.
6. No LLM request occurs.
7. No captured question is uploaded.
8. No web search fallback occurs.

### After assessment

User deliberately disables Assessment Mode.

Again, the software must state that actual assessment rules are determined by the institution and may prohibit tools such as OCR or searchable digital notes regardless of whether generative AI is used.

---

## 166. PRODUCT COHERENCE ACCEPTANCE TEST

Before considering the product polished, test the following scenario as one continuous workflow:

1. Dual-monitor system.
2. Start in Note Taking workspace.
3. HUD is Ghost.
4. Use Hold-to-Interact.
5. Execute a macro.
6. Capture a screenshot.
7. Collapse side panel.
8. Restore side panel.
9. Switch to Question Finder.
10. Confirm Question Finder macro profile activates.
11. Select a course.
12. Capture a question.
13. Receive local ranked results.
14. Open a result.
15. Enable Assessment Mode.
16. Repeat local search.
17. Verify all prohibited external operations are blocked.
18. Switch workspace.
19. Confirm Assessment Mode remains active.
20. Disconnect second monitor.
21. Confirm panels remain accessible.
22. Reconnect monitor.
23. Confirm layout remains recoverable.

If each individual feature works but this complete workflow feels fragmented, inconsistent, slow, or unpredictable, the implementation is not finished.

The goal is not simply to satisfy a checklist.

The goal is for the entire product to behave like **one coherent study tool**.

---

## 167. EVENT-DRIVEN FOREGROUND APPLICATION DETECTION

Do not continuously poll `GetForegroundWindow` at a high frequency to determine whether Study HUD macros/exclusions should apply.

Create a dedicated `ForegroundWindowService` using `SetWinEventHook` for `EVENT_SYSTEM_FOREGROUND` with an out-of-context callback (`WINEVENT_OUTOFCONTEXT`; skip Study HUD's own process where appropriate).

On each foreground-change event:

1. receive the HWND from the WinEvent callback
2. immediately enqueue the HWND/transition for processing; do not perform expensive work inside the callback
3. on the foreground-state worker, resolve process ID and executable identity
4. calculate cached facts needed by the macro/HUD policy layer, including:
   - executable/process identity
   - whether it matches global exclusion rules
   - whether macros are allowed
   - whether capture is allowed
   - elevation/integrity compatibility where known
   - fullscreen state when required
5. publish one immutable/current `ForegroundContext` snapshot through the central session/policy service

Low-level mouse/keyboard callbacks must consult this cached snapshot. They must not synchronously enumerate processes, query UI Automation trees, read files, or perform database lookups.

Use Windows UI Automation (UIA) only if a future feature genuinely requires accessibility element-level information. Do not use UIA as the primary mechanism merely to detect which application is foreground.

If the foreground process exits or information cannot be resolved, degrade safely and refresh the context without blocking global input.

---

## 168. LOW-LEVEL INPUT HOOK SAFETY IMPLEMENTATION

Low-level hooks are permitted only when the desired behaviour cannot be implemented reliably through `RegisterHotKey`, Raw Input, or normal focused-window events.

Implement the hook layer as a small isolated service with the following hard rules:

### Hook callback may do

- parse the Windows hook structure
- read atomically cached Study HUD state
- update minimal press/hold/release state
- decide immediate pass-through vs suppression
- enqueue a compact input event
- return promptly

### Hook callback must not do

- OCR
- SQLite access
- Notion/API calls
- foreground-process discovery
- WPF dispatcher calls that wait synchronously
- file I/O
- synchronous structured logging
- macro action execution
- sleeps/delays
- locks that may be held by UI/background services

Use a bounded, low-allocation handoff such as `System.Threading.Channels` or an equivalent queue from the hook to the input-processing worker.

If the queue is unavailable/full or the macro subsystem is unhealthy, default to passing the user's input through unless Study HUD already owns an active gesture that must receive its release event to leave Capture Mode safely.

Implement health instrumentation for:

- callback duration
- dropped/enqueued events
- input worker backlog
- macro processing latency

Do not attempt to increase reliability by giving Study HUD real-time process/thread priority.

---

## 169. MOUSE 4 / MOUSE 5 PRESERVATION AND SUPPRESSION RULES

Side mouse buttons commonly provide browser Back/Forward behaviour. Study HUD must preserve that normal behaviour whenever the current context does not explicitly claim the button.

When a side-button event arrives:

1. read the cached `ForegroundContext`
2. read the active Workspace/Macro Profile snapshot
3. determine whether an enabled macro owns that exact trigger in the current context
4. determine whether Assessment Mode/exclusion/security policy allows it
5. if **not owned/allowed**, immediately pass the original event through
6. if owned, suppress only the minimum events necessary for the configured Press/Hold/Release gesture
7. enqueue the Study HUD action asynchronously

Do not wait for foreground-process detection after the button has already been pressed. The foreground identity must already be available from the event-driven cache described above.

For a hold-to-capture gesture, once Study HUD has legitimately claimed the button-down event, it must continue tracking the matching release/cancel even if foreground focus changes mid-drag; this avoids a stuck Capture Mode.

Add an automated/manual regression test confirming that Mouse 4/5 continue to navigate normally in supported browsers whenever no active Study HUD macro claims them.

---

## 170. RAW INPUT AND OPTIONAL NATIVE INPUT COMPONENT STRATEGY

Do not assume Raw Input is a universal replacement for low-level hooks.

Use each mechanism according to capability:

- `RegisterHotKey`: ordinary global keyboard shortcuts
- Raw Input: low-latency device/input observation where Study HUD does **not** need to suppress the original input
- low-level hooks: only where global suppression or precise hold/release semantics require them

Start with a carefully profiled .NET/PInvoke implementation isolated behind `IGlobalInputService`.

Do **not** add a mandatory C++ DLL merely because native code may be faster in theory. Introduce a small unmanaged/native input component only if profiling demonstrates that the managed/PInvoke implementation cannot meet the defined latency, reliability, or allocation targets.

If a native component becomes necessary:

- keep it narrowly scoped to input capture/state only
- expose a versioned C ABI or similarly stable interop boundary
- never place macro/business logic inside the native layer
- never perform network/database/UI work there
- keep ownership/lifetime explicit
- include x64 build output in the normal installer
- provide diagnostics when the native component cannot load
- retain a safe fallback where technically possible

The abstraction must allow this escalation without rewriting Workspaces, Macros, or HUD logic.

---

## 171. HYBRID MULTI-HWND OVERLAY IMPLEMENTATION

Implement the architecture in Section 4 as a coordinated set of native surfaces per monitor.

Recommended implementation:

### MonitorOverlayHost

One passive topmost overlay HWND per physical/logical monitor:

- knows that monitor's work area and DPI
- renders Ghost-mode panel visuals and snap/capture overlays
- does not unnecessarily activate
- can become interactive in Active/Edit mode
- uses click-through styles/hit testing while Ghost

### InteractiveIslandHost

A manager for small owned helper HWNDs:

- RevealTab HWND(s)
- optional ControlCapsule HWND
- future proven micro-controls only

Each island receives exact screen bounds from the shared layout engine. Do not duplicate layout calculations inside each HWND.

### Shared logical state

`LayoutService`, `WorkspaceService`, `PanelRegistry`, and the central session state remain authoritative. HWNDs are rendering/input surfaces, not independent owners of panel data.

When a panel moves, collapses, changes monitor, or changes DPI:

1. layout engine calculates logical state
2. physical coordinates are derived for the target monitor
3. passive overlay is updated
4. associated island HWND positions are updated through `SetWindowPos`
5. all surfaces render from the same model/version

Use batching/deferred window positioning when updating several related HWNDs to reduce visual tearing where practical.

---

## 172. INTERACTIVE ISLAND HIT-TESTING REQUIREMENTS

Interactive helper HWNDs must have the smallest practical rectangular/non-rectangular hit target needed for the visible control.

Rules:

- transparent padding outside the visible control must not intercept clicks
- hidden/collapsed controls must destroy or disable their hit surface immediately
- the reveal tab must remain within the monitor work area after DPI/resolution/taskbar changes
- an island should normally use no-activate behaviour so clicking it does not pull keyboard focus from Notion/PDF/browser
- when the action genuinely opens an interactive menu or editor, Study HUD may intentionally activate the appropriate UI surface
- maintain correct accessibility name/role for keyboard/screen-reader use where feasible

Include a diagnostic overlay available in developer/debug mode that can draw the actual native hit rectangles. This makes invisible click-blocking bugs easy to identify.

---

## 173. OCR/INDEX RESOURCE SCHEDULER

Create a central `BackgroundWorkScheduler` or equivalent rather than allowing the Notion synchroniser, downloader, and OCR engine to independently create unlimited tasks.

Use separate work classes/queues, for example:

- **Interactive**: current Question Finder OCR/search preparation
- **Normal**: small user-requested reprocess operation
- **Background**: bulk Notion image OCR/indexing
- **Maintenance**: database cleanup/checkpoint/rebuild work

Interactive work always takes precedence over Background/Maintenance work.

Suggested initial policy:

- background OCR concurrency: 1 on battery/Battery Saver; 1-2 on AC depending on CPU count
- interactive Question Finder OCR: allowed to pre-empt queue order and run promptly
- Notion downloads: bounded separately so downloaded items cannot accumulate indefinitely in RAM
- database writes: single controlled writer pipeline

All long operations accept `CancellationToken`.

When the user activates `Pause Indexing`, stop taking new background items after the current safe operation boundary. Preserve queue/index state so Resume does not restart completed work.

Do not cancel an in-flight SQLite transaction by forcibly terminating the process/thread. Cancel between safe transaction boundaries.

---

## 174. POWER-AWARE BACKGROUND PROCESSING

Study HUD must avoid aggressively draining a laptop battery during large initial syncs.

Observe Windows power changes using a supported power-status/event API.

Expose settings such as:

- Pause heavy indexing on battery
- Reduce OCR concurrency on battery
- Continue indexing while plugged in

Recommended default:

- AC power: bounded normal background indexing
- battery: reduce OCR concurrency to 1
- Battery Saver: pause bulk OCR after the current safe item unless the user overrides

A power-state change must never interrupt current screenshot capture, macro handling, or a foreground Question Finder query.

The control capsule/settings window may show a small `Indexing paused on battery` state without displaying a modal dialog.

---

## 175. FOREGROUND WORK PRIORITY AND UI RESPONSIVENESS

The following work has higher user-experience priority than background sync/indexing:

1. low-level input handoff
2. screenshot/capture interaction
3. HUD rendering, dragging, snapping, collapse/restore
4. current Question Finder OCR/search
5. macro execution
6. ordinary settings interaction
7. background Notion download/OCR/indexing
8. maintenance/rebuild work

Do not implement this with dangerous real-time thread priorities. Use queue priority, bounded concurrency, cancellation/yield points, and lower background worker priority where appropriate.

When interactive work begins while bulk OCR is running, background workers should stop taking new jobs until interactive latency returns to normal.

Add instrumentation so developers can correlate poor HUD latency with active background jobs.

---

## 176. SQLITE WAL, CONNECTION, AND TRANSACTION IMPLEMENTATION

At database initialization:

- enable foreign keys
- request `PRAGMA journal_mode=WAL;`
- set an appropriate `busy_timeout`
- verify the resulting journal mode and log a sanitised warning if WAL could not be enabled

Use a database access layer that clearly separates reads from writes.

Recommended model:

- a small pool/factory of read connections for FTS/search operations
- one serialized writer service/queue for sync/OCR/index updates
- transactions around coherent batches (for example one image/section group or a bounded batch), not around the entire course sync

Never hold a database transaction while awaiting network or OCR processing.

Use WAL checkpointing during low-activity periods and before controlled shutdown when appropriate. Avoid aggressive checkpointing on every write.

If a search encounters a temporary lock/busy state, apply bounded retry and return a recoverable error rather than hanging the HUD.

Database corruption recovery must first preserve/copy the damaged database for optional manual diagnostics, then rebuild derived index/cache data from source metadata where possible without destroying user configuration.

---

## 177. MEMORY MANAGEMENT CONSTRAINTS

Study HUD is expected to coexist with browsers, Notion, engineering software, games, and other memory-intensive applications.

Target steady-state private working-set memory below approximately **150 MB when the HUD is loaded and OCR/synchronisation are idle**, where practical on the chosen .NET/WPF runtime.

Implementation rules:

- do not keep full-resolution screenshots/images after they are no longer needed
- dispose `Bitmap`, `BitmapSource` backing resources, streams, OCR image handles, and native buffers deterministically where applicable
- decode images at the resolution actually needed for OCR/preview rather than retaining unnecessary duplicate representations
- process bulk image libraries as streams/items, not one in-memory collection of decoded images
- bound all producer/consumer queues
- cache OCR **text/results**, not indefinitely decoded image surfaces
- use weak/size-bounded thumbnail caches
- monitor native memory used by OCR/native imaging libraries as well as managed heap size

If profiling shows the OCR engine retains excessive memory after large jobs, consider moving bulk OCR workers to a separate worker process so the operating system can reclaim the worker's memory cleanly after a batch. Do this only if profiling justifies the additional complexity.

---

## 178. CPU AND IDLE RESOURCE CONSTRAINTS

The always-on HUD must not degrade demanding academic or entertainment software simply by existing.

Prefer event-driven APIs over polling.

Do not run a high-frequency timer to detect:

- foreground application changes
- monitor changes when Windows already exposes display events
- input state already available through event mechanisms

Any periodic maintenance timer must use the longest interval consistent with its purpose and must suspend unnecessary work when the machine/session is idle where appropriate.

Profile CPU in at least these states:

- HUD hidden
- HUD Ghost/idle
- HUD Active but idle
- panel drag/resize
- one-gesture capture
- local search
- bulk OCR/indexing on AC
- bulk OCR/indexing on battery policy

A Ghost/idle HUD with no background job should consume negligible CPU on a modern machine.

---

## 179. LOCAL-ONLY CRASH DIAGNOSTICS AND RECOVERY

Create a `CrashDiagnosticsService` that writes sanitised crash data to a local application diagnostics directory.

Handle, at minimum, appropriate application-domain/task/WPF unhandled exception paths while avoiding the dangerous assumption that every exception is safely recoverable.

For recoverable subsystem failures:

- isolate the failing subsystem
- keep the HUD running when safe
- present a small error/status indication
- record a local diagnostic event

For process-fatal failures:

1. attempt a final atomic flush of non-sensitive configuration where safe
2. record sanitised exception metadata
3. do not upload anything automatically
4. on next launch, detect the previous abnormal termination and offer `View diagnostics` / `Export diagnostics`

The default crash record must never contain:

- Notion access token
- authentication headers
- clipboard contents
- screenshots
- captured question text
- complete OCR/note contents

Diagnostic export must be initiated by the user.

---

## 180. WINDOWS PACKAGING AND UPDATE STRATEGY

Deliver Study HUD as a polished installable Windows application rather than a loose development folder.

Preferred packaging order:

1. **MSIX/App Installer-compatible packaging** if all required full-trust desktop, native interop, startup, hook, and update behaviours work correctly in testing.
2. If MSIX restrictions materially break required functionality, use a reputable signed desktop installer/updater architecture appropriate to .NET/WPF (Squirrel may be evaluated but is not mandatory).

Whichever packaging system is selected must provide:

- code-signed application/installer for production releases
- per-user installation where feasible to avoid unnecessary administrator requirements
- Start Menu/taskbar-friendly application identity
- clean uninstall
- versioned upgrades
- configuration/library preservation
- atomic or recoverable update installation
- rollback/recovery path when an update cannot start successfully

The packaging decision and why it was chosen must be documented in `HANDOFF.md`.

Do not select an updater solely because it is easy to code. Validate it with global input hooks, native helper components (if any), startup behaviour, Windows security, and the target deployment model.

---

## 181. UPDATE BEHAVIOUR DURING STUDY AND ASSESSMENT SESSIONS

Updates must never unexpectedly restart Study HUD during active work.

Rules:

- update checking/downloading must not block HUD startup
- do not install/restart while Capture Mode is active
- do not install/restart while Assessment Mode is active
- do not interrupt an active background database migration/rebuild
- preserve current settings/layout/library across version changes
- run schema/config migrations before exposing features that depend on the new schema
- if migration fails, restore/retain the previous valid data where possible and report the problem

Allow the user to choose an appropriate update policy such as notify-only or install-on-exit when supported by the chosen packaging system.

Assessment Mode must not silently contact an update service if its Study-HUD-local network policy forbids outbound traffic during that session.

---

## 182. ASSESSMENT MODE NETWORK SEMANTICS

**Assessment Mode means Study HUD is local-only; it does not mean the Windows computer is forced offline.**

Study HUD must never disable the Windows network adapter, manipulate the user's firewall to block unrelated applications, or prevent institution-required real-time proctoring software from communicating.

When Assessment Mode is active, Study HUD's policy layer blocks Study HUD from performing prohibited outbound operations, including:

- Notion synchronisation
- captured-question upload
- web search
- cloud OCR
- LLM/generative AI calls
- embedding/vector-model calls
- remote answer/search services
- update checks when configured to local-only assessment policy

Other applications retain normal network access.

Implement this through a central `INetworkPolicy` / `AssessmentPolicyService` used by every Study HUD network-capable component. All Study HUD HTTP clients/connectors must pass policy checks before outbound requests. Do not allow individual modules to instantiate unmanaged/untracked HTTP paths that bypass the policy layer.

Question Finder itself should be architecturally capable of operating with **zero network dependencies** during Assessment Mode.

---

## 183. ASSESSMENT MODE AND PROCTORING SOFTWARE

Study HUD must never attempt to evade, disable, hide from, inject into, modify, obscure, or bypass proctoring/monitoring software.

If proctoring software or an institution-controlled environment blocks:

- overlays
- global hooks
- screenshots
- clipboard operations
- Study HUD execution

Study HUD must fail safely and explain the incompatibility rather than attempting circumvention.

Assessment Mode's network isolation applies to Study HUD only, so legitimate proctoring traffic can continue.

Documentation/UI must state that Study HUD's technical `Local / Non-Generative` mode is **not a declaration that the application is permitted in an assessment**. The institution may prohibit overlays, macros, OCR, searchable notes, screenshots, or the application entirely.

---

## 184. PERFORMANCE AND RESOURCE ACCEPTANCE TESTS

Add a repeatable profiling/QA suite covering the audit risks introduced in Sections 167-183.

At minimum test and record:

### Global input

- hook callback duration distribution
- time from accepted trigger to macro worker start
- browser Mouse 4/5 pass-through when no macro owns the trigger
- side-button behaviour while foreground app changes
- input behaviour while WPF UI thread is intentionally busy/stalled in a debug test
- queue-overflow/fail-open behaviour

### Overlay

- Ghost-mode click-through across the whole panel
- exact reveal-tab hit region
- no invisible input interception around interactive islands
- reveal tab/control capsule do not unnecessarily activate the underlying/top-level app
- Z-order across two mixed-DPI monitors
- rapid collapse/restore/move without orphan helper HWNDs

### Database

- FTS search while background writer is committing OCR results
- WAL enabled and recoverable after forced termination
- busy/locked handling
- large incremental sync without long UI stalls

### Resources

- idle Ghost CPU
- steady-state RAM with OCR idle
- peak/bounded RAM during large OCR jobs
- CPU usage during bulk indexing
- battery-mode concurrency behaviour
- Question Finder latency while background OCR is already running

### Crash diagnostics

- local crash record created
- sensitive fields absent
- manual export works
- no automatic upload occurs

### Assessment/network

- Study HUD network calls are blocked in Assessment Mode
- local Question Finder still works
- unrelated test application retains internet/network access
- switching Workspace/Macro Profile does not weaken Assessment policy
- update service does not contact the network when assessment policy forbids it

A feature is not considered complete if it works functionally but violates these non-interference/resource targets.

---

## 185. IMPLEMENTATION DECISION RECORDS FOR WINDOWS-SENSITIVE FEATURES

For Windows-sensitive areas, create short Architecture Decision Records (ADRs) or equivalent documentation in the repository.

At minimum document decisions for:

- chosen overlay/HWND architecture
- input mechanism selection (`RegisterHotKey` vs Raw Input vs hooks)
- whether a native input DLL was required after profiling
- SQLite WAL/connection model
- OCR concurrency/power policy
- packaging/updater choice
- Assessment Mode network policy implementation

Each record should include:

- requirement/problem
- alternatives considered
- measured/technical evidence
- chosen approach
- known limitations
- rollback/fallback approach

Update `HANDOFF.md` with links/summaries so another coding AI does not accidentally reverse a deliberate Windows-specific decision later.

---

## 186. AUDIT-DRIVEN IMPLEMENTATION PRIORITY

The audit findings modify the implementation priorities as follows:

1. **Never block global input.** Hook callbacks stay minimal and fail open where safe.
2. **Never make side-button macros break normal navigation outside the exact context that owns them.**
3. **Use event-driven foreground tracking instead of high-frequency polling.**
4. **Use hybrid multi-HWND rendering/hit-testing where necessary for reliable click-through + interactive micro-controls.**
5. **Bound OCR CPU, memory, and queue growth.**
6. **Give interactive HUD/search work priority over background indexing.**
7. **Use SQLite WAL + controlled transactions so indexing and deterministic search coexist.**
8. **Keep crash diagnostics local and sanitised.**
9. **Package/update as a normal signed Windows utility without interrupting work.**
10. **Assessment Mode restricts Study HUD's own outbound capabilities, never unrelated proctoring/network software.**

If an implementation is simpler but conflicts with one of these rules, prefer the more robust architecture.

---

# Final Instruction to the Coding AI

Treat this document as the source of truth for implementation.

Before changing a requested behaviour, first determine whether the issue is:

1. an actual Windows/API constraint,
2. a conflict with another requirement,
3. a reliability problem,
4. or simply an implementation preference.

Only the first three justify changing the requested workflow, and any such change must preserve the user's intent as closely as possible and be documented in `HANDOFF.md`.

Build the application incrementally, keep it runnable at the end of each phase, test the complete workflows rather than isolated features only, and prioritise reliability and non-interference over unnecessary complexity.
