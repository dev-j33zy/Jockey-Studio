# Release Notes

All notable changes to Jockey Studio are documented here. Releases are published to GitHub, and the updater dialog shows each release's notes fetched from GitHub.

## 0.2.1 — 2026-09-27

Polish and packaging follow-ups to the 0.2.0 WPF rewrite.

- **Mouse-wheel volume faders** — the mouse wheel over a deck's volume fader now nudges its level (wheel up raises), so a fader can be set without dragging it; the wheel no longer scrolls whatever is behind the fader.
- **App icon** — the Jockey Studio icon is now embedded in the executable and shown in the window, Settings and About, replacing the default WPF icon.
- **Space-free release asset** — the published installer is `JockeyStudio_<version>_x64.exe` (no spaces), which is what CI names the asset and what the in-app updater expects.
- **Automated releases** — pushing a `v*` tag now builds the self-contained executable and publishes it to GitHub Releases with the matching section of this file as its release notes; installers are no longer tracked in the repository.
- **App identifier** — settings and state now live under `com.jockeystudio.app`. **Note:** installs that were on `com.cjaycapillo.jockeystudio` start with default settings; the old folder can be deleted once the new layout looks right (see the README for paths).
- **README** — corrected copyright and the cleanup steps for machines still carrying a 0.1.x Tauri/Rust install.

## 0.2.0 — 2026-09-24

Native WPF rewrite (Windows).

- **Native WPF rewrite** — the desktop app is now a .NET 8 / WPF (NAudio) Windows app, replacing the Tauri + React + Rust core. No WebView2 runtime, lower memory footprint, native rendering.
- **Feature parity** — multi-deck mixing, per-deck volume/mute/fades/loop/output device, auto-mix ducking with fade-pause/resume, drag & drop loading and reordering, playlists, themes, full screen and zoom all carry over to the rewrite.
- **Media library + new deck UX** — a top bar with icon-only New Deck / Library buttons and a sliding media library drawer; Settings, About, What's New and Check for Updates moved to menu-driven windows.
- **Live loop switching** — changing a deck's loop mode while it plays no longer tears down the pipeline; the current playthrough becomes #1 of the new selection in place.
- **Cleaner popups** — loop, volume and output device modals close when they lose focus and switch cleanly when another control is clicked (one modal at a time across all decks).
- **Loop dropdown polish** — aligned `Off / Endless / 2×–5×` items with the repeat badge kept on the deck's loop button as the active-mode indicator.
- **Self-contained installer** — distributed as a self-contained single-file `JockeyStudio_<version>_x64.exe`; the updater replaces the executable in place and relaunches.

## 0.1.2 — 2026-09-16

Windows and macOS installers, built on CI.

- **Windows: WebView2 bundled** — the NSIS installer now embeds the WebView2 bootstrapper and provisions the runtime silently during setup, so Windows no longer requires a separately installed WebView2 runtime.
- **Windows: correct DLL bundling** — release builds now target `x86_64-pc-windows-gnu` so `WebView2Loader.dll` is bundled with the executable.
- **macOS** — refreshed Apple Silicon (aarch64) `.dmg` from CI.

## 0.1.1 — 2026-09-16

Self-update on macOS, default output device setting, and playback/reorder refinements.

- **Self-update on macOS** — in-app updates now work on macOS via `.dmg`: download, mount, swap the `.app` into `/Applications`, and relaunch.
- **Default device output** — a default output device can be set in **Settings** (`System Default` or any device); decks follow it unless given a specific output, and per-deck picks persist until the deck is removed.
- **Drag-to-any-position reorder** — dragging a deck by its header can now move it to any position, not just within the same row.
- **Seeker accuracy + start-position resume** — the seeker tracks precisely and playback resumes from the deck's stored position (live pos handled separately from the stream position).
- **Deck-level loop mode** — loop repeats (off / endless / repeat 2–5×) are per deck; changing one deck no longer affects the others, and counted loops fade in each new playthrough.
- **Cross-platform installer CI** — GitHub Actions builds Windows (NSIS `.exe`) and macOS (Apple Silicon `.dmg`) and attaches both to versioned releases.

## 0.1.0 — 2026-09-16

Initial release.

- Multi-deck mixing on floating tiles, each with its own volume, mute, fades, loop and output device.
- Native per-deck device routing via rodio, with the full layout and settings persisted between sessions.
- Auto-mix (vMix-style ducking) with configurable amount, gate, attack/release/hold.
- Drag & drop file loading and deck reordering; browser mode for development.
- In-app auto-update (Windows) with silent check, bubble, and one-click installer update with relaunch.