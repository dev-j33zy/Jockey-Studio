use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, AtomicU8, Ordering};
use std::sync::Arc;
use std::time::Duration;

use parking_lot::Mutex;
use rodio::cpal;
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{OutputStream, OutputStreamHandle, Sink, Source};

use crate::audio::decoder::MediaSource;
use crate::models::*;

pub const HEARTBEAT_MS: u64 = 200;

const S_STOPPED: u8 = 0;
const S_PLAYING: u8 = 2;
const S_PAUSED: u8 = 3;
const S_ENDED: u8 = 4;

fn status_from_u8(v: u8) -> PlaybackStatus {
    match v {
        S_PLAYING => PlaybackStatus::Playing,
        S_PAUSED => PlaybackStatus::Paused,
        S_ENDED => PlaybackStatus::Ended,
        _ => PlaybackStatus::Stopped,
    }
}

/// Cheap atomics shared between commands and snapshot reads, so tile state can
/// be refreshed without re-plumbing cpal streams.
#[derive(Debug)]
pub struct TileShared {
    pub status: AtomicU8,
    pub position_bits: AtomicU64,
    pub duration_bits: AtomicU64,
    pub volume_bits: AtomicU32,
    pub loop_mode: AtomicU8,
    pub error: Mutex<Option<String>>,
    pub volume_ramp: AtomicBool,
    pub ramp_vol_bits: AtomicU32,
    pub ramp_rate_bits: AtomicU32,
    pub ramp_target_bits: AtomicU32,
}

impl TileShared {
    pub fn new() -> Self {
        Self {
            status: AtomicU8::new(S_STOPPED),
            position_bits: AtomicU64::new(0),
            duration_bits: AtomicU64::new(0),
            volume_bits: AtomicU32::new(0.9f32.to_bits()),
            loop_mode: AtomicU8::new(0),
            error: Mutex::new(None),
            volume_ramp: AtomicBool::new(false),
            ramp_vol_bits: AtomicU32::new(1.0f32.to_bits()),
            ramp_rate_bits: AtomicU32::new(1.0f32.to_bits()),
            ramp_target_bits: AtomicU32::new(1.0f32.to_bits()),
        }
    }

    pub fn position(&self) -> f64 {
        f64::from_bits(self.position_bits.load(Ordering::Relaxed))
    }

    pub fn set_position(&self, v: f64) {
        self.position_bits.store(v.to_bits(), Ordering::Relaxed);
    }

    pub fn duration(&self) -> f64 {
        f64::from_bits(self.duration_bits.load(Ordering::Relaxed))
    }

    pub fn set_duration(&self, v: f64) {
        self.duration_bits.store(v.to_bits(), Ordering::Relaxed);
    }

    pub fn set_volume(&self, v: f32) {
        self.volume_bits.store(v.clamp(0.0, 1.0).to_bits(), Ordering::Relaxed);
    }

    pub fn volume(&self) -> f32 {
        f32::from_bits(self.volume_bits.load(Ordering::Relaxed))
    }

    pub fn status(&self) -> PlaybackStatus {
        status_from_u8(self.status.load(Ordering::Relaxed))
    }

    pub fn set_status(&self, s: PlaybackStatus) {
        self.status.store(
            match s {
                PlaybackStatus::Playing => S_PLAYING,
                PlaybackStatus::Paused => S_PAUSED,
                PlaybackStatus::Ended => S_ENDED,
                _ => S_STOPPED,
            },
            Ordering::Relaxed,
        );
    }

    pub fn set_loop_mode(&self, m: LoopMode) {
        self.loop_mode.store(
            match m {
                // Off = 0, Endless = 1, Times(n) encodes the play count itself.
                LoopMode::Off => 0,
                LoopMode::Endless => 1,
                LoopMode::Times(n) => n,
            },
            Ordering::Relaxed,
        );
    }
}

/// rodio 0.20's `OutputStream` is `!Send` because it holds a `cpal::Stream`.
/// Every access here is serialized behind the engine mutex and the stream is
/// never concurrently borrowed, so wrapping it as Send is safe for this code.
/// Rolling RMS meter shared between the audio worker thread and the
/// heartbeat loop. Called from the cpal stream with u64-exact atomics so
/// bytes don't drift; `commit()` snapshots the window into an atomically
/// readable dB value and resets the accumulators.
#[derive(Debug, Default)]
pub struct LevelShared {
    sum_sq_bits: AtomicU64,
    count: AtomicU64,
    rms_db_bits: AtomicU64,
}

impl LevelShared {
    /// Accumulate one sample's squared value (sample is already `/ 32768`).
    pub fn add(&self, sq: f64) {
        self.sum_sq_bits.fetch_add(sq.to_bits(), Ordering::SeqCst);
        self.count.fetch_add(1, Ordering::SeqCst);
    }

