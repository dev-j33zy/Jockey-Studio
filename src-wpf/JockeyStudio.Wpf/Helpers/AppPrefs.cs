using System.IO;
using System.Text.Json;

namespace JockeyStudio.Wpf.Helpers;

/// <summary>Small non-engine preferences (theme mode, deck zoom) that live next
/// to the persisted app state but are cosmetic, so they never leak into .jcky
/// playlists or the restore file. Loaded once at startup; writes are atomic.</summary>
public static class AppPrefs
{
    private sealed class PrefsData
    {
        public string Theme { get; set; } = "system";
        public double Zoom { get; set; } = 1.0;
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "com.cjaycapillo.jockeystudio", "app-settings.json");

    public static ThemeMode Theme { get; private set; } = ThemeMode.System;
    public static double Zoom { get; private set; } = 1.0;

    static AppPrefs()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var data = JsonSerializer.Deserialize<PrefsData>(File.ReadAllText(SettingsPath));
            if (data == null) return;
            Theme = Enum.TryParse(data.Theme, ignoreCase: true, out ThemeMode t) ? t : ThemeMode.System;
            if (data.Zoom > 0) Zoom = Math.Clamp(data.Zoom, 0.5, 1.8);
        }
        catch
        {
            // keep defaults on any read error
        }
    }

    public static void SetTheme(ThemeMode mode)
    {
        Theme = mode;
        Save(new PrefsData { Theme = mode.ToString().ToLowerInvariant(), Zoom = Zoom });
    }

    public static void SetZoom(double zoom)
    {
        Zoom = zoom;
        Save(new PrefsData { Theme = Theme.ToString().ToLowerInvariant(), Zoom = zoom });
    }

    private static void Save(PrefsData data)
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsPath);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
        }
        catch
        {
            // a failed pref write must never bring the app down
        }
    }
}