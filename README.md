# Study HUD

A Windows desktop study assistant: an always‑on‑top, click‑through HUD overlay with configurable
macros, one‑gesture screenshot capture, multi‑monitor layouts, and a **deterministic Question
Finder** that searches your own locally‑indexed Notion notes — **without** generating answers or
using any generative AI.

> **Status: early / build‑from‑source.** There is no installer or prebuilt release yet. You build
> and run it from source with the .NET 8 SDK. It targets Windows only (WPF + Win32).

---

## What you need

- **Windows 11 or 10, 64‑bit.**
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (x64). Check with `dotnet --info`
  — you want an 8.0.x SDK.
- One of:
  - **Visual Studio 2022** (17.8+) with the *.NET desktop development* workload, **or**
  - **JetBrains Rider**, **or**
  - just the **command line** (`dotnet` CLI).
- **Git** (to clone the repo).
- Optional, for the note search: a **Notion account** and an *internal integration* token (see
  [Connect your Notion notes](#connect-your-notion-notes)).

The Question Finder uses **Windows' built‑in OCR**. If OCR finds no text, install an OCR language
pack: *Settings → Time & language → Language & region →* your language *→ Language options →*
install the *Basic typing / OCR* feature.

---

## Get the code

```powershell
git clone https://github.com/hughdwill-byte/Study-Assistant.git
cd Study-Assistant
```

The solution lives in the `src` folder.

---

## Build

The solution is **x64‑only**, so pass the x64 platform.

**Command line:**

```powershell
cd src
dotnet restore StudyHud.sln
dotnet build StudyHud.sln -c Debug -p:Platform=x64
```

**Visual Studio:** open `src/StudyHud.sln`, make sure the configuration dropdown shows **Debug | x64**,
and build (Ctrl+Shift+B).

---

## Run

**Visual Studio / Rider:** set **StudyHud.App** as the startup project and press **F5**.

**Command line** — build first (above), then launch the app the build produced:

```powershell
# from the src folder
.\StudyHud.App\bin\x64\Debug\net8.0-windows\StudyHud.exe
```

(`dotnet run` also works but must be told the platform:
`dotnet run --project StudyHud.App/StudyHud.App.csproj -c Debug -p:Platform=x64`.)

When it starts you'll see two things:

1. The **Study HUD settings window** (this is where you manage everything).
2. The **HUD overlay** on your screen(s), in **Ghost mode** — visible but click‑through, so it
   doesn't get in your way.

Closing the settings window does **not** quit the app — the HUD keeps running. Quit from the
settings window / your IDE when you're done.

---

## Using the HUD

The overlay has three interaction states so it stays out of the way until you want it:

| State | What it means | How to get there |
|-------|---------------|------------------|
| **Ghost** | Visible but clicks pass straight through to whatever's underneath. | Default. |
| **Active** | The HUD is clickable. | **Hold** the Hold‑to‑Interact key (default **Caps Lock**); release to go back to Ghost. |
| **Edit** | Move / resize / arrange panels. | **Ctrl + Shift + E** (toggles; it does *not* exit when you release keys). |

Default global shortcuts (all configurable in **Settings**):

- **Caps Lock (hold)** — make the HUD interactive while held (Hold‑to‑Interact).
  *Tip:* Caps Lock also toggles caps — pick a dedicated key (e.g. Scroll Lock) or a mouse side
  button in Settings to avoid that.
- **Ctrl + Shift + H** — Panic hide / show the whole HUD (for screen‑sharing, presenting, games).
- **Ctrl + Shift + E** — toggle Edit mode.

Panel positions, your theme, the active course, and Assessment Mode are saved automatically and
restored next launch (`%LOCALAPPDATA%\StudyHud\`).

---

## Connect your Notion notes

The Question Finder searches a **local index** of your notes, so you sync once (before you need it),
then search offline.

1. In Notion, create an **internal integration** at
   [notion.so/my-integrations](https://www.notion.so/my-integrations) and copy its **secret**.
2. **Share** each course page (and its sub‑pages) with that integration in Notion
   (*⋯ menu → Connections → your integration*).
3. In Study HUD's settings window, open **Notion Sync** (or **Courses**) in the sidebar.
4. Paste the secret into **Integration token** and click **Save token**. The token is encrypted on
   your device (Windows DPAPI) and is never logged or uploaded. Click **Test** to confirm it connects.
5. **Add a course**: give it a name and the **Notion root page ID** — the 32‑character id at the end
   of the page's URL (`notion.so/…-<id>`).
6. Click **Sync** on the course. Study HUD downloads the page's images/text, runs **local OCR** once,
   and builds the searchable index. Progress and per‑course health (indexed / low‑confidence / failed,
   pages, weeks, last sync) show on the card.

Do this **before** an assessment while you have internet — afterwards search works fully offline.

---

## Find a question in your notes

1. Switch the HUD to the **Question Finder** workspace (from the settings window or the control
   capsule).
2. Click **Draw Question** and drag a rectangle around a question anywhere on screen.
3. Study HUD OCRs it locally, extracts words / variables / units / symbols, and searches your index.
4. You get the top matching note sections with **why they matched** (matched terms, variables,
   expressions) and a **Match score** — then open the note in Notion.

It **never answers the question** — it only points you to your own relevant notes.

---

## Assessment Mode

Toggle **Assessment Mode** in the settings window (or the control capsule) to enforce a strict,
local‑only profile. While it's on, Study HUD blocks its own Notion sync, cloud OCR, web search, and
any LLM/embedding/upload calls — only the **prebuilt local index** and **local OCR** are used.
It does **not** touch Windows networking or other apps.

> **Important:** "non‑generative" does **not** automatically mean "permitted". OCR and search tools
> may still be disallowed by your institution. **You are responsible for complying with your
> assessment's rules.**

---

## Where your data lives

Everything is local, under `%LOCALAPPDATA%\StudyHud\`:

| Path | What |
|------|------|
| `studyhud.db` | SQLite database: courses, note index, full‑text search. |
| `settings.json` | Your settings (theme, triggers, exclusions, …). |
| `layouts\` | Saved HUD panel layouts per workspace. |
| `creds\` | Your Notion token, encrypted with Windows DPAPI. |
| `Logs\` | Local‑only diagnostic logs (no tokens, note text, or screenshots). |

To **reset** the app completely, quit it and delete the `%LOCALAPPDATA%\StudyHud` folder.

---

## Run the tests (optional)

```powershell
cd src
dotnet test StudyHud.sln -c Debug -p:Platform=x64
```

---

## Troubleshooting

- **Build fails with a platform error** — you forgot `-p:Platform=x64` (or the VS dropdown isn't on
  *x64*). The solution has no *Any CPU* configuration.
- **"WindowsDesktop" / WPF errors on build** — install the *.NET desktop development* workload in the
  Visual Studio Installer, or use the full .NET 8 **SDK** (not just the runtime).
- **Question Finder finds no text** — install a Windows OCR language pack (see
  [What you need](#what-you-need)).
- **HUD isn't visible** — press **Ctrl + Shift + H** (you may have Panic‑hidden it). Check the app is
  still running (the settings window closing doesn't quit it).
- **"Could not connect" to Notion** — check the token, that you **shared** the pages with the
  integration, and that Assessment Mode is off.
- **Macros/typing don't work in another app** — a non‑elevated app can't send input to an elevated
  (admin) app. Run the target app without admin, or Study HUD with matching elevation.
- **Something's wrong at startup** — see the newest file in `%LOCALAPPDATA%\StudyHud\Logs\`.

---

## For developers

Architecture, design decisions, and module responsibilities are in
[`src/HANDOFF.md`](src/HANDOFF.md). The full product spec is
[`Study_HUD_Master_Specification.md`](Study_HUD_Master_Specification.md). CI builds and tests every
push on Windows (`.github/workflows/ci.yml`).