    /// Consume the current window and publish its RMS level in dBFS.
    pub fn commit(&self) -> f64 {
        let count = self.count.swap(0, Ordering::SeqCst);
        let sum = f64::from_bits(self.sum_sq_bits.swap(0, Ordering::SeqCst));
        let rms = if count == 0 { 0.0 } else { (sum / count as f64).sqrt() };
        let db = if rms < 1e-6 { -96.0 } else { 20.0 * rms.log10() };
        self.rms_db_bits.store(db.to_bits(), Ordering::SeqCst);
        db
    }
}

/// Wraps a deck's source so every decoded sample feeds its shared level
/// meter before reaching the sink.
pub struct LevelSource<R> {
    inner: R,
    level: Arc<LevelShared>,
}

impl<R> LevelSource<R> {
    fn new(inner: R, level: Arc<LevelShared>) -> Self {
        Self { inner, level }
    }
}

impl<R: rodio::Source<Item = i16> + Send> Iterator for LevelSource<R> {
    type Item = i16;

    fn next(&mut self) -> Option<i16> {
        let s = self.inner.next()?;
        let f = s as f32 / 32768.0;
        self.level.add((f as f64) * (f as f64));
        Some(s)
    }
}

impl<R: rodio::Source<Item = i16> + Send> rodio::Source for LevelSource<R> {
    fn current_frame_len(&self) -> Option<usize> {
        self.inner.current_frame_len()
    }

    fn channels(&self) -> u16 {
        self.inner.channels()
    }

    fn sample_rate(&self) -> u32 {
        self.inner.sample_rate()
    }

    fn total_duration(&self) -> Option<Duration> {
        self.inner.total_duration()
    }
}

#[allow(dead_code)]
pub struct KeepAlive(OutputStream);
unsafe impl Send for KeepAlive {}

pub struct TilePlayer {
    pub id: TileId,
    pub media: Option<MediaId>,
    pub media_path: Option<PathBuf>,
    pub title: String,
    pub device_id: DeviceId,
    pub volume: f32,
    pub muted: bool,
    pub duckers: std::collections::HashMap<TileId, f32>,
    pub am_ducked: bool,
    pub am_hold: u64,
    pub level: Arc<LevelShared>,
    pub loop_mode: LoopMode,
    loop_plays_remaining: u8,
    pub fades: FadeConfig,
    pub error: Option<String>,
    pub shared: Arc<TileShared>,
    pub stream: Option<KeepAlive>,
    pub sink: Option<Arc<Sink>>,
    // Absolute time the current stream was seeked/started at. rodio's
    // `get_pos()` counts from the start of the *current* sound, so the
    // reported position is `pos_offset + sink.get_pos()`.
    pos_offset: f64,
}

impl TilePlayer {
    pub fn new(id: TileId) -> Self {
        Self {
            id: id.clone(),
            media: None,
            media_path: None,
            title: String::new(),
            device_id: DEFAULT_DEVICE_ID.to_string(),
            volume: 0.9,
            muted: false,
            duckers: std::collections::HashMap::new(),
            am_ducked: false,
            am_hold: 0,
            level: Arc::new(LevelShared::default()),
            loop_mode: LoopMode::Off,
            loop_plays_remaining: 0,
            fades: FadeConfig::default(),
            error: None,
            shared: Arc::new(TileShared::new()),
            stream: None,
            sink: None,
            pos_offset: 0.0,
        }
    }

    pub fn is_playing(&self) -> bool {
        self.shared.status() == PlaybackStatus::Playing
    }

    /// Cancel any in-progress auto-mix duck/restore volume ramp.
    pub fn cancel_ramp(&mut self) {
        self.shared.volume_ramp.store(false, Ordering::Relaxed);
    }

    /// Start a volume ramp from `cur` toward `target` with the same
    /// time-constant envelope rodio's `fade_in` uses: each heartbeat advances
    /// halfway-window `rate = 1 - e^(-HEARTBEAT_MS/time_ms)` of the remaining
    /// gap, so attack/release is smooth and time-based regardless of tick
    /// jitter.
    pub fn arm_ramp(&self, cur: f32, target: f32, time_ms: f64) {
        let rate = 1.0_f64 - (-(HEARTBEAT_MS as f64).min(time_ms.max(1.0)) / time_ms.max(1.0)).exp();
        let rate = rate.clamp(0.0, 1.0) as f32;
        self.shared.ramp_vol_bits.store(cur.to_bits(), Ordering::Relaxed);
        self.shared.ramp_rate_bits.store(rate.to_bits(), Ordering::Relaxed);
        self.shared.ramp_target_bits.store(target.to_bits(), Ordering::Relaxed);
        self.shared.volume_ramp.store(true, Ordering::Relaxed);
    }

