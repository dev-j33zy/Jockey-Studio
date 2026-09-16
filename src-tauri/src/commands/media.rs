use tauri::Manager;

use crate::audio::engine::SharedEngine;
use crate::models::*;

fn engine<'a>(
    app: &'a tauri::AppHandle,
) -> parking_lot::MutexGuard<'a, crate::audio::engine::AudioEngine> {
    let state = app.state::<SharedEngine>();
    state.inner().lock()
}

#[tauri::command(rename_all = "camelCase")]
pub fn import_media(app: tauri::AppHandle, path: String) -> Result<MediaItem, String> {
    let item = engine(&app).import_media(path.clone())?;
    let media_id = item.id.clone();
    let eng: SharedEngine = {
        let state = app.state::<SharedEngine>();
        state.inner().clone()
    };
    // Scan the file for overall loudness on a background thread so the
    // command returns immediately; results are patched into the engine and
    // pushed to the UI via the "analyzed" event.
    std::thread::Builder::new()
        .name("media-analyze".into())
        .spawn(move || {
            let Ok(stats) = crate::audio::decoder::analyze(std::path::Path::new(&path)) else {
                return;
            };
            let mut e = eng.lock();
            e.set_media_analysis(&media_id, stats.peak_db, stats.rms_db);
        })
        .ok();
    Ok(item)
}

#[tauri::command(rename_all = "camelCase")]
pub fn add_tiles(app: tauri::AppHandle, tiles: Vec<TileState>) -> Result<(), String> {
    engine(&app).add_tiles(tiles);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn add_empty_tiles(app: tauri::AppHandle, count: usize) -> Result<Vec<TileState>, String> {
    Ok(engine(&app).add_empty_tiles(count))
}

#[tauri::command(rename_all = "camelCase")]
pub fn remove_tile(app: tauri::AppHandle, tile_id: String) -> Result<(), String> {
    engine(&app).remove_tile(&tile_id);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn reorder_tiles(app: tauri::AppHandle, order: Vec<String>) -> Result<(), String> {
    engine(&app).reorder_tiles(order);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn load_media_into_tile(
    app: tauri::AppHandle,
    tile_id: String,
    media_id: String,
) -> Result<TileState, String> {
    engine(&app).load_media_into_tile(&tile_id, &media_id)
}

#[tauri::command(rename_all = "camelCase")]
pub fn play(app: tauri::AppHandle, tile_id: String) -> Result<(), String> {
    engine(&app).play(&tile_id);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn pause(app: tauri::AppHandle, tile_id: String) -> Result<(), String> {
    engine(&app).pause(&tile_id);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn stop(app: tauri::AppHandle, tile_id: String) -> Result<(), String> {
    engine(&app).stop(&tile_id);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn seek(app: tauri::AppHandle, tile_id: String, secs: f64) -> Result<(), String> {
    engine(&app).seek(&tile_id, secs);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn set_volume(app: tauri::AppHandle, tile_id: String, volume: f32) -> Result<(), String> {
    engine(&app).set_volume(&tile_id, volume);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn set_muted(app: tauri::AppHandle, tile_id: String, muted: bool) -> Result<(), String> {
    engine(&app).set_muted(&tile_id, muted);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn set_loop(app: tauri::AppHandle, tile_id: String, mode: LoopMode) -> Result<(), String> {
    engine(&app).set_loop(&tile_id, mode);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn set_fades(app: tauri::AppHandle, tile_id: String, fades: FadeConfig) -> Result<(), String> {
    engine(&app).set_fades(&tile_id, fades);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn set_settings(app: tauri::AppHandle, settings: EngineSettings) -> Result<(), String> {
    engine(&app).set_settings(settings);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn set_tile_device(
    app: tauri::AppHandle,
    tile_id: String,
    device_id: String,
) -> Result<(), String> {
    engine(&app).set_tile_device(&tile_id, device_id);
    Ok(())
}

#[tauri::command(rename_all = "camelCase")]
pub fn get_snapshot(app: tauri::AppHandle) -> Result<EngineSnapshot, String> {
    Ok(engine(&app).get_snapshot())
}