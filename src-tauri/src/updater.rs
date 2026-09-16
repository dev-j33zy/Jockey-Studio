use std::io::Read;
use std::time::Duration;

use serde::Deserialize;
use tauri::AppHandle;

use crate::models::UpdateInfo;

/// GitHub repository that hosts this app's releases, e.g. "user/repo".
const GITHUB_REPO: &str = "dev-j33zy/Jockey-Studio";

const UA: &str = "JockeyStudio-Updater";

#[derive(Deserialize)]
struct ReleaseAsset {
    name: String,
    browser_download_url: String,
}

#[derive(Deserialize)]
struct Release {
    tag_name: String,
    body: Option<String>,
    published_at: Option<String>,
    assets: Vec<ReleaseAsset>,
}

/// `check_for_updates` command — silently compare the latest GitHub release
/// against the running build. Returns `None` when the build is current, when
/// the repo is unset, or when the release has no NSIS installer asset.
#[tauri::command]
pub async fn check_for_updates() -> Result<Option<UpdateInfo>, String> {
    tauri::async_runtime::spawn_blocking(latest_release)
        .await
        .map_err(|e| e.to_string())?
}

fn latest_release() -> Result<Option<UpdateInfo>, String> {
    let repo = GITHUB_REPO.trim();
    if repo.is_empty() || repo == "owner/repo" {
        return Ok(None);
    }
    let current = env!("CARGO_PKG_VERSION");
    let url = format!("https://api.github.com/repos/{repo}/releases/latest");
    let body = ureq::get(&url)
        .set("User-Agent", UA)
        .set("Accept", "application/vnd.github+json")
        .timeout(Duration::from_secs(8))
        .call()
        .map_err(|e| e.to_string())?
        .into_reader()
        .take(512 * 1024);

    let mut text = String::new();
    let mut reader = body;
    reader.read_to_string(&mut text).map_err(|e| e.to_string())?;
    let rel: Release = serde_json::from_str(&text).map_err(|e| e.to_string())?;

    let asset = rel
        .assets
        .iter()
        .find(|a| a.name.to_lowercase().ends_with(".exe"))
        .ok_or_else(|| "no installer asset found".to_string())?;

    let latest = rel.tag_name.trim_start_matches('v').to_string();
    if latest.is_empty() {
        return Ok(None);
    }
    let newer = match (
        semver::Version::parse(&latest),
        semver::Version::parse(current),
    ) {
        (Ok(l), Ok(c)) => l > c,
        _ => latest != current,
    };
    if !newer {
        return Ok(None);
    }

    Ok(Some(UpdateInfo {
        latest_version: latest,
        current_version: current.to_string(),
        release_notes: rel.body.unwrap_or_default(),
        published_at: rel.published_at.unwrap_or_default(),
        asset_name: asset.name.clone(),
        download_url: asset.browser_download_url.clone(),
    }))
}

/// `install_update` command — download the installer, hand it to a detached
/// helper that (1) waits for this process to exit, (2) runs the installer in
/// silent mode, and (3) relaunches the app if the install succeeded. Then exit
/// the app so the installer can overwrite the locked executable.
#[tauri::command]
pub async fn install_update(app: AppHandle, info: UpdateInfo) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || install_release(&info))
        .await
        .map_err(|e| e.to_string())??;
    app.exit(0);
    Ok(())
}

#[cfg(windows)]
fn install_release(info: &UpdateInfo) -> Result<(), String> {
    use std::os::windows::process::CommandExt;

    let dir = std::env::temp_dir().join("jockey-studio-update");
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let installer = dir.join("jockey-studio-setup.exe");

    let agent = ureq::AgentBuilder::new()
        .timeout_connect(Duration::from_secs(20))
        .timeout_read(Duration::from_secs(600))
        .build();
    let resp = agent
        .get(&info.download_url)
        .set("User-Agent", UA)
        .call()
        .map_err(|e| e.to_string())?;
    let mut file = std::fs::File::create(&installer).map_err(|e| e.to_string())?;
    std::io::copy(&mut resp.into_reader(), &mut file).map_err(|e| e.to_string())?;
    drop(file);

    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let script = dir.join("run-update.cmd");
    let script_text = format!(
        "@echo off\r\nping -n 4 127.0.0.1 >nul\r\n\"{}\" /S\r\nif errorlevel 1 exit /b 1\r\nstart \"\" \"{}\"\r\n",
        installer.display(),
        exe.display()
    );
    std::fs::write(&script, script_text).map_err(|e| e.to_string())?;

    std::process::Command::new("cmd")
        .arg("/c")
        .arg(&script)
        .creation_flags(0x08000000) // CREATE_NO_WINDOW
        .spawn()
        .map_err(|e| e.to_string())?;
    Ok(())
}

#[cfg(not(windows))]
fn install_release(_info: &UpdateInfo) -> Result<(), String> {
    Ok(())
}