    pub fn tile_state(&self) -> TileState {
        let shared = &self.shared;
        TileState {
            id: self.id.clone(),
            media: self.media.clone(),
            title: self.title.clone(),
            device_id: self.device_id.clone(),
            status: shared.status(),
            position_secs: shared.position(),
            duration_secs: shared.duration(),
            volume: self.volume,
            muted: self.muted,
            loop_mode: self.loop_mode,
            fades: self.fades,
            error: self.error.clone().or_else(|| shared.error.lock().clone()),
        }
    }
}

pub type SharedEngine = Arc<Mutex<AudioEngine>>;

pub struct AudioEngine {
    pub tiles: Vec<TilePlayer>,
    pub media: Vec<MediaItem>,
    pub devices: Vec<OutputDevice>,
    pub settings: EngineSettings,
    pub sink: Option<Arc<dyn Fn(EngineEvent) + Send + Sync>>,
    tick: u64,
}

impl AudioEngine {
    pub fn new() -> Self {
        Self {
            tiles: Vec::new(),
            media: Vec::new(),
            devices: Vec::new(),
            settings: EngineSettings::default(),
            sink: None,
            tick: 0,
        }
    }

    pub fn set_sink(&mut self, sink: Arc<dyn Fn(EngineEvent) + Send + Sync>) {
        self.sink = Some(sink);
    }

    pub fn shutdown(&mut self) {
        for tile in self.tiles.iter_mut() {
            tile.sink = None;
            tile.stream = None;
            tile.shared.set_status(PlaybackStatus::Stopped);
        }
    }

    fn emit(&self, ev: EngineEvent) {
        if let Some(s) = &self.sink {
            s(ev);
        }
    }

    pub fn media_by_id(&self, id: &str) -> Option<&MediaItem> {
        self.media.iter().find(|m| m.id == id)
    }

    pub fn tile_index(&self, id: &str) -> Option<usize> {
        self.tiles.iter().position(|t| t.id == id)
    }

    // ---------------------------------------------------------------- media

    pub fn import_media(&mut self, path: String) -> Result<MediaItem, String> {
        let p = PathBuf::from(&path);
        let info = crate::audio::decoder::probe(&p)
            .map_err(|e| format!("unsupported media: {e}"))?;

        let item = MediaItem {
            id: uuid::Uuid::new_v4().to_string(),
            path: path.clone(),
            title: info.title,
            container: info.container,
            codec: info.codec,
            duration_secs: info.duration.unwrap_or(0.0),
            channels: info.channels,
            sample_rate: info.sample_rate,
            kind: if info.is_video { MediaKind::Video } else { MediaKind::Audio },
            peak_db: None,
            rms_db: None,
        };
        self.media.push(item.clone());
        self.emit(EngineEvent {
            kind: "media".into(),
            action: "library".into(),
            tile_id: None,
            payload: serde_json::json!({ "mediaId": item.id }),
        });
        Ok(item)
    }

    /// Record background loudness analysis for an imported file.
    pub fn set_media_analysis(&mut self, media_id: &str, peak_db: f32, rms_db: f32) {
        if let Some(m) = self.media.iter_mut().find(|m| m.id == media_id) {
            m.peak_db = Some(peak_db);
            m.rms_db = Some(rms_db);
            let tile_id = self
                .tiles
                .iter()
                .find(|t| t.media.as_deref() == Some(media_id))
                .map(|t| t.id.clone());
            self.emit(EngineEvent {
                kind: "media".into(),
                action: "analyzed".into(),
                tile_id,
                payload: serde_json::json!({ "mediaId": media_id, "peakDb": peak_db, "rmsDb": rms_db }),
            });
        }
    }

    // ----------------------------------------------------------------- tiles

    pub fn add_tiles(&mut self, states: Vec<TileState>) {
        for st in states {
            if self.tile_index(&st.id).is_none() {
                let mut t = TilePlayer::new(st.id.clone());
                t.title = st.title;
                t.volume = st.volume;
                t.muted = st.muted;
                t.loop_mode = st.loop_mode;
                t.loop_plays_remaining = st.loop_mode.repeat_count();
                t.fades = st.fades;
                t.device_id = st.device_id;
                t.media = st.media.clone();
                t.shared.set_volume(st.volume);
                t.shared.set_loop_mode(st.loop_mode);
                t.shared.set_duration(st.duration_secs);
                self.tiles.push(t);
            }
        }
    }

    pub fn add_empty_tiles(&mut self, count: usize) -> Vec<TileState> {
        let mut out = Vec::new();
        for _ in 0..count {
            let id = uuid::Uuid::new_v4().to_string();
            let t = TilePlayer::new(id.clone());
            let st = t.tile_state();
            self.tiles.push(t);
            out.push(st);
        }
        out
    }

    pub fn remove_tile(&mut self, id: &str) {
        self.restore_ducked(id);
        if let Some(i) = self.tile_index(id) {
            let t = &mut self.tiles[i];
            t.sink = None;
            t.stream = None;
            t.shared.set_status(PlaybackStatus::Stopped);
            self.tiles.remove(i);
            self.emit(EngineEvent {
                kind: "tile".into(),
                action: "removed".into(),
                tile_id: Some(id.to_string()),
                payload: serde_json::Value::Null,
            });
        }
    }

