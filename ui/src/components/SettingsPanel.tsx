import { useStore } from "../state/store";
import { backend } from "../state/store";
import type { EngineSettings } from "../types";
import { DEFAULT_DEVICE_ID } from "../types";

export default function SettingsPanel() {
  const { snapshot, settingsOpen, toggleSettings, updateSettings, checkForUpdates, updateStatus } =
    useStore();
  const s: EngineSettings = snapshot.settings;

  if (!settingsOpen) return null;

  const set = (patch: Partial<EngineSettings>) => void updateSettings(patch);

  return (
    <aside className="drawer settings-panel">
      <div className="drawer-header">
        <h2>Engine Settings</h2>
        <button className="tool-btn" title="Close" onClick={() => toggleSettings(false)}>
          x
        </button>
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Auto-mix amount (dB)</span>
          <span className="setting-value">-{s.autoMixDb.toFixed(0)} dB</span>
        </label>
        <input
          type="range"
          min={0}
          max={40}
          step={1}
          value={s.autoMixDb}
          onChange={(e) => set({ autoMixDb: Number(e.target.value) })}
        />
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Auto-mix gate threshold</span>
          <span className="setting-value">{s.autoMixGateDb.toFixed(0)} dB</span>
        </label>
        <input
          type="range"
          min={-60}
          max={-20}
          step={1}
          value={s.autoMixGateDb}
          onChange={(e) => set({ autoMixGateDb: Number(e.target.value) })}
        />
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Auto-mix attack (s)</span>
          <span className="setting-value">{(s.autoMixAttackMs / 1000).toFixed(2)} s</span>
        </label>
        <input
          type="range"
          min={0.05}
          max={1.5}
          step={0.05}
          value={s.autoMixAttackMs / 1000}
          onChange={(e) => set({ autoMixAttackMs: Math.round(Number(e.target.value) * 1000) })}
        />
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Auto-mix release (s)</span>
          <span className="setting-value">{(s.autoMixReleaseMs / 1000).toFixed(2)} s</span>
        </label>
        <input
          type="range"
          min={0.1}
          max={3}
          step={0.05}
          value={s.autoMixReleaseMs / 1000}
          onChange={(e) => set({ autoMixReleaseMs: Math.round(Number(e.target.value) * 1000) })}
        />
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Auto-mix hold (s)</span>
          <span className="setting-value">{(s.autoMixHoldMs / 1000).toFixed(2)} s</span>
        </label>
        <input
          type="range"
          min={0}
          max={2}
          step={0.1}
          value={s.autoMixHoldMs / 1000}
          onChange={(e) => set({ autoMixHoldMs: Math.round(Number(e.target.value) * 1000) })}
        />
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Default device output</span>
          <span className="setting-value">
            {s.defaultDeviceId === DEFAULT_DEVICE_ID
              ? "System Default"
              : (snapshot.devices.find((d) => d.id === s.defaultDeviceId)?.name ?? s.defaultDeviceId)}
          </span>
        </label>
        <select
          className="setting-select"
          value={s.defaultDeviceId}
          onChange={(e) => void set({ defaultDeviceId: e.target.value })}
        >
          <option value={DEFAULT_DEVICE_ID}>System Default</option>
          {snapshot.devices
            .filter((d) => d.id !== DEFAULT_DEVICE_ID)
            .map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
        </select>
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Default fade-in (s)</span>
          <span className="setting-value">{s.defaultFadeIn.toFixed(2)}</span>
        </label>
        <input
          type="range"
          min={0}
          max={3}
          step={0.05}
          value={s.defaultFadeIn}
          onChange={(e) => set({ defaultFadeIn: Number(e.target.value) })}
        />
      </div>

      <div className="setting">
        <label className="setting-label">
          <span>Default fade-out (s)</span>
          <span className="setting-value">{s.defaultFadeOut.toFixed(2)}</span>
        </label>
        <input
          type="range"
          min={0}
          max={3}
          step={0.05}
          value={s.defaultFadeOut}
          onChange={(e) => set({ defaultFadeOut: Number(e.target.value) })}
        />
      </div>

      <div className="setting">
        <button className="btn" onClick={() => void backend.refreshDevices().then(() => useStore.getState().refresh())}>
          Refresh Audio Devices
        </button>
      </div>

      <div className="setting">
        <button
          className="btn"
          disabled={updateStatus === "checking"}
          onClick={() => void checkForUpdates(true)}
        >
          Check for Updates
        </button>
        {updateStatus === "checking" ? (
          <span className="setting-value">Checking…</span>
        ) : updateStatus === "uptodate" ? (
          <span className="setting-value">You're on the latest version</span>
        ) : null}
      </div>

      <p className="drawer-note">
        Auto-mix is per deck: switch it on with the automix button on any deck. While a deck's level
        is above the gate, it ducks every OTHER deck that also has auto-mix engaged; once it stays
        below the gate for the hold time, the others ramp back up. Decks without auto-mix are never
        ducked. Per-deck fades apply on that deck's next play.
      </p>
      <p className="drawer-note">
        Every deck follows the default output device above unless you pick a specific output in the
        deck's device menu. Per-deck picks persist until the deck is removed; new decks follow the
        default again.
      </p>
    </aside>
  );
}