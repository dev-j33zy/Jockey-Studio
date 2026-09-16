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

export interface Backend {
  /** true when running inside the Tauri desktop shell */
  readonly isNative: boolean;
  importMedia(paths: string[]): Promise<MediaItem[]>;
  importDropped(files: File[], paths: string[]): Promise<MediaItem[]>;
  addTiles(tiles: TileState[]): Promise<void>;
  addEmptyTiles(count: number): Promise<TileState[]>;
  removeTile(id: string): Promise<void>;
  clearAll(): Promise<void>;
  reorderTiles(order: string[]): Promise<void>;
  loadMediaIntoTile(tileId: string, mediaId: string): Promise<TileState>;
  play(tileId: string): Promise<void>;
  pause(tileId: string): Promise<void>;
  stop(tileId: string): Promise<void>;
  seek(tileId: string, secs: number): Promise<void>;
  setVolume(tileId: string, volume: number): Promise<void>;
  setMuted(tileId: string, muted: boolean): Promise<void>;
  setLoop(tileId: string, mode: LoopMode): Promise<void>;
  setFades(tileId: string, fades: FadeConfig): Promise<void>;
  setSettings(settings: EngineSettings): Promise<void>;
  setTileDevice(tileId: string, deviceId: string): Promise<void>;
  getSnapshot(): Promise<EngineSnapshot>;
  listDevices(): Promise<OutputDevice[]>;
  refreshDevices(): Promise<OutputDevice[]>;
  saveState(state: PersistedAppState): Promise<void>;
  checkForUpdates(): Promise<UpdateInfo | null>;
  installUpdate(info: UpdateInfo): Promise<void>;
  onEvent(cb: (ev: EngineEvent) => void): () => void;
}