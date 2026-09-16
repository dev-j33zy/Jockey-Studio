use std::fs;
use std::path::PathBuf;

use tauri::{AppHandle, Manager};
use thiserror::Error;

use crate::models::{AppState, PersistedWindow};

#[derive(Debug, Error)]
pub enum StorageError {
    #[error("path error: {0}")]
    Path(String),
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),
    #[error("serialization error: {0}")]
    Json(#[from] serde_json::Error),
}

pub fn state_path(app: &AppHandle) -> Result<PathBuf, StorageError> {
    let dir = app
        .path()
        .app_config_dir()
        .map_err(|e| StorageError::Path(e.to_string()))?;
    Ok(dir.join("app-state.json"))
}

pub fn load(app: &AppHandle) -> Result<AppState, StorageError> {
    let path = state_path(app)?;
    if !path.exists() {
        return Ok(AppState::default());
    }
    let raw = fs::read_to_string(&path)?;
    Ok(serde_json::from_str(&raw)?)
}

/// Atomically write the persisted state (tmp file + rename).
pub fn save(app: &AppHandle, state: &AppState) -> Result<(), StorageError> {
    let path = state_path(app)?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let tmp = path.with_extension("json.tmp");
    let raw = serde_json::to_string_pretty(state)?;
    fs::write(&tmp, raw)?;
    fs::rename(&tmp, &path)?;
    Ok(())
}

pub fn save_window(app: &AppHandle, width: u32, height: u32) -> Result<(), StorageError> {
    let mut state = load(app)?;
    state.window = PersistedWindow { width, height };
    save(app, &state)
}

/// Merge a full store payload (tiles, order, media, settings) from the UI and
/// persist it. The playback engine is the source of truth at runtime; this
/// keeps the layout/settings copy on disk fresh for restart.
#[tauri::command(rename_all = "camelCase")]
pub fn save_app_state(state: AppState, app: AppHandle) -> Result<(), String> {
    save(&app, &state).map_err(|e| e.to_string())
}

#[tauri::command(rename_all = "camelCase")]
pub fn load_app_state(app: AppHandle) -> Result<AppState, String> {
    load(&app).map_err(|e| e.to_string())
}

#[tauri::command(rename_all = "camelCase")]
pub fn save_window_prefs(app: AppHandle, width: u32, height: u32) -> Result<(), String> {
    save_window(&app, width, height).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn app_state_roundtrip() {
        let state = AppState {
            tile_order: vec!["a".into(), "b".into()],
            ..Default::default()
        };
        let raw = serde_json::to_string(&state).unwrap();
        let back: AppState = serde_json::from_str(&raw).unwrap();
        assert_eq!(back.tile_order, vec!["a".to_string(), "b".to_string()]);
        assert_eq!(back.window.width, 1500);
    }

    #[test]
    fn window_persisted() {
        let mut state = AppState::default();
        state.window = PersistedWindow { width: 1280, height: 720 };
        let raw = serde_json::to_string(&state).unwrap();
        let back: AppState = serde_json::from_str(&raw).unwrap();
        assert_eq!(back.window, PersistedWindow { width: 1280, height: 720 });
    }
}