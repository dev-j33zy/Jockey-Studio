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
import { DEFAULT_DEVICE_ID } from "../types";

const DEFAULT_SETTINGS: EngineSettings = {
  autoMixEnabled: true,
  autoMixDb: 10,
  autoMixGateDb: -42,
  autoMixAttackMs: 250,
  autoMixReleaseMs: 800,
  autoMixHoldMs: 600,
  autoMixDuck: 0.35,
  defaultFadeIn: 0.05,
  defaultFadeOut: 0.2,
  defaultDeviceId: DEFAULT_DEVICE_ID,
};

const DEFAULT_DEVICES: OutputDevice[] = [
  {
    id: "default",
    name: "Browser Output",
    isDefault: true,
    isActive: true,
    channels: 2,
    sampleRate: 0,
  },
];

interface BMedia {
  item: MediaItem;
  file: File;
  url: string;
}

interface BTile {
  state: TileState;
  el: HTMLAudioElement | null;
  startedAtUrl: string;
}

function newTileId(): string {
  return crypto.randomUUID?.() ?? `tile-${Math.random().toString(36).slice(2)}`;
}

function newMediaId(): string {
  return crypto.randomUUID?.() ?? `media-${Math.random().toString(36).slice(2)}`;
}

function titleOf(name: string): string {
  return name.replace(/\.[^.]+$/, "");
}

function probeDuration(url: string): Promise<number> {
  return new Promise((resolve) => {
    const a = new Audio();
    a.preload = "metadata";
    a.src = url;
    const done = () => {
      a.removeEventListener("loadedmetadata", done);
      a.removeEventListener("error", done);
      resolve(Number.isFinite(a.duration) ? a.duration : 0);
    };
    a.addEventListener("loadedmetadata", done, { once: true });
    a.addEventListener("error", done, { once: true });
  });
}

export class BrowserBackend implements Backend {
  readonly isNative = false;

  private medias = new Map<string, BMedia>();
  private tiles = new Map<string, BTile>();
  private tileOrder: string[] = [];
  private settings: EngineSettings = { ...DEFAULT_SETTINGS };

  private rememberFile(file: File): BMedia {
    const url = URL.createObjectURL(file);
    const existing = [...this.medias.values()].find((m) => m.item.title === titleOf(file.name));
    if (existing) return existing;
    const media: BMedia = {
      url,
      file,
      item: {
        id: newMediaId(),
        path: url,
        title: titleOf(file.name),
        durationSecs: 0,
        channels: 2,
        sampleRate: 48000,
        kind: "audio",
      },
    };
    this.medias.set(media.item.id, media);
    probeDuration(url).then((d) => {
      media.item.durationSecs = d;
    });
    return media;
  }

  async importMedia(paths: string[]): Promise<MediaItem[]> {
    return paths
      .map((path) => {
        const m = [...this.medias.values()].find((x) => x.item.path === path);
        return m?.item;
      })
      .filter((x): x is MediaItem => !!x);
  }

  async importDropped(files: File[]): Promise<MediaItem[]> {
    return files.map((f) => this.rememberFile(f)).map((m) => m.item);
  }

  async addTiles(tiles: TileState[]): Promise<void> {
    for (const st of tiles) {
      if (!this.tiles.has(st.id)) {
        this.tiles.set(st.id, this.makeTile(st));
        this.tileOrder.push(st.id);
      }
    }
  }

  async addEmptyTiles(count: number): Promise<TileState[]> {
    const created: TileState[] = [];
    for (let i = 0; i < count; i++) {
      const st: TileState = {
        id: newTileId(),
        title: "",
        deviceId: "default",
        status: "stopped",
        positionSecs: 0,
        durationSecs: 0,
        volume: 0.9,
        muted: false,
        loopMode: "off",
        fades: { fadeIn: this.settings.defaultFadeIn, fadeOut: this.settings.defaultFadeOut, autoMix: true },
      };
      this.tiles.set(st.id, this.makeTile(st));
      this.tileOrder.push(st.id);
      created.push(st);
    }
    return created;
  }

  private makeTile(st: TileState): BTile {
    return { state: { ...st }, el: null, startedAtUrl: "" };
  }

  async removeTile(id: string): Promise<void> {
    const t = this.tiles.get(id);
    t?.el?.pause();
    if (t?.state.media) {
      const m = this.medias.get(t.state.media);
      m && URL.revokeObjectURL(m.url);
    }
    this.tiles.delete(id);
    this.tileOrder = this.tileOrder.filter((x) => x !== id);
  }

  async reorderTiles(order: string[]): Promise<void> {
    const known = order.filter((id) => this.tiles.has(id));
    const rest = this.tileOrder.filter((id) => !known.includes(id));
    this.tileOrder = [...known, ...rest];
  }

  private resolveMedia(mediaId: string): BMedia | undefined {
    return this.medias.get(mediaId);
  }

