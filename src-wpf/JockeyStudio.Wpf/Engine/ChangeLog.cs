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
    public const string LatestTitle = "Compact List View and more reliable seeking";

    /// <summary>Explanation of the current build's headline change (moved here
    /// from the About page).</summary>
    public const string LatestSummary =
        "Switch between tiled decks and compact stacked rows from the View menu; your choice is " +
        "remembered, and rows can be reordered by dragging. Both seekers now jump directly to " +
        "clicked timestamps during playback without changing play/pause state. The list seeker " +
        "shows a hover timestamp, and tile Loop and Auto-mix controls identify themselves on hover.";

    /// <summary>Versioned history, newest first. Sync source: CHANGELOG.md (the
    /// release notes published to GitHub and shown by the updater dialog).</summary>
    public static IReadOnlyList<ChangeLogEntry> Versions { get; } = new[]
    {
        new ChangeLogEntry
        {
            Version = "0.2.5",
            Title = "Compact List View, more reliable seeking, and clearer deck controls",
            Date = "2026-10-07",
            Items = new[]
            {
                "List View — switch between tiled decks and compact stacked rows from the View menu; the choice is remembered. Rows include playback, seeking, metadata and deck controls, and can be reordered by dragging.",
                "More reliable seeking — clicking either seeker jumps directly to the requested timestamp, even during active playback or repeated clicks. Seeking keeps the play/pause state unchanged; empty-deck seekers start at the left edge.",
                "List seeker hover time — hovering the list seeker displays the timestamp at the pointer.",
                "Deck control tooltips — tile-view Loop and Auto-mix buttons now identify themselves on hover.",
                "List layout refinements — fixed the Remove button alignment and positioned metadata with the row controls.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.2.4",
            Title = "Seeking during playback now follows the most recent click, and update notifications compare versions strictly",
            Date = "2026-10-07",
            Items = new[]
            {
                "More reliable seeking — clicks made while the audio pipeline is restarting are remembered; playback resumes at the latest requested position instead of an earlier seek.",
                "Accurate update checks — an update is offered only when both the installed and release versions parse as major.minor.patch and the release version is greater. The dialog labels the current and available versions.",
                "Robust slider values — slider converters handle common numeric value types, not just double.",
                "Preserve settings-only state — saved non-default settings remain restorable even when no decks or media are present; automix hold state is cleared during deck restoration.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.2.3",
            Title = "Sliders respond where you click, the volume button says what it is set to, and an update clears out the old 0.1.x app so the shortcuts point at this one",
            Date = "2026-09-28",
            Items = new[]
            {
                "Click a slider to jump to that point — clicking the bar used to nudge the value by one LargeChange step, because the track is drawn as two transparent RepeatButtons bound to Slider.Increase/DecreaseLarge. This now works on the seek bar, all five auto-mix and fade sliders in Settings, and the volume fader.",
                "Volume level on hover — the volume button reports the level its deck is playing at, and stops doing so while the volume modal is open, where the level is already on screen.",
                "Updates clear the 0.1.x app — the old Tauri build was a separate per-user product with its own install folder, shortcuts and Programs entry, and it shares no AppId with this installer, so an update could never supersede it. A machine that ran 0.1.x was left with two Jockey Studio entries in the Start menu and in Add/Remove Programs, and the stale one kept launching the old app even after a successful update. Installing any current release now removes those leftovers for every account on the machine, so one install leaves one shortcut and one Programs entry. Your settings and deck data are untouched.",
                "App icon — the Jockey Studio icon is now embedded in the executable and shown in the window, Settings and About, replacing the default WPF icon.",
                "Space-free release asset — the published installer is JockeyStudio_<version>_x64.exe (no spaces), which is what CI names the asset and what the in-app updater expects.",
                "Automated releases — pushing a v* tag now builds the self-contained executable and publishes it to GitHub Releases with the matching section of the changelog as its release notes; installers are no longer tracked in the repository.",
                "App identifier — settings and state now live under com.jockeystudio.app. Installs that were on com.cjaycapillo.jockeystudio start with default settings; see the README for the old folder's paths.",
                "README — corrected copyright and the cleanup steps for machines still carrying a 0.1.x Tauri/Rust install.",
            },
        },
        new ChangeLogEntry
        {
            Version = "0.2.2",
            Title = "A real Windows installer, and auto-update that works",
            Date = "2026-09-27",
            Items = new[]
            {
                "Windows installer — releases now ship JockeyStudio_<version>_x64-setup.exe (Inno Setup) instead of a bare executable. It installs to C:\\Program Files\\Jockey Studio for all users, adds a Start Menu shortcut (plus an optional desktop shortcut) and an Add/Remove Programs entry, and installs over an existing copy. Installing and updating both ask for UAC approval.",
                "Auto-update fixed — 0.2.0 and 0.2.1 updated by copying the new executable over the running one from a generated .cmd. The copy failed silently whenever the app's path contained a space, so the update never landed and the update prompt came back on every launch. Updates now download the installer, hand it to Windows, and let it upgrade the install and relaunch the app.",
                "Safer update payload — the updater now only accepts a -setup.exe release asset, so a portable build or any other .exe on a release can no longer be mistaken for the installer and copied over the running app.",
                "CI guards — the release workflow now fails if the built setup's filename does not match the tag being released, or if the changelog has no section for that version.",
            },
        },
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