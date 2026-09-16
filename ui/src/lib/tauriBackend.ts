import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import type {
  EngineEvent,
  EngineSettings,
  EngineSnapshot,
  FadeConfig,
  LoopMode,
  MediaItem,
  OutputDevice,
  PersistedAppState,
  TileState,
  UpdateInfo,
} from "../types";
import type { Backend } from "./backend";

export function isTauriRuntime(): boolean {
  return typeof window !== "undefined" && !!window.__TAURI_INTERNALS__;
}

class TauriBackend implements Backend {
  readonly isNative = true;

  async importMedia(paths: string[]): Promise<MediaItem[]> {
    const items: MediaItem[] = [];
    for (const path of paths) {
      items.push(await invoke<MediaItem>("import_media", { path }));
    }
    return items;
  }

  async importDropped(files: File[], paths: string[]): Promise<MediaItem[]> {
    const resolved = paths.length
      ? paths
      : files.map((f) => (f as File & { path?: string }).path ?? "").filter(Boolean);
    return this.importMedia(resolved);
  }

  async addTiles(tiles: TileState[]): Promise<void> {
    await invoke("add_tiles", { tiles });
  }

  async addEmptyTiles(count: number): Promise<TileState[]> {
    return invoke<TileState[]>("add_empty_tiles", { count });
  }

  async removeTile(id: string): Promise<void> {
    await invoke("remove_tile", { tileId: id });
  }

  async clearAll(): Promise<void> {
    await invoke("reset_all");
  }

  async reorderTiles(order: string[]): Promise<void> {
    await invoke("reorder_tiles", { order });
  }

  async loadMediaIntoTile(tileId: string, mediaId: string): Promise<TileState> {
    return invoke<TileState>("load_media_into_tile", { tileId, mediaId });
  }

  play(tileId: string) {
    return invoke<void>("play", { tileId });
  }

  pause(tileId: string) {
    return invoke<void>("pause", { tileId });
  }

  stop(tileId: string) {
    return invoke<void>("stop", { tileId });
  }

  seek(tileId: string, secs: number) {
    return invoke<void>("seek", { tileId, secs });
  }

  setVolume(tileId: string, volume: number) {
    return invoke<void>("set_volume", { tileId, volume });
  }

  setMuted(tileId: string, muted: boolean) {
    return invoke<void>("set_muted", { tileId, muted });
  }

  setLoop(tileId: string, mode: LoopMode) {
    return invoke<void>("set_loop", { tileId, mode });
  }

  setFades(tileId: string, fades: FadeConfig) {
    return invoke<void>("set_fades", { tileId, fades });
  }

  setSettings(settings: EngineSettings) {
    return invoke<void>("set_settings", { settings });
  }

  setTileDevice(tileId: string, deviceId: string) {
    return invoke<void>("set_tile_device", { tileId, deviceId });
  }

  async getSnapshot(): Promise<EngineSnapshot> {
    return invoke<EngineSnapshot>("get_snapshot");
  }

  listDevices() {
    return invoke<OutputDevice[]>("list_devices");
  }

  async refreshDevices(): Promise<OutputDevice[]> {
    return invoke<OutputDevice[]>("refresh_devices");
  }

  saveState(state: PersistedAppState) {
    return invoke<void>("save_app_state", { state });
  }

  checkForUpdates() {
    return invoke<UpdateInfo | null>("check_for_updates");
  }

  installUpdate(info: UpdateInfo) {
    return invoke<void>("install_update", { info });
  }

  onEvent(cb: (ev: EngineEvent) => void): () => void {
    let unlisten: UnlistenFn | undefined;
    let disposed = false;
    listen<EngineEvent>("engine://event", (event) => {
      if (!disposed) cb(event.payload);
    }).then((fn) => {
      if (disposed) fn();
      else unlisten = fn;
    });
    return () => {
      disposed = true;
      unlisten?.();
    };
  }
}

export { TauriBackend };