  private ensureEl(tile: BTile): HTMLAudioElement {
    if (tile.el) return tile.el;
    const el = new Audio();
    el.preload = "auto";
    tile.el = el;
    el.addEventListener("ended", () => {
      if (tile.state.loopMode !== "off") {
        el.currentTime = 0;
        void el.play();
      } else {
        tile.state.status = "ended";
        tile.state.positionSecs = tile.state.durationSecs;
      }
    });
    return el;
  }

  async loadMediaIntoTile(tileId: string, mediaId: string): Promise<TileState> {
    const tile = this.tiles.get(tileId);
    const media = this.resolveMedia(mediaId);
    if (!tile || !media) throw new Error("tile or media not found");

    const el = this.ensureEl(tile);
    el.pause();
    el.src = media.url;
    tile.state.media = media.item.id;
    tile.state.title = media.item.title;
    tile.state.durationSecs = media.item.durationSecs;
    tile.state.positionSecs = 0;
    tile.state.status = "stopped";
    tile.startedAtUrl = media.url;
    return { ...tile.state };
  }

  private elementFor(tileId: string): HTMLAudioElement | null {
    const tile = this.tiles.get(tileId);
    if (!tile) return null;
    if (!tile.state.media) return null;
    return this.ensureEl(tile);
  }

  async play(tileId: string): Promise<void> {
    const tile = this.tiles.get(tileId);
    const el = this.elementFor(tileId);
    if (!tile || !el) return;
    if (tile.state.status === "paused") {
      void el.play();
      tile.state.status = "playing";
    } else if (tile.state.status === "ended") {
      el.currentTime = 0;
      void el.play();
      tile.state.status = "playing";
    } else if (tile.state.status === "stopped") {
      void el.play();
      tile.state.status = "playing";
    }
  }

  async pause(tileId: string): Promise<void> {
    const tile = this.tiles.get(tileId);
    const el = this.elementFor(tileId);
    if (!tile || !el) return;
    el.pause();
    tile.state.status = "paused";
  }

  async stop(tileId: string): Promise<void> {
    const tile = this.tiles.get(tileId);
    const el = this.elementFor(tileId);
    if (!tile) return;
    if (el) {
      el.pause();
      el.currentTime = 0;
    }
    tile.state.status = "stopped";
    tile.state.positionSecs = 0;
  }

  async seek(tileId: string, secs: number): Promise<void> {
    const tile = this.tiles.get(tileId);
    const el = this.elementFor(tileId);
    if (!tile || !el) return;
    const max = tile.state.durationSecs || Number.MAX_VALUE;
    el.currentTime = Math.min(Math.max(0, secs), max);
    tile.state.positionSecs = el.currentTime;
  }

  async setVolume(tileId: string, volume: number): Promise<void> {
    const tile = this.tiles.get(tileId);
    if (!tile) return;
    tile.state.volume = Math.min(1, Math.max(0, volume));
    if (tile.el) tile.el.volume = tile.state.muted ? 0 : tile.state.volume;
  }

  async setMuted(tileId: string, muted: boolean): Promise<void> {
    const tile = this.tiles.get(tileId);
    if (!tile) return;
    tile.state.muted = muted;
    if (tile.el) tile.el.volume = muted ? 0 : tile.state.volume;
  }

  async setLoop(tileId: string, mode: LoopMode): Promise<void> {
    const tile = this.tiles.get(tileId);
    if (!tile) return;
    tile.state.loopMode = mode;
    if (tile.el) tile.el.loop = mode !== "off";
  }

  async setFades(tileId: string, fades: FadeConfig): Promise<void> {
    const tile = this.tiles.get(tileId);
    if (tile) tile.state.fades = { ...fades };
  }

  async setSettings(settings: EngineSettings): Promise<void> {
    this.settings = { ...settings };
  }

  async setTileDevice(tileId: string, deviceId: string): Promise<void> {
    const tile = this.tiles.get(tileId);
    if (tile) tile.state.deviceId = deviceId;
  }

  async getSnapshot(): Promise<EngineSnapshot> {
    const tiles: TileState[] = this.tileOrder
      .map((id) => this.tiles.get(id))
      .filter((t): t is BTile => !!t)
      .map((t) => {
        if (t.el && t.state.status === "playing") {
          t.state.positionSecs = t.el.currentTime;
          if (Number.isFinite(t.el.duration) && t.el.duration > 0) {
            t.state.durationSecs = t.el.duration;
          }
        }
        return { ...t.state };
      });
    const media: MediaItem[] = [...this.medias.values()].map((m) => ({ ...m.item }));
    return {
      tiles,
      media,
      devices: DEFAULT_DEVICES,
      settings: { ...this.settings },
    };
  }

  listDevices() {
    return Promise.resolve(DEFAULT_DEVICES);
  }

  refreshDevices() {
    return Promise.resolve(DEFAULT_DEVICES);
  }

  async saveState(_state: PersistedAppState): Promise<void> {
    // Layout persistence in browser mode is intentionally skipped.
  }

  checkForUpdates(): Promise<UpdateInfo | null> {
    return Promise.resolve(null);
  }

  installUpdate(_info: UpdateInfo): Promise<void> {
    return Promise.resolve();
  }

  onEvent(_cb: (ev: EngineEvent) => void): () => void {
    return () => {};
  }
}