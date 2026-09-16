import { useStore } from "../state/store";
import { formatDuration } from "../lib/util";

export default function MediaLibrary() {
  const { snapshot, libraryOpen, toggleLibrary, loadInto, selectedTileId } = useStore();

  if (!libraryOpen) return null;

  return (
    <aside className="drawer media-library">
      <div className="drawer-header">
        <h2>Media Library</h2>
        <button className="tool-btn" title="Close" onClick={() => toggleLibrary(false)}>
          x
        </button>
      </div>
      <p className="drawer-hint">
        Click “Load” to place audio into
        {selectedTileId ? " the selected deck" : " the next open deck"}.
      </p>
      <div className="media-list">
        {snapshot.media.length === 0 ? (
          <div className="media-empty">
            <p>No media yet. Drag music from File Explorer onto any deck to add it.</p>
          </div>
        ) : (
          snapshot.media.map((m) => (
            <div className="media-row" key={m.id}>
              <div className="media-meta">
                <span className="media-title" title={m.title}>
                  {m.title}
                </span>
                <span className="media-sub">
                  {m.kind === "video" ? "Video" : "Audio"}
                  {m.container ? ` · ${m.container.toUpperCase()}` : ""} · {m.channels}ch ·{" "}
                  {Math.round(m.sampleRate / 1000)}kHz · {formatDuration(m.durationSecs)}
                  {m.peakDb != null ? ` · Peak ${m.peakDb.toFixed(1)} dB` : ""}
                </span>
              </div>
              <button className="btn small" onClick={() => void loadInto(selectedTileId, m.id)}>
                Load
              </button>
            </div>
          ))
        )}
      </div>
    </aside>
  );
}