# Release Notes

All notable changes to Jockey Studio are documented here. Releases are published to GitHub, and the updater dialog shows each release's notes fetched from GitHub.

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
- Code-signing helper for Windows installers (`scripts/sign-release.ps1`).