use serde::{Deserialize, Deserializer, Serialize, Serializer};

pub type TileId = String;
pub type MediaId = String;
pub type DeviceId = String;

pub const DEFAULT_DEVICE_ID: &str = "default";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum MediaKind {
    Audio,
    Video,
}

impl Default for MediaKind {
    fn default() -> Self {
        MediaKind::Audio
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MediaItem {
    pub id: MediaId,
    pub path: String,
    pub title: String,
    pub container: Option<String>,
    pub codec: Option<String>,
    pub duration_secs: f64,
    pub channels: u16,
    pub sample_rate: u32,
    pub kind: MediaKind,
    pub peak_db: Option<f32>,
    pub rms_db: Option<f32>,
}

impl Default for MediaItem {
    fn default() -> Self {
        Self {
            id: String::new(),
            path: String::new(),
            title: String::new(),
            container: None,
            codec: None,
            duration_secs: 0.0,
            channels: 2,
            sample_rate: 48000,
            kind: MediaKind::Audio,
            peak_db: None,
            rms_db: None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum PlaybackStatus {
    Stopped,
    Loading,
    Playing,
    Paused,
    Ended,
    Error,
}

/// How a deck repeats its media once it reaches the end:
/// - `Off` – play once, then stop.
/// - `Endless` – repeat seamlessly forever until stopped.
/// - `Times(n)` – play the media exactly `n` times in total (n = 2..=5).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LoopMode {
    Off,
    Endless,
    Times(u8),
}

impl LoopMode {
    /// Total number of playthroughs this mode wants (or 0 when not a count).
    pub fn repeat_count(&self) -> u8 {
        match *self {
            LoopMode::Times(n) => n.clamp(2, 5),
            _ => 0,
        }
    }
}

impl Serialize for LoopMode {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_str(match *self {
            LoopMode::Off => "off",
            LoopMode::Endless => "endless",
            LoopMode::Times(n) => match n.clamp(2, 5) {
                2 => "x2",
                3 => "x3",
                4 => "x4",
                _ => "x5",
            },
        })
    }
}

impl<'de> Deserialize<'de> for LoopMode {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        let s = String::deserialize(deserializer)?;
        Ok(match s.as_str() {
            "endless" => LoopMode::Endless,
            "x2" => LoopMode::Times(2),
            "x3" => LoopMode::Times(3),
            "x4" => LoopMode::Times(4),
            "x5" => LoopMode::Times(5),
            // Lenient: older builds persisted "one"/"all"; unknown → plain single play.
            _ => LoopMode::Off,
        })
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FadeConfig {
    pub fade_in: f32,
    pub fade_out: f32,
    pub auto_mix: bool,
}

impl Default for FadeConfig {
    fn default() -> Self {
        Self {
            fade_in: 0.05,
            fade_out: 0.2,
            auto_mix: true,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TileState {
    pub id: TileId,
    pub media: Option<MediaId>,
    pub title: String,
    pub device_id: DeviceId,
    pub status: PlaybackStatus,
    pub position_secs: f64,
    pub duration_secs: f64,
    pub volume: f32,
    pub muted: bool,
    pub loop_mode: LoopMode,
    pub fades: FadeConfig,
    pub error: Option<String>,
}

impl Default for TileState {
    fn default() -> Self {
        Self {
            id: String::new(),
            media: None,
            title: String::new(),
            device_id: DEFAULT_DEVICE_ID.to_string(),
            status: PlaybackStatus::Stopped,
            position_secs: 0.0,
            duration_secs: 0.0,
            volume: 0.9,
            muted: false,
            loop_mode: LoopMode::Off,
            fades: FadeConfig::default(),
            error: None,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OutputDevice {
    pub id: DeviceId,
    pub name: String,
    pub is_default: bool,
    pub is_active: bool,
    pub channels: u16,
    pub sample_rate: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EngineSettings {
    pub auto_mix_enabled: bool,
    #[serde(default = "default_auto_mix_db")]
    pub auto_mix_db: f32,
    #[serde(default = "default_auto_mix_gate_db")]
    pub auto_mix_gate_db: f32,
    #[serde(default = "default_auto_mix_attack_ms")]
    pub auto_mix_attack_ms: u64,
    #[serde(default = "default_auto_mix_release_ms")]
    pub auto_mix_release_ms: u64,
    #[serde(default = "default_auto_mix_hold_ms")]
    pub auto_mix_hold_ms: u64,
    #[serde(default)]
    pub auto_mix_duck: f32,
    pub default_fade_in: f32,
    pub default_fade_out: f32,
    #[serde(default = "default_device_id")]
    pub default_device_id: String,
}

fn default_device_id() -> String {
    DEFAULT_DEVICE_ID.to_string()
}

fn default_auto_mix_db() -> f32 {
    10.0
}

fn default_auto_mix_gate_db() -> f32 {
    -42.0
}

fn default_auto_mix_attack_ms() -> u64 {
    250
}

fn default_auto_mix_release_ms() -> u64 {
    800
}

fn default_auto_mix_hold_ms() -> u64 {
    600
}

impl Default for EngineSettings {
    fn default() -> Self {
        Self {
            auto_mix_enabled: true,
            auto_mix_db: default_auto_mix_db(),
            auto_mix_gate_db: default_auto_mix_gate_db(),
            auto_mix_attack_ms: default_auto_mix_attack_ms(),
            auto_mix_release_ms: default_auto_mix_release_ms(),
            auto_mix_hold_ms: default_auto_mix_hold_ms(),
            auto_mix_duck: 0.35,
            default_fade_in: 0.05,
            default_fade_out: 0.2,
            default_device_id: default_device_id(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EngineSnapshot {
    pub tiles: Vec<TileState>,
    pub media: Vec<MediaItem>,
    pub devices: Vec<OutputDevice>,
    pub settings: EngineSettings,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EngineEvent {
    pub kind: String,
    pub action: String,
    pub tile_id: Option<TileId>,
    pub payload: serde_json::Value,
}

/// Result of a background update check against the GitHub release feed.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateInfo {
    pub latest_version: String,
    pub current_version: String,
    pub release_notes: String,
    pub published_at: String,
    pub asset_name: String,
    pub download_url: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct AppState {
    pub tile_order: Vec<TileId>,
    pub tiles: Vec<PersistedTile>,
    pub media: Vec<PersistedMedia>,
    pub settings: PersistedSettings,
    pub window: PersistedWindow,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PersistedTile {
    pub id: TileId,
    pub media_id: Option<MediaId>,
    pub device_id: DeviceId,
    pub volume: f32,
    pub muted: bool,
    pub loop_mode: LoopMode,
    pub fades: FadeConfig,
}

impl From<&TileState> for PersistedTile {
    fn from(t: &TileState) -> Self {
        Self {
            id: t.id.clone(),
            media_id: t.media.clone(),
            device_id: t.device_id.clone(),
            volume: t.volume,
            muted: t.muted,
            loop_mode: t.loop_mode,
            fades: t.fades,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PersistedMedia {
    pub id: MediaId,
    pub path: String,
    pub title: String,
    #[serde(default)]
    pub container: Option<String>,
    #[serde(default)]
    pub codec: Option<String>,
    #[serde(default)]
    pub duration_secs: f64,
    #[serde(default)]
    pub channels: u16,
    #[serde(default)]
    pub sample_rate: u32,
    #[serde(default)]
    pub kind: MediaKind,
    #[serde(default)]
    pub peak_db: Option<f32>,
    #[serde(default)]
    pub rms_db: Option<f32>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PersistedSettings {
    pub auto_mix_enabled: bool,
    #[serde(default = "default_auto_mix_db")]
    pub auto_mix_db: f32,
    #[serde(default = "default_auto_mix_gate_db")]
    pub auto_mix_gate_db: f32,
    #[serde(default = "default_auto_mix_attack_ms")]
    pub auto_mix_attack_ms: u64,
    #[serde(default = "default_auto_mix_release_ms")]
    pub auto_mix_release_ms: u64,
    #[serde(default = "default_auto_mix_hold_ms")]
    pub auto_mix_hold_ms: u64,
    #[serde(default)]
    pub auto_mix_duck: f32,
    pub default_fade_in: f32,
    pub default_fade_out: f32,
    #[serde(default = "default_device_id")]
    pub default_device_id: String,
}

impl From<&EngineSettings> for PersistedSettings {
    fn from(s: &EngineSettings) -> Self {
        Self {
            auto_mix_enabled: s.auto_mix_enabled,
            auto_mix_db: s.auto_mix_db,
            auto_mix_gate_db: s.auto_mix_gate_db,
            auto_mix_attack_ms: s.auto_mix_attack_ms,
            auto_mix_release_ms: s.auto_mix_release_ms,
            auto_mix_hold_ms: s.auto_mix_hold_ms,
            auto_mix_duck: s.auto_mix_duck,
            default_fade_in: s.default_fade_in,
            default_fade_out: s.default_fade_out,
            default_device_id: s.default_device_id.clone(),
        }
    }
}

impl From<&PersistedSettings> for EngineSettings {
    fn from(p: &PersistedSettings) -> Self {
        Self {
            auto_mix_enabled: p.auto_mix_enabled,
            auto_mix_db: p.auto_mix_db,
            auto_mix_gate_db: p.auto_mix_gate_db,
            auto_mix_attack_ms: p.auto_mix_attack_ms,
            auto_mix_release_ms: p.auto_mix_release_ms,
            auto_mix_hold_ms: p.auto_mix_hold_ms,
            auto_mix_duck: p.auto_mix_duck,
            default_fade_in: p.default_fade_in,
            default_fade_out: p.default_fade_out,
            default_device_id: p.default_device_id.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PersistedWindow {
    pub width: u32,
    pub height: u32,
}

impl Default for PersistedWindow {
    fn default() -> Self {
        Self {
            width: 1500,
            height: 900,
        }
    }
}