# Jockey Studio

Multi-deck audio mixing player built with **Tauri 2**, **React** and **Rust (rodio)**. Load music onto floating tiles, route each tile to any output device, and mix with fades, loops and per-deck auto-mix.

## Features

- **Multiple persistent decks** — every tile has its own volume, mute, repeat, loop region, output device and fade settings; the full layout is saved between sessions.
- **Native device routing** — every deck can be sent to a different WASAPI output device, refreshed live.
- **Per-deck fades** — adjustable fade-in/out (seconds) applied on the next play.
- **Auto-mix (vMix group style)** — per-deck on/off. While a deck's signal is above the gate, it ducks every OTHER deck that also has auto-mix engaged; once it stays below the gate for the hold time, the others ramp back up. Decks without auto-mix are never touched. Attack/release/hold timing is configurable (seconds, decimal precision).
- **Drag & drop reorder** — drag a deck by its header to reorder tiles; dropping files onto a tile loads them.
- **In-app auto-update** — a silent check against GitHub releases at launch, an update bubble + dialog, and a one-click silent installer update with auto-relaunch.

## Auto-update

- Updates are fetched from `https://github.com/dev-j33zy/Jockey-Studio/releases/latest`.
- Version tags are compared with semver (a leading `v` is stripped); the first release asset ending in `.exe` is treated as the Windows installer.
- Flow: silent check on launch → bottom-right bubble → updater dialog with release notes → **Install Now** downloads the installer, closes the app, silently installs and relaunches.
- Manual check: **Settings → Check for Updates**.

## Installation

Build the current installer:

```
npm run tauri -- build
```

Outputs:
- Installer: `src-tauri/target/release/bundle/nsis/Jockey Studio_<version>_x64-setup.exe`
- Portable exe (no bundle): `npm run tauri -- build --no-bundle` → `src-tauri/target/release/jockey-studio.exe`

### Code signing (Windows SmartScreen)

Installers are signed with the locally-issued self-signed certificate **"Jockey Studio"** (SHA-256, RFC3161 timestamp), which is installed into the machine's **Trusted Root** and **Trusted Publishers** stores.

- On a machine where that certificate is trusted, SmartScreen installs without a warning (publisher shows "Jockey Studio").
- On any other machine the certificate chain is self-signed, so SmartScreen will still flag it as "Unknown publisher" — install a commercial code-signing cert (e.g. DigiCert) and resign for global distribution.
- Signing is automated via the PowerShell helper in `scripts/sign-release.ps1` (creates the cert if missing, trusts it, downloads a recent release build, signs and verifies).

## Development

Prerequisites: Node.js 20+, Rust (stable), and on Windows the WebView2 runtime.

```
npm install
npm run dev               # Tauri dev window + Vite HMR (opens on localhost:5173)
npm run build             # type-check + Vite production build (ui/dist)
npm run test              # UI unit tests (vitest)
npm run test:rust         # Rust unit tests
npm run tauri -- dev      # run the desktop app in dev mode
```

> Note: `cargo test` may exit with `0xc0000139` on some Tauri setups (known toolchain quirk); the UI tests and both builds are the reliable checks.

## Structure

```
ui/                 React + TypeScript app (Vite), renderer logic, store, components
src-tauri/          Rust backend: audio engine (rodio), persistence, auto-updater
src-tauri/src/      lib.rs (commands), audio/engine.rs, updater.rs, models.rs
scripts/            build/signing utilities (generate-icon, sign-release)
```

Engine settings (auto-mix amount, gate, attack/release/hold) live in the Rust persistence model and are serialized as part of the app state.

## License

Copyright (c) 2026 CJay Capillo.