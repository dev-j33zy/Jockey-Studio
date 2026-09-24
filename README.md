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

## Installation

Grab the latest release from the [GitHub releases page](https://github.com/dev-j33zy/Jockey-Studio/releases) and run `JockeyStudio_<version>_x64.exe`. It is a self-contained, single-file `.exe` — no runtime install needed.

### Building the installer yourself

```
dotnet publish src-wpf\JockeyStudio.Wpf\JockeyStudio.Wpf.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

The single-file `JockeyStudio.exe` lands in the project's `bin\Release\net8.0-windows\win-x64\publish\` output. A self-contained build carries its own .NET 8 runtime, so it runs on any Windows 10/11 (x64) machine.

### Releases (CI)

Every `v*` tag triggers `.github/workflows/build.yml`, which publishes the Windows build on GitHub Actions and attaches `JockeyStudio_<version>_x64.exe` to the matching release with its `CHANGELOG.md` section as the notes. That exe asset is exactly what the in-app updater downloads — the release and its asset are the app's update feed.

## Auto-update

- Updates are fetched from `https://github.com/dev-j33zy/Jockey-Studio/releases/latest`.
- Version tags are compared with semver (a leading `v` is stripped). The update package is the first release asset ending in `.exe` (the self-contained single-file build).
- Flow: silent check on launch → bottom-right bubble → updater dialog with the release notes → **Install Now** downloads the new `.exe`, closes the app, replaces the running executable and relaunches.
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
.github/workflows/            CI: publish + attach release assets on v* tags
CHANGELOG.md                  release notes
```

Engine settings (auto-mix amount, gate, attack/release/hold, default device) live in the persisted app state.

## License

Copyright (c) 2026 CJay Capillo.