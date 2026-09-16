mod audio;
mod commands;
mod models;
mod storage;
mod updater;

use std::sync::Arc;

use parking_lot::Mutex;
use tauri::{Emitter, Manager};

use audio::engine::{AudioEngine, SharedEngine};
use models::EngineEvent;

/// Boot the Tauri app, wire the audio engine, restore state, and forward engine
/// events to the frontend.
pub fn run() {
    let engine: SharedEngine = Arc::new(Mutex::new(AudioEngine::new()));

    let builder = tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(engine.clone())
        .invoke_handler(tauri::generate_handler![
            commands::media::import_media,
            commands::media::add_tiles,
            commands::media::add_empty_tiles,
            commands::media::remove_tile,
            commands::media::reorder_tiles,
            commands::media::load_media_into_tile,
            commands::media::play,
            commands::media::pause,
            commands::media::stop,
            commands::media::seek,
            commands::media::set_volume,
            commands::media::set_muted,
            commands::media::set_loop,
            commands::media::set_fades,
            commands::media::set_settings,
            commands::media::set_tile_device,
            commands::media::get_snapshot,
            commands::devices::list_devices,
            commands::devices::refresh_devices,
            storage::save_app_state,
            storage::load_app_state,
            storage::save_window_prefs,
            updater::check_for_updates,
            updater::install_update,
        ])
        .setup(move |app| {
            // Forward engine events straight to the webview.
            let app_handle = app.handle().clone();
            let emit_handle = app_handle.clone();
            let sink: Arc<dyn Fn(EngineEvent) + Send + Sync> = Arc::new(move |ev| {
                let _ = emit_handle.emit("engine://event", ev);
            });
            {
                let mut e = engine.lock();
                e.set_sink(sink);
                // Restore persisted tiles/settings/routing.
                if let Ok(state) = storage::load(&app_handle) {
                    e.restore(&state);
                }
            }
            Ok(())
        });

    builder
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app, event| {
            if let tauri::RunEvent::ExitRequested { .. } = event {
                if let Some(state) = app.try_state::<SharedEngine>() {
                    state.lock().shutdown();
                }
            }
        });
}