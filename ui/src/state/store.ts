import { create } from "zustand";
import type {
  EngineEvent,
  EngineSettings,
  EngineSnapshot,
  FadeConfig,
  LoopMode,
  PersistedAppState,
  TileState,
  UpdateInfo,
} from "../types";
import { DEFAULT_DEVICE_ID } from "../types";
import type { Backend } from "../lib/backend";
import { createBackend } from "../lib/util";

const POLL_MS = 250;
const DISMISSED_KEY = "jockey.dismissed-update";

export const backend: Backend = createBackend();

interface StoreState {
  native: boolean;
  booted: boolean;
  error: string | null;
  snapshot: EngineSnapshot;
  selectedTileId: string | null;
  libraryOpen: boolean;
  settingsOpen: boolean;
  updateInfo: UpdateInfo | null;
  updateDialogOpen: boolean;
  updateInProgress: boolean;
  updateStatus: "idle" | "checking" | "uptodate";
  init: () => Promise<void>;
  refresh: () => Promise<void>;
  selectTile: (id: string | null) => void;
  toggleLibrary: (open?: boolean) => void;
  toggleSettings: (open?: boolean) => void;
  checkForUpdates: (manual?: boolean) => Promise<void>;
  dismissUpdate: () => void;
  openUpdateDialog: () => void;
  closeUpdateDialog: () => void;
  installUpdate: () => Promise<void>;
  loadDropped: (files: File[], paths: string[], targetTileId?: string) => Promise<void>;
  addDecks: (count?: number) => Promise<void>;
  removeTile: (id: string) => Promise<void>;
  clearAll: () => Promise<void>;
  moveTileTo: (id: string, index: number) => Promise<void>;
  loadInto: (mediaId: string) => Promise<void>;
  play: (id: string) => Promise<void>;
  pause: (id: string) => Promise<void>;
  stop: (id: string) => Promise<void>;
  seek: (id: string, secs: number) => Promise<void>;
  setVolume: (id: string, volume: number) => Promise<void>;
  setMuted: (id: string, muted: boolean) => Promise<void>;
  setLoop: (id: string, mode: LoopMode) => Promise<void>;
  setFades: (id: string, fades: FadeConfig) => Promise<void>;
  updateSettings: (patch: Partial<EngineSettings>) => Promise<void>;
  setTileDevice: (id: string, deviceId: string) => Promise<void>;
  persist: () => Promise<void>;
}

const EMPTY_SNAPSHOT: EngineSnapshot = {
  tiles: [],
  media: [],
  devices: [],
  settings: {
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
  },
};

let polling = false;
let debounceHandle: number | undefined;
let loadChain: Promise<void> = Promise.resolve();

function runErr(
  fn: () => Promise<void> | void,
  onError: (msg: string) => void,
): Promise<void> {
  return Promise.resolve()
    .then(fn)
    .catch((e) => {
      onError(String(e?.message ?? e));
    });
}

function buildPersistState(s: EngineSnapshot): PersistedAppState {
  return {
    tileOrder: s.tiles.map((t) => t.id),
    tiles: s.tiles.map((t) => ({
      id: t.id,
      mediaId: t.media ?? null,
      deviceId: t.deviceId,
      volume: t.volume,
      muted: t.muted,
      loopMode: t.loopMode,
      fades: t.fades,
    })),
    media: s.media.map((m) => ({
      id: m.id,
      path: m.path,
      title: m.title,
      container: m.container ?? null,
      codec: m.codec ?? null,
      durationSecs: m.durationSecs,
      channels: m.channels,
      sampleRate: m.sampleRate,
      kind: m.kind,
      peakDb: m.peakDb ?? null,
      rmsDb: m.rmsDb ?? null,
    })),
    settings: s.settings,
    window: { width: window.innerWidth, height: window.innerHeight },
  };
}

