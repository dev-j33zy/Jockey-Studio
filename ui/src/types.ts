export type MediaKind = "audio" | "video";
export type PlaybackStatus = "stopped" | "loading" | "playing" | "paused" | "ended" | "error";
export type LoopMode = "off" | "endless" | "x2" | "x3" | "x4" | "x5";

/** Deck outputs that list `id === DEFAULT_DEVICE_ID` follow the app-level
 *  default output device configured in Settings instead of pinning their own. */
export const DEFAULT_DEVICE_ID = "default";

export interface MediaItem {
  id: string;
  path: string;
  title: string;
  container?: string | null;
  codec?: string | null;
  durationSecs: number;
  channels: number;
  sampleRate: number;
  kind: MediaKind;
  peakDb?: number | null;
  rmsDb?: number | null;
}

export interface FadeConfig {
  fadeIn: number;
  fadeOut: number;
  autoMix: boolean;
}

export interface TileState {
  id: string;
  media?: string | null;
  title: string;
  deviceId: string;
  status: PlaybackStatus;
  positionSecs: number;
  durationSecs: number;
  volume: number;
  muted: boolean;
  loopMode: LoopMode;
  fades: FadeConfig;
  error?: string | null;
}

export interface OutputDevice {
  id: string;
  name: string;
  isDefault: boolean;
  isActive: boolean;
  channels: number;
  sampleRate: number;
}

export interface EngineSettings {
  autoMixEnabled: boolean;
  autoMixDb: number;
  autoMixGateDb: number;
  autoMixAttackMs: number;
  autoMixReleaseMs: number;
  autoMixHoldMs: number;
  autoMixDuck: number;
  defaultFadeIn: number;
  defaultFadeOut: number;
  defaultDeviceId: string;
}

export interface EngineSnapshot {
  tiles: TileState[];
  media: MediaItem[];
  devices: OutputDevice[];
  settings: EngineSettings;
}

export interface EngineEvent {
  kind: string;
  action: string;
  tileId?: string | null;
  payload: Record<string, unknown>;
}

export interface UpdateInfo {
  latestVersion: string;
  currentVersion: string;
  releaseNotes: string;
  publishedAt: string;
  assetName: string;
  downloadUrl: string;
}

export interface PersistedAppState {
  tileOrder: string[];
  tiles: Array<{
    id: string;
    mediaId?: string | null;
    deviceId: string;
    volume: number;
    muted: boolean;
    loopMode: LoopMode;
    fades: FadeConfig;
  }>;
  media: Array<{
    id: string;
    path: string;
    title: string;
    container?: string | null;
    codec?: string | null;
    durationSecs: number;
    channels: number;
    sampleRate: number;
    kind: MediaKind;
    peakDb?: number | null;
    rmsDb?: number | null;
  }>;
  settings: EngineSettings;
  window: { width: number; height: number };
}