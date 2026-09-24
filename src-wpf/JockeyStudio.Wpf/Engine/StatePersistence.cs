using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JockeyStudio.Wpf.Engine;

/// <summary>1:1 of models.rs PersistedTile: everything about a deck that should
/// survive a restart (its media, routing and per-deck controls).</summary>
public sealed class PersistedTile
{
    public string Id { get; set; } = "";
    public string? MediaId { get; set; }
    public string DeviceId { get; set; } = EngineConst.DEFAULT_DEVICE_ID;
    public float Volume { get; set; } = 0.9f;
    public bool Muted { get; set; }
    public string LoopMode { get; set; } = "off";
    public FadesConfig Fades { get; set; } = new();
}

/// <summary>1:1 of models.rs AppState: the full layout+library+settings payload
/// written to disk and merged back on launch.</summary>
public sealed class AppState
{
    public List<string> TileOrder { get; set; } = new();
    public List<PersistedTile> Tiles { get; set; } = new();
    public List<MediaItem> Media { get; set; } = new();
    public EngineSettings Settings { get; set; } = new();

    /// <summary>WPF-only: last release the update bubble was dismissed for, so a
    /// silent boot check stops nudging. Unknown to the Tauri build; serde/json
    /// ignore it there, and old state files simply default it to empty.</summary>
    public string UpdateDismissedVersion { get; set; } = "";

    public bool HasData => Tiles.Count > 0 || Media.Count > 0;
}

/// <summary>App-state persistence (1:1 of src-tauri/src/storage.rs). Serialized
/// to the same file the Tauri build used (%APPDATA%/com.cjaycapillo.jockeystudio
/// /app-state.json), so a WPF install picks up the playlist, decks and settings
/// a user had in the webview app. Writes are atomic (tmp file + replace).</summary>
public static class StatePersistence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string StatePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "com.cjaycapillo.jockeystudio", "app-state.json");

    /// <summary>Read the persisted state, or null when there is nothing to
    /// restore (missing file or unreadable/corrupt payload — fall back on a
    /// fresh layout rather than crashing or clearing a good file).</summary>
    public static AppState? Load() => LoadFrom(StatePath);

    /// <summary>Read + deserialize a playlist/session file from <paramref name="path"/>
    /// (used by File → Open Playlist, which loads saved .jcky files). Returns
    /// null for missing/unreadable/corrupt files or empty payloads.</summary>
    public static AppState? LoadFrom(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string raw = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<AppState>(raw, Options);
            return state is { HasData: true } ? state : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Serialize the state to disk atomically. Never throws: a failed
    /// save must not take the running app down; the layout stays in memory.</summary>
    public static void Save(AppState state) => SaveTo(state, StatePath);

    /// <summary>Canonical JSON of a state snapshot — cheap deterministic
    /// comparison for the Undo/Redo log (drop no-op entries).</summary>
    public static string ToJson(AppState state)
    {
        try { return JsonSerializer.Serialize(state, Options); }
        catch { return ""; }
    }

    /// <summary>Serialize <paramref name="state"/> to <paramref name="path"/>
    /// (any location, e.g. File → Save As) atomically; never throws.</summary>
    public static void SaveTo(AppState state, string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            string raw = JsonSerializer.Serialize(state, Options);
            File.WriteAllText(tmp, raw);
            File.Move(tmp, path, true);
        }
        catch
        {
        }
    }
}