# Study HUD

A Windows desktop study assistant: an always‑on‑top, click‑through HUD overlay with configurable
macros, one‑gesture screenshot capture, multi‑monitor layouts, and a **deterministic Question
Finder** that searches your own locally‑indexed Notion notes — **without** generating answers or
using any generative AI.

> **Status: early preview.** It runs, but it's rough and unsigned. The easiest way to use it is to
> **download the prebuilt app** (below) — no coding required. Developers can also
> [build from source](#build-from-source-developers). Windows only (WPF + Win32).

---

## Download and run (easiest — no build needed)

This is the simple path: grab the prebuilt app and double‑click it. **You do not need to install
.NET, Visual Studio, or anything else** — the download is a self‑contained Windows app.

1. Open the **[Releases page](https://github.com/hughdwill-byte/Study-Assistant/releases)** — the
   newest version is at the top.
2. Under **Assets**, download **`StudyHud-<version>-win-x64.zip`**.
3. In File Explorer, **right‑click the zip → Extract All…** into a folder you'll keep
   (for example `C:\StudyHud`). **Extract it outside OneDrive** — Windows often refuses to launch an
   app from a synced OneDrive folder (*"Windows cannot access the specified device, path or file"*).
   *Don't run it from inside the zip.*
4. Open the extracted **`StudyHud`** folder and **double‑click `StudyHud.exe`** (it sits among a set
   of support files — that's normal).
5. **First launch:** Windows may show *"Windows protected your PC"* (SmartScreen — the app isn't
   code‑signed yet). Click **More info → Run anyway**. You only do this once per version.

> **If your antivirus blocks it** (e.g. Avast/AVG flag it or make it "disappear"): the app isn't
> code‑signed, so this is a *false positive*. Restore it from your antivirus's quarantine and add an
> **exception** for the `StudyHud` folder, then run it again.

That's it. When it starts you'll see two things:

1. The **Study HUD settings window** — where you manage everything.
2. The **HUD overlay** on your screen(s), in **Ghost mode** — visible but click‑through, so it
   stays out of your way.

> **Closing the settings window does *not* quit the app** — the HUD keeps running in the background.
> To fully quit (e.g. before updating), close the settings window and then end any remaining
> **`StudyHud.exe`** in **Task Manager** (Ctrl+Shift+Esc → Details).

**For the Question Finder's text recognition (OCR)** you may need a Windows OCR language pack. If a
capture finds no text, install it via *Settings → Time & language → Language & region →* your
language *→ Language options → Basic typing / OCR*.

Requires **Windows 10 or 11, 64‑bit**.

---

## Updating

Study HUD is a **portable app** — there's no installer, so updating just means swapping in the newer
files. **Your data is never touched by an update** (see [below](#your-data-is-safe-across-updates)).

The easy way:

1. **Quit Study HUD completely** (close the settings window, then end any `StudyHud.exe` in Task
   Manager — see the note above).
2. Open the **[Releases page](https://github.com/hughdwill-byte/Study-Assistant/releases)** (newest
   at the top) and download the new **`StudyHud-<version>-win-x64.zip`**.
3. **Extract it over your existing folder** and choose **Replace** when Windows asks — or just
   extract to a brand‑new folder and run the new `StudyHud.exe` from there. Either works.
4. Double‑click the new `StudyHud.exe`. (SmartScreen may prompt once more for the new version —
   *More info → Run anyway*.)

To check which version you have, look at the release you downloaded, or the `StudyHud.exe` file's
*Properties → Details*.

### Your data is safe across updates

Your courses, note index, settings, saved layouts, and Notion token all live **separately from the
app**, under `%LOCALAPPDATA%\StudyHud\` (see [Where your data lives](#where-your-data-lives)).
Replacing the app files **does not delete or change any of it** — you can update as often as you like
without re‑syncing your notes or re‑entering your token.

*Tip:* if you'd rather keep old versions around, extract each release into its own folder
(`StudyHud-0.1.0`, `StudyHud-0.2.0`, …) and just run the newest — they all share the same data folder.

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

Everything is local, under `%LOCALAPPDATA%\StudyHud\` (paste that into the File Explorer address bar
to open it):

| Path | What |
|------|------|
| `studyhud.db` | SQLite database: courses, note index, full‑text search. |
| `settings.json` | Your settings (theme, triggers, exclusions, …). |
| `layouts\` | Saved HUD panel layouts per workspace. |
| `creds\` | Your Notion token, encrypted with Windows DPAPI. |
| `Logs\` | Local‑only diagnostic logs (no tokens, note text, or screenshots). |

This folder is **separate from the app**, so updating the app never affects it. To **reset** the app
completely, quit it and delete the whole `%LOCALAPPDATA%\StudyHud` folder.

---

## Troubleshooting

- **"Windows cannot access the specified device, path or file"** — you're running it from inside
  OneDrive (or the zip). Extract to a local folder outside OneDrive, e.g. `C:\StudyHud`, and run it
  from there.
- **Antivirus blocks it or it "disappears" on launch (Avast/AVG/Defender)** — a false positive on an
  unsigned app. Restore it from quarantine and add an **exception** for the `StudyHud` folder, then
  run again.
- **SmartScreen blocks it / "Windows protected your PC"** — expected; the app isn't code‑signed.
  Click **More info → Run anyway**.
- **Question Finder finds no text** — install a Windows OCR language pack (see
  [Download and run](#download-and-run-easiest--no-build-needed)).
- **HUD isn't visible** — press **Ctrl + Shift + H** (you may have Panic‑hidden it). Check the app is
  still running (closing the settings window doesn't quit it).
- **"Could not connect" to Notion** — check the token, that you **shared** the pages with the
  integration, and that Assessment Mode is off.
- **Macros/typing don't work in another app** — a non‑elevated app can't send input to an elevated
  (admin) app. Run the target app without admin, or Study HUD with matching elevation.
- **Something's wrong at startup** — see the newest file in `%LOCALAPPDATA%\StudyHud\Logs\`.
- **(Building from source) build fails with a platform error** — you forgot `-p:Platform=x64` (or the
  VS dropdown isn't on *x64*). The solution has no *Any CPU* configuration.

---

## Build from source (developers)

Prefer to compile it yourself, or want to contribute? You'll need:

- **Windows 11 or 10, 64‑bit.**
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (x64). Check with `dotnet --info`
  — you want an 8.0.x SDK.
- One of: **Visual Studio 2022** (17.8+) with the *.NET desktop development* workload,
  **JetBrains Rider**, or just the **`dotnet` CLI**.
- **Git**.

```powershell
git clone https://github.com/hughdwill-byte/Study-Assistant.git
cd Study-Assistant/src
dotnet restore StudyHud.sln
dotnet build StudyHud.sln -c Debug -p:Platform=x64
```

The solution is **x64‑only**, so always pass `-p:Platform=x64` (there is no *Any CPU* configuration).

**Run it:**

```powershell
# from the src folder, after building
.\StudyHud.App\bin\x64\Debug\net8.0-windows\StudyHud.exe
```

In Visual Studio / Rider, set **StudyHud.App** as the startup project (configuration **Debug | x64**)
and press **F5**. (`dotnet run --project StudyHud.App/StudyHud.App.csproj -c Debug -p:Platform=x64`
also works.)

**Run the tests:**

```powershell
cd src
dotnet test StudyHud.sln -c Debug -p:Platform=x64
```

**Produce a release build like the download** (self‑contained single file):

```powershell
dotnet publish src/StudyHud.App/StudyHud.App.csproj -c Release -r win-x64 --self-contained true `
  -p:Platform=x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Releases are cut automatically by GitHub Actions when a `v*` tag is pushed
(`.github/workflows/release.yml`).

---

## For developers

Architecture, design decisions, and module responsibilities are in
[`src/HANDOFF.md`](src/HANDOFF.md). The full product spec is
[`Study_HUD_Master_Specification.md`](Study_HUD_Master_Specification.md). CI builds and tests every
push on Windows (`.github/workflows/ci.yml`).