    /// Clear every deck back to its fresh empty state: stop playback, drop any
    /// loaded media and its audio sinks, and reset volume, mute, loop, fades,
    /// device routing and errors to their defaults. The number of decks and
    /// their ids are preserved.
    pub fn reset_all(&mut self) -> usize {
        let ids: Vec<TileId> = self.tiles.iter().map(|t| t.id.clone()).collect();
        for id in &ids {
            self.reset_tile(id);
        }
        let count = ids.len();
        self.emit(EngineEvent {
            kind: "tile".into(),
            action: "reset".into(),
            tile_id: None,
            payload: serde_json::json!({ "count": count }),
        });
        count
    }

    fn reset_tile(&mut self, tile_id: &str) {
        let Some(idx) = self.tile_index(tile_id) else {
            return;
        };
        self.restore_ducked(tile_id);
        self.cancel_fade(idx);
        let tile = &mut self.tiles[idx];
        tile.sink = None;
        tile.stream = None;
        tile.duckers.clear();
        tile.am_ducked = false;
        tile.am_hold = 0;
        tile.media = None;
        tile.media_path = None;
        tile.title = String::new();
        tile.volume = 0.9;
        tile.muted = false;
        tile.loop_mode = LoopMode::Off;
        tile.loop_plays_remaining = 0;
        tile.device_id = DEFAULT_DEVICE_ID.to_string();
        tile.fades = FadeConfig::default();
        tile.error = None;
        tile.shared.set_status(PlaybackStatus::Stopped);
        tile.shared.set_position(0.0);
        tile.shared.set_duration(0.0);
        tile.shared.set_volume(0.9);
        tile.shared.set_loop_mode(LoopMode::Off);
        tile.shared.error.lock().take();
        tile.shared.volume_ramp.store(false, Ordering::Relaxed);
    }

    pub fn reorder_tiles(&mut self, order: Vec<TileId>) {
        let mut by_id: std::collections::HashMap<TileId, TilePlayer> = self
            .tiles
            .drain(..)
            .map(|t| (t.id.clone(), t))
            .collect();
        let mut next = Vec::with_capacity(order.len());
        for id in order {
            if let Some(t) = by_id.remove(&id) {
                next.push(t);
            }
        }
        next.extend(by_id.into_values());
        self.tiles = next;
    }

    pub fn load_media_into_tile(
        &mut self,
        tile_id: &str,
        media_id: &str,
    ) -> Result<TileState, String> {
        let idx = self
            .tile_index(tile_id)
            .ok_or_else(|| "tile not found".to_string())?;
        let media = self
            .media_by_id(media_id)
            .ok_or_else(|| "media not found".to_string())?
            .clone();

        self.restore_ducked(tile_id);
        let tile = &mut self.tiles[idx];
        tile.sink = None;
        tile.stream = None;
        tile.media = Some(media.id.clone());
        tile.media_path = Some(PathBuf::from(&media.path));
        tile.title = media.title.clone();
        tile.error = None;
        tile.shared.error.lock().clone_from(&None);
        tile.shared.set_status(PlaybackStatus::Stopped);
        tile.shared.set_position(0.0);
        tile.shared.set_duration(media.duration_secs);
        tile.loop_plays_remaining = tile.loop_mode.repeat_count();

        let st = self.tiles[idx].tile_state();
        self.emit(EngineEvent {
            kind: "tile".into(),
            action: "loaded".into(),
            tile_id: Some(tile_id.to_string()),
            payload: serde_json::json!({ "mediaId": media_id }),
        });
        Ok(st)
    }

    // ------------------------------------------------------------- transport

    pub fn play(&mut self, tile_id: &str) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        let (has_media, status) = {
            let t = &self.tiles[idx];
            (t.media_path.is_some(), t.shared.status())
        };
        if !has_media || status == PlaybackStatus::Playing {
            return;
        }

        // Resume a live paused sink.
        if status == PlaybackStatus::Paused {
            let resume = self.tiles[idx].sink.is_some();
            if resume {
                let sink = self.tiles[idx].sink.as_ref().unwrap().clone();
                sink.play();
                self.tiles[idx].shared.set_status(PlaybackStatus::Playing);
                self.emit_tile_status(tile_id, "playing");
                return;
            }
        }

        // Resume from wherever the deck's timeline is: live for paused sinks,
        // from the stored position otherwise. A ended track restarts from the
        // top so pressing play after the song out begins it again.
        let from = match status {
            PlaybackStatus::Paused | PlaybackStatus::Stopped => {
                self.tiles[idx].shared.position()
            }
            _ => 0.0,
        };