export const useStore = create<StoreState>((set, get) => ({
  native: backend.isNative,
  booted: false,
  error: null,
  snapshot: EMPTY_SNAPSHOT,
  selectedTileId: null,
  libraryOpen: false,
  settingsOpen: false,
  updateInfo: null,
  updateDialogOpen: false,
  updateInProgress: false,
  updateStatus: "idle",

  async init() {
    if (get().booted) return;
    set({ booted: true, error: null });

    await runErr(async () => {
      const snap = await backend.getSnapshot();
      set({ snapshot: snap });
      if (snap.tiles.length === 0) {
        await backend.addEmptyTiles(4);
      }
      await backend.listDevices();
      await get().refresh();
    }, (msg) => set({ error: msg }));

    backend.onEvent((ev: EngineEvent) => {
      if (ev.kind === "tile" || ev.kind === "media" || ev.kind === "engine") {
        flushRefresh();
      }
    });

    if (!polling) {
      polling = true;
      window.setInterval(() => {
        void get().refresh();
      }, POLL_MS);
    }

    // Silent background update check — the app must never block its own
    // startup or produce any indication that it is scanning.
    void get().checkForUpdates();
  },

  async refresh() {
    await runErr(async () => {
      const snap = await backend.getSnapshot();
      set({ snapshot: snap });
    }, (msg) => set({ error: msg }));
  },

  selectTile(id) {
    set({ selectedTileId: id });
  },

  toggleLibrary(open) {
    set({ libraryOpen: typeof open === "boolean" ? open : !get().libraryOpen });
  },

  toggleSettings(open) {
    set({ settingsOpen: typeof open === "boolean" ? open : !get().settingsOpen });
  },

  async checkForUpdates(manual = false) {
    set({ updateStatus: "checking" });
    try {
      const info = await backend.checkForUpdates();
      if (!info) {
        set({ updateStatus: "uptodate" });
        return;
      }
      const dismissed = localStorage.getItem(DISMISSED_KEY);
      const showDialog = manual || info.latestVersion !== dismissed;
      set({ updateInfo: info, updateStatus: "idle", updateDialogOpen: showDialog });
    } catch {
      set({ updateStatus: "idle" });
    }
  },

  dismissUpdate() {
    const info = get().updateInfo;
    if (info) localStorage.setItem(DISMISSED_KEY, info.latestVersion);
    set({ updateInfo: null, updateDialogOpen: false, updateStatus: "idle" });
  },

  openUpdateDialog() {
    set({ updateDialogOpen: true });
  },

  closeUpdateDialog() {
    set({ updateDialogOpen: false });
  },

  async installUpdate() {
    const info = get().updateInfo;
    if (!info || get().updateInProgress) return;
    set({ updateInProgress: true });
    try {
      await backend.installUpdate(info);
    } catch (e) {
      set({ error: String((e as Error)?.message ?? e), updateInProgress: false });
    }
  },

  async loadDropped(files, paths, targetTileId) {
    await runErr(async () => {
      const items = await backend.importDropped(files, paths ?? []);
      if (items.length === 0) {
        if (files.length > 0) set({ error: "Could not import dropped file." });
        return;
      }
      let i = 0;
      if (targetTileId) {
        await backend.loadMediaIntoTile(targetTileId, items[0].id);
        i = 1;
      }
      for (; i < items.length; i++) {
        await get().loadInto(items[i].id);
      }
      await get().refresh();
      await get().persist();
    }, (msg) => set({ error: msg }));
  },

  async addDecks(count = 1) {
    await runErr(async () => {
      await backend.addEmptyTiles(count);
      await get().refresh();
      await get().persist();
    }, (msg) => set({ error: msg }));
  },

  async removeTile(id) {
    await runErr(async () => {
      await backend.removeTile(id);
      if (get().selectedTileId === id) set({ selectedTileId: null });
      await get().refresh();
      await get().persist();
    }, (msg) => set({ error: msg }));
  },

  async clearAll() {
    await runErr(async () => {
      await backend.clearAll();
      set({ selectedTileId: null });
      await get().refresh();
      await get().persist();
    }, (msg) => set({ error: msg }));
  },

  async moveTileTo(id, index) {
    const order = get().snapshot.tiles.map((t) => t.id);
    const from = order.indexOf(id);
    if (from < 0) return;
    if (index === from) return;
    order.splice(from, 1);
    order.splice(Math.max(0, Math.min(index, order.length)), 0, id);
    await backend.reorderTiles(order);
    await get().refresh();
    await get().persist();
  },

  loadInto(mediaId) {
    // Serialize loads so each click evaluates the deck list freshly: rapid
    // clicks must never select the same empty deck or spawn duplicate decks.
    loadChain = loadChain
      .then(async () => {
        await runErr(async () => {
          const nextEmpty = get().snapshot.tiles.find((t) => !t.media);
          let target = nextEmpty?.id ?? null;
          if (!target) {
            const created = await backend.addEmptyTiles(1);
            await get().refresh();
            target = created[0]?.id ?? null;
          }
          if (!target) return;
          await backend.loadMediaIntoTile(target, mediaId);
          await get().refresh();
          await get().persist();
        }, (msg) => set({ error: msg }));
      })
      .catch(() => undefined);
    return loadChain;
  },

  play(id) {
    return runErr(async () => {
      await backend.play(id);
    }, (msg) => set({ error: msg }));
  },

  pause(id) {
    return runErr(async () => {
      await backend.pause(id);
    }, (msg) => set({ error: msg }));
  },

  stop(id) {
    return runErr(async () => {
      await backend.stop(id);
    }, (msg) => set({ error: msg }));
  },

  seek(id, secs) {
    return runErr(async () => {
      await backend.seek(id, secs);
    }, (msg) => set({ error: msg }));
  },

  setVolume(id, volume) {
    return runErr(async () => {
      await backend.setVolume(id, volume);
    }, (msg) => set({ error: msg }));
  },

  setMuted(id, muted) {
    return runErr(async () => {
      await backend.setMuted(id, muted);
    }, (msg) => set({ error: msg }));
  },

  setLoop(id, mode) {
    return runErr(async () => {
      await backend.setLoop(id, mode);
    }, (msg) => set({ error: msg }));
  },

  setFades(id, fades) {
    return runErr(async () => {
      await backend.setFades(id, fades);
    }, (msg) => set({ error: msg }));
  },

  async updateSettings(patch) {
    await runErr(async () => {
      const merged: EngineSettings = { ...get().snapshot.settings, ...patch };
      await backend.setSettings(merged);
      set((s) => ({ snapshot: { ...s.snapshot, settings: merged } }));
      await get().persist();
    }, (msg) => set({ error: msg }));
  },

  async setTileDevice(id, deviceId) {
    await runErr(async () => {
      await backend.setTileDevice(id, deviceId);
      await get().refresh();
    }, (msg) => set({ error: msg }));
  },

  async persist() {
    await runErr(async () => {
      await backend.saveState(buildPersistState(get().snapshot));
    }, () => undefined);
  },
}));

export function selectTile(snapshot: EngineSnapshot, id: string): TileState | undefined {
  return snapshot.tiles.find((t) => t.id === id);
}

function flushRefresh() {
  if (debounceHandle) window.clearTimeout(debounceHandle);
  debounceHandle = window.setTimeout(() => {
    void useStore.getState().refresh();
  }, 40);
}