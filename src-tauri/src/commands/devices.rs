use rodio::cpal::{self, traits::{DeviceTrait, HostTrait}};

use tauri::Manager;

use crate::audio::engine::SharedEngine;
use crate::models::{DEFAULT_DEVICE_ID, OutputDevice};

fn enumerate() -> Vec<OutputDevice> {
    let host = cpal::default_host();
    let mut list = Vec::new();

    let default_name = host
        .default_output_device()
        .and_then(|d| d.name().ok());

    if let Ok(devices) = host.output_devices() {
        for dev in devices {
            let name = dev.name().unwrap_or_else(|_| "Unknown device".to_string());
            let is_default = default_name.as_deref() == Some(name.as_str());
            let (channels, sample_rate) = dev
                .default_output_config()
                .map(|c| (c.channels(), c.sample_rate().0))
                .unwrap_or((2, 44100));
            list.push(OutputDevice {
                id: name.clone(),
                name,
                is_default,
                is_active: true,
                channels,
                sample_rate,
            });
        }
    }

    // Guarantee a "default" entry so tiles always have at least one target.
    if !list.iter().any(|d| d.id == DEFAULT_DEVICE_ID) {
        list.insert(
            0,
            OutputDevice {
                id: DEFAULT_DEVICE_ID.to_string(),
                name: "System Default".to_string(),
                is_default: default_name.is_none(),
                is_active: true,
                channels: 2,
                sample_rate: 44100,
            },
        );
    }
    list
}

#[tauri::command(rename_all = "camelCase")]
pub fn list_devices(app: tauri::AppHandle) -> Result<Vec<OutputDevice>, String> {
    let devices = enumerate();
    app.state::<SharedEngine>().lock().set_devices(devices.clone());
    Ok(devices)
}

#[tauri::command(rename_all = "camelCase")]
pub fn refresh_devices(app: tauri::AppHandle) -> Result<Vec<OutputDevice>, String> {
    list_devices(app)
}