        // The deck's loop mode is a high-level setting: a fresh playback
        // session re-arms the counter so a `Times(n)` loop plays n times then
        // ends. Resumes (paused) and seeks never re-arm it.
        if matches!(status, PlaybackStatus::Stopped | PlaybackStatus::Ended) {
            self.tiles[idx].loop_plays_remaining = self.tiles[idx].loop_mode.repeat_count();
        }

        if let Err(err) = self.start_playback(idx, Some(from)) {
            self.fail_tile(idx, tile_id, err);
        }
    }

    pub fn pause(&mut self, tile_id: &str) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        self.cancel_fade(idx);
        self.restore_ducked(tile_id);
        let tile = &self.tiles[idx];
        if tile.shared.status() != PlaybackStatus::Playing {
            return;
        }
        if let Some(sink) = &tile.sink {
            sink.pause();
        }
        tile.shared.set_status(PlaybackStatus::Paused);
        self.emit_tile_status(tile_id, "paused");
    }

    pub fn stop(&mut self, tile_id: &str) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        self.cancel_fade(idx);
        self.restore_ducked(tile_id);
        let tile = &mut self.tiles[idx];
        let had_sink = tile.sink.is_some();
        if let Some(sink) = &tile.sink {
            sink.stop();
        }
        tile.sink = None;
        tile.stream = None;
        tile.shared.set_status(PlaybackStatus::Stopped);
        tile.shared.set_position(0.0);
        if had_sink {
            self.emit_tile_status(tile_id, "stopped");
        }
    }

    pub fn seek(&mut self, tile_id: &str, secs: f64) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        let (dur, status) = {
            let t = &self.tiles[idx];
            (t.shared.duration(), t.shared.status())
        };
        let target = secs.clamp(0.0, if dur > 0.0 { dur } else { f64::MAX });

        match status {
            PlaybackStatus::Playing => {
                self.cancel_fade(idx);
                self.tiles[idx].shared.set_position(target);
                if let Err(err) = self.start_playback(idx, Some(target)) {
                    self.fail_tile(idx, tile_id, err);
                }
            }
            PlaybackStatus::Paused => {
                self.cancel_fade(idx);
                let t = &mut self.tiles[idx];
                t.sink = None;
                t.stream = None;
                t.pos_offset = target;
                t.shared.set_position(target);
            }
            _ => {
                let t = &mut self.tiles[idx];
                t.pos_offset = target;
                t.shared.set_position(target);
            }
        }
    }

    pub fn set_volume(&mut self, tile_id: &str, volume: f32) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        let v = volume.clamp(0.0, 1.0);
        let tile = &mut self.tiles[idx];
        tile.volume = v;
        tile.duckers.clear();
        tile.cancel_ramp();
        // mute not changed here; effective volume only.
        let effective = if tile.muted { 0.0 } else { v };
        tile.shared.set_volume(effective);
        if let Some(sink) = &tile.sink {
            sink.set_volume(effective);
        }
    }

    pub fn set_muted(&mut self, tile_id: &str, muted: bool) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        let tile = &mut self.tiles[idx];
        tile.muted = muted;
        tile.cancel_ramp();
        let effective = if muted { 0.0 } else { tile.volume };
        tile.shared.set_volume(effective);
        if let Some(sink) = &tile.sink {
            sink.set_volume(effective);
        }
    }

    pub fn set_loop(&mut self, tile_id: &str, mode: LoopMode) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        {
            let tile = &mut self.tiles[idx];
            tile.loop_mode = mode;
            tile.loop_plays_remaining = mode.repeat_count();
            tile.shared.set_loop_mode(mode);
        }
        let restart = self.tiles[idx].sink.is_some()
            && self.tiles[idx].shared.status() == PlaybackStatus::Playing;
        if restart {
            let from = self.tiles[idx].shared.position();
            self.cancel_fade(idx);
            if let Err(err) = self.start_playback(idx, Some(from)) {
                self.fail_tile(idx, tile_id, err);
            }
        }
    }

    pub fn set_fades(&mut self, tile_id: &str, fades: FadeConfig) {
        if let Some(idx) = self.tile_index(tile_id) {
            self.tiles[idx].fades = fades;
        }
    }

    pub fn set_settings(&mut self, settings: EngineSettings) {
        self.settings = settings;
        self.emit(EngineEvent {
            kind: "engine".into(),
            action: "settings".into(),
            tile_id: None,
            payload: serde_json::json!({ "autoMixEnabled": self.settings.auto_mix_enabled }),
        });
    }

    pub fn set_tile_device(&mut self, tile_id: &str, device_id: String) {
        let idx = match self.tile_index(tile_id) {
            Some(i) => i,
            None => return,
        };
        let was_playing = {
            let t = &self.tiles[idx];
            t.sink.is_some() && t.shared.status() == PlaybackStatus::Playing
        };
        let from = self.tiles[idx].shared.position();
        self.tiles[idx].device_id = device_id.clone();
        self.cancel_fade(idx);
        if was_playing {
            {
                let t = &mut self.tiles[idx];
                t.sink = None;
                t.stream = None;
            }
            if let Err(err) = self.start_playback(idx, Some(from)) {
                self.fail_tile(idx, tile_id, err);
            }
        }
    }

    pub fn set_devices(&mut self, devices: Vec<OutputDevice>) {
        self.devices = devices;
    }

    pub fn get_snapshot(&mut self) -> EngineSnapshot {
        self.heartbeat_tick();
        EngineSnapshot {
            tiles: self.tiles.iter().map(|t| t.tile_state()).collect(),
            media: self.media.clone(),
            devices: self.devices.clone(),
            settings: self.settings.clone(),
        }
    }

    pub fn restore(&mut self, state: &AppState) {
        self.settings = EngineSettings::from(&state.settings);

        for pm in &state.media {
            if self.media.iter().any(|m| m.id == pm.id) {
                continue;
            }
            let p = PathBuf::from(&pm.path);
            let mut item = MediaItem {
                id: pm.id.clone(),
                path: pm.path.clone(),
                title: if pm.title.is_empty() {
                    p.file_stem()
                        .and_then(|s| s.to_str())
                        .unwrap_or("Untitled")
                        .to_string()
                } else {
                    pm.title.clone()
                },
                container: pm.container.clone(),
                codec: pm.codec.clone(),
                duration_secs: pm.duration_secs,
                channels: pm.channels,
                sample_rate: pm.sample_rate,
                kind: pm.kind,
                peak_db: pm.peak_db,
                rms_db: pm.rms_db,
            };
            if item.duration_secs <= 0.0 {
                if let Ok(info) = crate::audio::decoder::probe(&p) {
                    item.container = info.container;
                    item.codec = info.codec;
                    item.duration_secs = info.duration.unwrap_or(0.0);
                    item.channels = info.channels;
                    item.sample_rate = info.sample_rate;
                    item.kind = if info.is_video { MediaKind::Video } else { MediaKind::Audio };
                }
            }
            self.media.push(item);
        }

        let mut by_id: std::collections::HashMap<TileId, TilePlayer> = std::collections::HashMap::new();
        for pt in &state.tiles {
            let mut t = TilePlayer::new(pt.id.clone());
            t.device_id = pt.device_id.clone();
            t.volume = pt.volume;
            t.muted = pt.muted;
            t.loop_mode = pt.loop_mode;
            t.loop_plays_remaining = pt.loop_mode.repeat_count();
            t.fades = pt.fades.clone();
            if let Some(mid) = &pt.media_id {
                if let Some(m) = self.media_by_id(mid) {
                    t.media = Some(m.id.clone());
                    t.media_path = Some(PathBuf::from(&m.path));
                    t.title = m.title.clone();
                    t.shared.set_duration(m.duration_secs);
                }
            }
            t.shared.set_volume(if pt.muted { 0.0 } else { pt.volume });
            t.shared.set_loop_mode(pt.loop_mode);
            by_id.insert(pt.id.clone(), t);
        }

        let mut ordered = Vec::with_capacity(state.tile_order.len());
        for id in &state.tile_order {
            if let Some(t) = by_id.remove(id) {
                ordered.push(t);
            }
        }
        ordered.extend(by_id.into_values());
        self.tiles = ordered;
    }

    // -------------------------------------------------------------- internal

    fn fail_tile(&mut self, idx: usize, tile_id: &str, err: String) {
        self.restore_ducked(tile_id);
        {
            let t = &mut self.tiles[idx];
            t.shared.set_status(PlaybackStatus::Stopped);
            t.shared.error.lock().clone_from(&Some(err.clone()));
            t.error = Some(err);
        }
        self.emit_tile_status(tile_id, "error");
    }

    fn emit_tile_status(&self, tile_id: &str, action: &str) {
        let pos = self
            .tiles
            .iter()
            .find(|t| t.id == tile_id)
            .map(|t| t.shared.position())
            .unwrap_or(0.0);
        self.emit(EngineEvent {
            kind: "tile".into(),
            action: action.to_string(),
            tile_id: Some(tile_id.to_string()),
            payload: serde_json::json!({ "position": pos }),
        });
    }

    fn cancel_fade(&mut self, idx: usize) {
        self.tiles[idx].cancel_ramp();
    }

    fn start_playback(&mut self, idx: usize, from: Option<f64>) -> Result<(), String> {
        let tile = &self.tiles[idx];
        let path = tile
            .media_path
            .clone()
            .ok_or_else(|| "no media loaded".to_string())?;
        let pinned_device = tile.device_id != DEFAULT_DEVICE_ID;
        // `default` means "follow the app-level default output device"; a
        // concrete device name pins this deck to that device until removed.
        let device_id = if pinned_device {
            tile.device_id.clone()
        } else {
            self.settings.default_device_id.clone()
        };
        let is_loop = matches!(tile.loop_mode, LoopMode::Endless);
        let fade_in = tile.fades.fade_in;
        let effective_volume = if tile.muted { 0.0 } else { tile.volume };
        let start = from.unwrap_or(0.0).max(0.0);

        let mut source = MediaSource::open(&path).map_err(|e| e.to_string())?;
        if start > 0.0 {
            source.seek(start).map_err(|e| e.to_string())?;
        }
        let total = source.total_duration();

        let fade = Duration::from_secs_f32(fade_in.max(0.0));
        let source: Box<dyn Source<Item = i16> + Send> = if is_loop {
            Box::new(source.repeat_infinite().fade_in(fade))
        } else {
            Box::new(source.fade_in(fade))
        };
        let level = self.tiles[idx].level.clone();
        let source = Box::new(LevelSource::new(source, level)) as Box<dyn Source<Item = i16> + Send>;

        let mut effective = device_id;
        let device = if effective == DEFAULT_DEVICE_ID {
            None
        } else {
            self.resolve_device(&effective)
        };
        if device.is_none() && effective != DEFAULT_DEVICE_ID {
            if pinned_device {
                return Err("output device not found".to_string());
            }
            // The app-level default was unplugged; fall back to the OS default.
            effective = DEFAULT_DEVICE_ID.to_string();
        }

        let output = if effective == DEFAULT_DEVICE_ID {
            OutputStream::try_default()
        } else {
            OutputStream::try_from_device(device.as_ref().expect("device checked above"))
        };
        let (stream, handle): (OutputStream, OutputStreamHandle) = output.map_err(|e| e.to_string())?;

        let sink = Arc::new(Sink::try_new(&handle).map_err(|e| e.to_string())?);
        sink.set_volume(effective_volume);
        sink.append(source);

        let tile = &mut self.tiles[idx];
        tile.stream = Some(KeepAlive(stream));
        tile.sink = Some(sink);
        tile.error = None;
        tile.shared.error.lock().clone_from(&None);
        tile.shared.set_status(PlaybackStatus::Playing);
        tile.pos_offset = start;
        tile.shared.set_position(start);
        tile.shared.set_duration(total.map(|d| d.as_secs_f64()).unwrap_or(0.0));

        let id = tile.id.clone();
        self.emit_tile_status(&id, "playing");
        Ok(())
    }

    /// vMix-style auto-mix group: duck every OTHER auto-mix deck down by
    /// `settings.auto_mix_db` (dB) for the calling deck's benefit, using the
    /// attack time constant. Decks without auto-mix engaged are never ducked.
    /// No-op when the caller already has auto-mix engaged. Ducking is reverted
    /// by `restore_ducked` once the caller goes silent (or stops/pauses/ends).
    fn duck_others(&mut self, starter_id: &str) {
        if self
            .tiles
            .iter()
            .any(|t| t.id == starter_id && t.am_ducked)
        {
            return;
        }
        let amount_db = self.settings.auto_mix_db.clamp(0.0, 60.0);
        let factor = 10.0_f32.powf(-amount_db / 20.0);
        let attack = self.settings.auto_mix_attack_ms as f64;
        for tile in self.tiles.iter_mut() {
            if tile.id == starter_id
                || tile.sink.is_none()
                || !tile.is_playing()
                || !tile.fades.auto_mix
            {
                continue;
            }
            let base = tile.volume;
            if base <= 0.0 {
                continue;
            }
            tile.duckers.entry(starter_id.to_string()).or_insert(base);
            let target = (base * factor).clamp(0.0, 1.0);
            let cur = tile.shared.volume();
            if (cur - target).abs() > f32::EPSILON {
                tile.arm_ramp(cur, target, attack);
            }
        }
        if let Some(t) = self.tiles.iter_mut().find(|t| t.id == starter_id) {
            t.am_ducked = true;
        }
    }

    /// Remove a deck's auto-mix engagement: ramp back up any tiles it was
    /// ducking (restore their base volume instead of stopping them). Only
    /// restores when no other deck is still ducking that tile.
    fn restore_ducked(&mut self, ducker_id: &str) {
        let release = self.settings.auto_mix_release_ms.max(1) as f64;
        for tile in self.tiles.iter_mut() {
            if let Some(base) = tile.duckers.remove(ducker_id) {
                if tile.duckers.is_empty() {
                    let cur = tile.shared.volume();
                    let target = base.clamp(0.0, 1.0);
                    if (cur - target).abs() > f32::EPSILON {
                        tile.arm_ramp(cur, target, release);
                    }
                }
            }
        }
    }

    fn resolve_device(&self, device_id: &str) -> Option<cpal::Device> {
        let host = cpal::default_host();
        host.output_devices()
            .ok()?
            .into_iter()
            .find(|d| d.name().map(|n| n == device_id).unwrap_or(false))
    }

    /// Progress sink states (positions, pause sync, end-of-track, fade-outs).
    /// Called on every `get_snapshot`, which the UI refreshes periodically.
    fn heartbeat_tick(&mut self) {
        self.tick += 1;
        let mut events: Vec<(TileId, &'static str)> = Vec::new();
        let mut ended: Vec<TileId> = Vec::new();
        let mut restart: Vec<TileId> = Vec::new();
        let mut engage: Vec<TileId> = Vec::new();
        let mut disengage: Vec<TileId> = Vec::new();
        let gate_db = self.settings.auto_mix_gate_db as f64;
        let hold_ticks = (self.settings.auto_mix_hold_ms / HEARTBEAT_MS).max(1);

        for tile in self.tiles.iter_mut() {
            let Some(sink) = tile.sink.clone() else { continue };

            // Auto-mix duck / restore volume-ramp progress (exponential).
            if tile.shared.volume_ramp.load(Ordering::Relaxed) {
                let cur = f32::from_bits(tile.shared.ramp_vol_bits.load(Ordering::Relaxed));
                let target = f32::from_bits(tile.shared.ramp_target_bits.load(Ordering::Relaxed));
                let rate = f32::from_bits(tile.shared.ramp_rate_bits.load(Ordering::Relaxed)).max(0.0);
                let mut next = cur + (target - cur) * rate;
                if (next - target).abs() <= 1e-4 {
                    next = target;
                }
                tile.shared.ramp_vol_bits.store(next.to_bits(), Ordering::Relaxed);
                tile.shared.set_volume(next);
                let effective = if tile.muted { 0.0 } else { next };
                sink.set_volume(effective);
                if (next - target).abs() < f32::EPSILON {
                    tile.shared.volume_ramp.store(false, Ordering::Relaxed);
                }
            }

            // vMix-style auto-mix: while this deck's level stays above the
            // gate it ducks every other playing deck; once it drops below for
            // longer than the hold time the others are restored.
            if tile.fades.auto_mix {
                if tile.muted || !tile.is_playing() {
                    if tile.am_ducked {
                        tile.am_ducked = false;
                        disengage.push(tile.id.clone());
                    }
                    tile.am_hold = 0;
                } else {
                    let rms_db = tile.level.commit();
                    if rms_db > gate_db {
                        tile.am_hold = 0;
                        if !tile.am_ducked {
                            engage.push(tile.id.clone());
                        }
                    } else if tile.am_ducked {
                        tile.am_hold += 1;
                        if tile.am_hold >= hold_ticks {
                            tile.am_hold = 0;
                            tile.am_ducked = false;
                            disengage.push(tile.id.clone());
                        }
                    } else {
                        tile.am_hold += 1;
                    }
                }
            } else if tile.am_ducked {
                tile.am_ducked = false;
                tile.am_hold = 0;
                disengage.push(tile.id.clone());
            }

            // Position sync.
            tile.shared
                .set_position(tile.pos_offset + sink.get_pos().as_secs_f64());

            // Pause-state sync.
            let paused = sink.is_paused();
            match tile.shared.status() {
                PlaybackStatus::Playing if paused => {
                    tile.shared.set_status(PlaybackStatus::Paused);
                    events.push((tile.id.clone(), "paused"));
                }
                PlaybackStatus::Paused if !paused => {
                    tile.shared.set_status(PlaybackStatus::Playing);
                    events.push((tile.id.clone(), "playing"));
                }
                _ => {}
            }

            // End of track for one-shot sources. Counted loop modes bring the
            // next playthrough in with a fresh fade; `endless` never gets here
            // because its source repeats seamlessly.
            if sink.empty()
                && matches!(
                    tile.shared.status(),
                    PlaybackStatus::Playing | PlaybackStatus::Paused
                )
            {
                sink.stop();
                tile.sink = None;
                tile.stream = None;
                if matches!(tile.loop_mode, LoopMode::Times(_))
                    && tile.loop_plays_remaining > 1
                {
                    tile.loop_plays_remaining -= 1;
                    restart.push(tile.id.clone());
                } else {
                    tile.shared.set_status(PlaybackStatus::Ended);
                    events.push((tile.id.clone(), "ended"));
                    ended.push(tile.id.clone());
                }
            }
        }

        for id in restart {
            let idx = match self.tile_index(&id) {
                Some(i) => i,
                None => continue,
            };
            if let Err(err) = self.start_playback(idx, Some(0.0)) {
                self.fail_tile(idx, &id, err);
            }
        }

        for id in engage {
            self.duck_others(&id);
        }
        for id in disengage {
            self.restore_ducked(&id);
        }
        for id in ended {
            self.restore_ducked(&id);
        }
        for (id, action) in events {
            self.emit_tile_status(&id, action);
        }
    }
}