using System.Text.Json.Serialization;
using static JockeyStudio.Wpf.Engine.EngineConst;

namespace JockeyStudio.Wpf.Engine;

public static class EngineConst
{
    public const string DEFAULT_DEVICE_ID = "default";
}

/// <summary>1:1 of engine.rs deck statuses (Stopped=0, Playing=2, Paused=3,
/// Ended=4); Loading=1 mirrors the Rust transition state and Error is WPF-
/// specific.</summary>
public enum PlaybackStatus
{
    Stopped = 0,
    Loading = 1,
    Playing = 2,
    Paused = 3,
    Ended = 4,
    Error = 5,
}

public enum MediaKind
{
    Audio,
    Video,
}

/// <summary>1:1 of models.rs MediaItem.</summary>
public sealed class MediaItem
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Container { get; set; }
    public string? Codec { get; set; }
    public double DurationSecs { get; set; }
    public ushort Channels { get; set; } = 2;
    public uint SampleRate { get; set; } = 48000;
    public MediaKind Kind { get; set; } = MediaKind.Audio;
    public float? PeakDb { get; set; }
    public float? RmsDb { get; set; }
}

/// <summary>1:1 of models.rs FadeConfig.</summary>
public sealed class FadesConfig
{
    public float FadeIn { get; set; } = 0.05f;
    public float FadeOut { get; set; } = 0.2f;
    public bool AutoMix { get; set; } = true;

    public FadesConfig Clone() => new() { FadeIn = FadeIn, FadeOut = FadeOut, AutoMix = AutoMix };
}

/// <summary>1:1 of models.rs OutputDevice (camelCase JSON for parity).</summary>
public sealed class OutputDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    public ushort Channels { get; set; } = 2;
    public uint SampleRate { get; set; } = 48000;
}

/// <summary>1:1 of models.rs EngineSettings.</summary>
public sealed class EngineSettings
{
    public bool AutoMixEnabled { get; set; } = true;
    public float AutoMixDb { get; set; } = 10.0f;
    public ulong AutoMixAttackMs { get; set; } = 250;
    public ulong AutoMixReleaseMs { get; set; } = 800;
    public float DefaultFadeIn { get; set; } = 0.05f;
    public float DefaultFadeOut { get; set; } = 0.2f;
    public string DefaultDeviceId { get; set; } = DEFAULT_DEVICE_ID;
}

/// <summary>Loop-mode helpers (1:1 of models.rs LoopMode string encoding).</summary>
public static class LoopModes
{
    /// <summary>Total number of playthroughs a counted mode wants (0 otherwise).</summary>
    public static int RepeatCount(string mode) =>
        mode switch
        {
            "x2" => 2,
            "x3" => 3,
            "x4" => 4,
            "x5" => 5,
            _ => 0,
        };

    public static bool IsEndless(string mode) => mode == "endless";
    public static bool IsCounted(string mode) => RepeatCount(mode) >= 2;
}