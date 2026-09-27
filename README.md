# Jockey Studio

Multi-deck audio mixing workstation for Windows, built with **.NET 8 / WPF** and **NAudio**. Load music onto decks, route each deck to any output device, and mix with fades, loops and per-deck auto-mix.

## Features

- **Multiple persistent decks** — every deck has its own volume, mute, loop repeats (off / endless / 2×–5×), output device and fade settings; the full layout is saved between sessions.
- **Native device routing** — a default output device can be set in Settings (System Default or any device); every deck follows it unless you pick a specific output on the deck, and per-deck picks persist until the deck is removed.
- **Per-deck fades** — adjustable fade-in/out (seconds) applied on the next play.
- **Auto-mix (vMix group style)** — per-deck on/off. While a deck's signal is above the gate, it ducks every OTHER deck that also has auto-mix engaged; once it stays below the gate for the hold time, the others ramp back up. Decks without auto-mix are never touched. Timing is configurable (seconds, decimal precision).
- **Live loop switching** — changing a deck's loop mode while it plays applies immediately in place; the current playthrough becomes #1 of the new selection, no pipeline restart.
- **Media library** — a sliding drawer on the right lists your library for one-click loading; dropping files onto a deck loads them too.
- **Drag & drop reorder** — drag a deck by its header to reorder tiles to any position.
- **Playlists** — open/save decks as playlists (`File → Open/Save Playlist`), including layout, device and loop settings.
- **Clear all decks** — stop all playback and reset every deck to a fresh empty state (decks and their order are kept).
- **Appearance & layout** — light/dark/system themes, full screen (F11), zoom, and a deck finder (Ctrl+F).
- **In-app auto-update** — a silent check against GitHub releases at launch, an update bubble + dialog, and a one-click update that swaps in the new build and relaunches (self-contained single-file `.exe`).

<img width="1486" height="893" alt="Jockey Studio screenshot" src="https://github.com/user-attachments/assets/9e46ccbb-7fbe-430f-bb57-07600d3fc0f8" />

## Installation

Grab `JockeyStudio_<version>_x64-setup.exe` from the [GitHub releases page](https://github.com/dev-j33zy/Jockey-Studio/releases) and run it. The setup installs Jockey Studio to `C:\Program Files\Jockey Studio` for every user on the machine, adds a Start Menu shortcut (and, if you tick the box, a desktop shortcut) and an Add/Remove Programs entry you can uninstall from. It asks for UAC approval, and running it again over an existing install upgrades it in place.

The app itself is a self-contained single-file `.exe` carrying its own .NET 8 runtime, so the installed copy runs on any Windows 10/11 (x64) machine with nothing else to install.

A portable copy still works — grab the `JockeyStudio_<version>_x64.exe` build and run it from anywhere — but **only the installed copy can update itself**, because an in-place update needs write access to its own folder. A portable build still detects new releases; installing from that prompt is how you move to the installed version.

### Building the installer yourself

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isdl.php):

```
dotnet publish src-wpf\JockeyStudio.Wpf\JockeyStudio.Wpf.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o publish_out
iscc /DAppVersion=0.2.2 /DPublishDir=publish_out /DOutputDir=artifacts installer\jockeystudio.iss
```

The setup lands in `artifacts\`. `installer/jockeystudio.iss` is the single source of truth for the install layout, shortcuts and the installer name the updater looks for.

### Releases (CI)

Every `v*` tag triggers `.github/workflows/build.yml`, which publishes the Windows build on GitHub Actions and attaches `JockeyStudio_<version>_x64-setup.exe` to the matching release with its `CHANGELOG.md` section as the notes. That setup is exactly what the in-app updater downloads — the release and its asset are the app's update feed.

### Upgrading a machine that ran the 0.1.x (Tauri/Rust) builds

The old Rust app ships its own updater, so a machine that ran 0.1.x keeps its installed Rust copy alongside the new WPF app. If a launch re-opens the old version (it will re-offer the update on every run), clean the machine once, per user account:

Install the current release first (`JockeyStudio_<version>_x64-setup.exe`) — it puts the app in `C:\Program Files\Jockey Studio` with its own shortcuts, so the old install's leftovers are then just clutter.

In the old app folder — `%LOCALAPPDATA%\Jockey Studio\`, or `%LOCALAPPDATA%\Programs\Jockey Studio\` (occasionally on a non-system drive):

- **Delete** the whole folder: `jockey-studio.exe`, `uninstall.exe`, `WebView2Loader.dll`, the `resources\` folder, `unins000.exe` / `unins000.dat`. The Rust app's shortcut pointed here, so repoint or remove the old Start Menu / desktop shortcut.

Also delete:

- `%TEMP%\jockey-studio-update\` — stale updater payload (`jockey-studio-setup.exe`, `run-update.cmd`).
- `%LOCALAPPDATA%\com.cjaycapillo.jockeystudio\` and `%APPDATA%\com.cjaycapillo.jockeystudio\` — data folders from older builds that used the old bundle identifier (present only if such a build ran on the machine).
- Old `Downloads\Jockey.Studio_0.1.x_x64-setup.exe` installers, and any portable `JockeyStudio_*.exe` left in Downloads, if any.

**Keep** (do not delete):

- `%APPDATA%\com.jockeystudio.app\` — the app's settings/state folder (current bundle identifier), shared by the installed and portable copies.

## Auto-update

- Updates are fetched from `https://github.com/dev-j33zy/Jockey-Studio/releases/latest`.
- Version tags are compared with semver (a leading `v` is stripped). The update package is the release's `-setup.exe` asset — the Inno Setup installer, matched by that suffix so the portable `.exe` on the same release can never be mistaken for it.
- Flow: silent check on launch → bottom-right bubble → updater dialog with the release notes → **Install Now** downloads the installer, hands it to Windows (UAC), the app closes, and the setup upgrades the install in place and relaunches it.
- The app must be installed, not run as a portable copy: the app is in Program Files, so the update is a reinstall, which needs elevation. A portable build still detects releases, and installing from that prompt is how you move to the installed version.
- Manual check: **Help → Check for Updates**, or **Help → About → Updates** tab.

## Development

Prerequisites: .NET 8 SDK (Windows).

```
dotnet run --project src-wpf\JockeyStudio.Wpf    # run the desktop app in dev mode
dotnet build src-wpf\JockeyStudio.Wpf\JockeyStudio.Wpf.csproj -c Debug
```

## Structure

```
src-wpf/
  JockeyStudio.Wpf/           .NET 8 / WPF app (NAudio)
    Controls/                 tile cards, top bar, library drawer, settings/about/update dialogs
    Engine/                   deck player (WASAPI), auto-mix, looping, device library, updater, persistence
    Helpers/                  app preferences, theme manager
    Themes/                   colors (dark/light) and control styles
installer/jockeystudio.iss   Inno Setup script: install layout, shortcuts, installer name
.github/workflows/            CI: publish + attach release assets on v* tags
CHANGELOG.md                  release notes
```

Engine settings (auto-mix amount, gate, attack/release/hold, default device) live in the persisted app state.

## License

Copyright (c) 2026 dev-j33zy.
