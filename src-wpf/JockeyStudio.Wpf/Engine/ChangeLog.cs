namespace JockeyStudio.Wpf.Engine;

/// <summary>One release note entry — the "What's new" block shown for a version
/// of the app. Mirrors the release notes published to GitHub, which is the
/// change log the updater shows (see <see cref="UpdateInfo.ReleaseNotes"/>);
/// keep these entries in sync with the GitHub release bodies when publishing.</summary>
public sealed class ChangeLogEntry
{
    public string Version { get; init; } = "";
    public string Title { get; init; } = "";
    public string Date { get; init; } = "";
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
}

/// <summary>The bundled change log: what's new in the current build followed by
/// the versioned release history, newest first. Content is ported 1:1 from the
/// project CHANGELOG.md / GitHub release notes — the same list the updater
/// dialog fetches — so the What's New page stays in sync with the updater.</summary>
public static class ChangeLog
{
    /// <summary>Headline feature of the current build — the sub-title of the
    /// "What's new" page. Kept separate from the versioned history because it
    /// describes the in-development behavior of this build, not a shipped
    /// release.</summary>
    public const string LatestTitle = "Scroll-wheel volume control and a proper app icon";

    /// <summary>Explanation of the current build's headline change (moved here
    /// from the About page).</summary>
    public const string LatestSummary =
        "0.2.1 polishes the WPF rewrite: the mouse wheel over a deck's volume fader now " +
        "nudges its level so you can set it without dragging, the Jockey Studio icon is " +
        "embedded in the executable and shown in the window, Settings and About, and " +
        "release assets are now named without spaces so the in-app updater can fetch " +
        "them reliably. Settings and state moved to the com.jockeystudio.app folder.";

    /// <summary>Versioned history, newest first. Sync source: CHANGELOG.md (the
    /// release notes published to GitHub and shown by the updater dialog).</summary>
    public static IReadOnlyList<ChangeLogEntry> Versions { get; } = new[]
    {
        new ChangeLogEntry
        {
            Version = "0.2.1",
            Title = "Scroll-wheel volume control, app icon and automated releases",
            Date = "2026-09-27",
            Items = new[]
            {
                "Mouse-wheel volume faders — the mouse wheel over a deck's volume fader now nudges its level (wheel up raises), so a fader can be set without dragging; the wheel no longer scrolls whatever is behind the fader.",
                "App icon — the Jockey Studio icon is now embedded in the executable and shown in the window, Settings and About, replacing the default WPF icon.",
                "Space-free release asset — the published installer is JockeyStudio_<version>_x64.exe (no spaces), which is what CI names the asset and what the in-app updater expects.",
                "Automated releases — pushing a v* tag now builds the self-contained executable and publishes it to GitHub Releases with the matching section of the changelog as its release notes.",
                "App identifier — settings and state now live under com.jockeystudio.app. Installs that were on com.cjaycapillo.jockeystudio start with default settings; see the README for the old folder's paths.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.2.0",
            Title = "Native WPF rewrite",
            Date = "2026-09-24",
            Items = new[]
            {
                "Native WPF rewrite — the desktop app is now a .NET 8 / WPF (NAudio) Windows app, replacing the Tauri + React + Rust core. No WebView2 runtime, lower memory footprint, native rendering.",
                "Feature parity — multi-deck mixing, per-deck volume/mute/fades/loop/output device, auto-mix ducking with fade-pause/resume, drag & drop loading and reordering, playlists, themes, full screen and zoom all carry over to the rewrite.",
                "Media library + new deck UX — a top bar with icon-only New Deck / Library buttons and a sliding media library drawer; Settings, About, What's New and Check for Updates moved to menu-driven windows.",
                "Live loop switching — changing a deck's loop mode while it plays no longer tears down the pipeline; the current playthrough becomes #1 of the new selection in place.",
                "Cleaner popups — loop, volume and output device modals close when they lose focus and switch cleanly when another control is clicked (one modal at a time across all decks).",
                "Loop dropdown polish — aligned Off / Endless / 2x-5x items with the repeat badge kept on the deck's loop button as the active-mode indicator.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.1.2",
            Title = "Installers, bundled WebView2 and CI builds",
            Date = "2026-09-16",
            Items = new[]
            {
                "Windows: WebView2 bundled — the NSIS installer embeds the WebView2 bootstrapper and provisions the runtime silently during setup, so Windows no longer requires a separately installed WebView2 runtime.",
                "Windows: correct DLL bundling — release builds target x86_64-pc-windows-gnu so WebView2Loader.dll is bundled with the executable.",
                "macOS: refreshed Apple Silicon (aarch64) .dmg built on CI.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.1.1",
            Title = "Self-update on macOS, default output device, playback refinements",
            Date = "2026-09-16",
            Items = new[]
            {
                "Self-update on macOS — in-app updates now work on macOS via .dmg: download, mount, swap the .app into /Applications, and relaunch.",
                "Default device output — a default output device can be set in Settings; decks follow it unless given a specific output, and per-deck picks persist until the deck is removed.",
                "Drag-to-any-position reorder — dragging a deck by its header can now move it to any position, not just within the same row.",
                "Seeker accuracy + start-position resume — the seeker tracks precisely and playback resumes from the deck's stored position.",
                "Deck-level loop mode — loop repeats (off / endless / repeat 2-5x) are per deck; changing one deck no longer affects the others, and counted loops fade in each new playthrough.",
                "Cross-platform installer CI — GitHub Actions builds Windows (NSIS .exe) and macOS (Apple Silicon .dmg) and attaches both to versioned releases.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.1.0",
            Title = "Initial release",
            Date = "2026-09-16",
            Items = new[]
            {
                "Multi-deck mixing on floating tiles, each with its own volume, mute, fades, loop and output device.",
                "Native per-deck device routing, with the full layout and settings persisted between sessions.",
                "Auto-mix (vMix-style ducking) with configurable amount, attack/release times and duck level.",
                "Drag & drop file loading and deck reordering.",
                "In-app auto-update (Windows) with silent check, bubble, and one-click installer update with relaunch.",
            },
        },
    };
}