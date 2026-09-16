import { useCallback, useEffect, useRef, useState } from "react";
import { useStore } from "./state/store";
import TopBar from "./components/TopBar";
import TileCard from "./components/TileCard";
import MediaLibrary from "./components/MediaLibrary";
import SettingsPanel from "./components/SettingsPanel";
import UpdateBubble from "./components/UpdateBubble";
import UpdateDialog from "./components/UpdateDialog";
import { droppedPaths } from "./lib/dropPaths";

interface TileRect {
  id: string;
  left: number;
  top: number;
  width: number;
  height: number;
}

interface DragState {
  id: string;
  x: number;
  y: number;
  over: number;
}

/** Drop slot index (0..rects.length) in the remaining-deck layout: the number
 *  of decks that stay before the dragged deck at the pointer's position.
 *  Rows fully above the pointer always count; within the pointer's row the
 *  slot snaps before/after the tile whose vertical band contains the pointer,
 *  using each tile's horizontal center so it never jumps to the row's first
 *  column. */
function computeOver(rects: TileRect[], x: number, y: number): number {
  let over = rects.length;
  for (let i = 0; i < rects.length; i++) {
    const r = rects[i];
    if (y >= r.top + r.height) {
      over = i + 1;
      continue;
    }
    if (y < r.top) {
      over = i;
      break;
    }
    if (x < r.left + r.width / 2) {
      over = i;
      break;
    }
    over = i + 1;
  }
  return over;
}

export default function App() {
  const init = useStore((s) => s.init);
  const snapshot = useStore((s) => s.snapshot);
  const error = useStore((s) => s.error);
  const native = useStore((s) => s.native);
  const addDecks = useStore((s) => s.addDecks);
  const clearAll = useStore((s) => s.clearAll);
  const loadDropped = useStore((s) => s.loadDropped);
  const moveTileTo = useStore((s) => s.moveTileTo);

  const [drag, setDrag] = useState<DragState | null>(null);
  const dragRects = useRef<TileRect[]>([]);
  const dragRef = useRef<DragState | null>(null);

  useEffect(() => {
    void init();
  }, [init]);

  // Close the media library / settings drawers whenever focus or a click moves
  // outside them — but never when the whole window loses focus (e.g. the user
  // switches to another application), so the drawers stay open while alt-tabbing.
  useEffect(() => {
    const isChrome = (t: EventTarget | null): boolean =>
      t instanceof HTMLElement && !!t.closest?.(".drawer, .drawer-toggle");
    const closeIfOutside = (e: Event) => {
      if (isChrome(e.target)) return;
      const s = useStore.getState();
      if (s.libraryOpen || s.settingsOpen) {
        s.toggleLibrary(false);
        s.toggleSettings(false);
      }
    };
    window.addEventListener("pointerdown", closeIfOutside, true);
    window.addEventListener("focusin", closeIfOutside, true);
    return () => {
      window.removeEventListener("pointerdown", closeIfOutside, true);
      window.removeEventListener("focusin", closeIfOutside, true);
    };
  }, []);

  // Keep the webview from navigating to a dropped file anywhere in the window,
  // and block the native right-click context menu on every surface.
  useEffect(() => {
    const prevent = (e: Event) => e.preventDefault();
    window.addEventListener("dragover", prevent);
    window.addEventListener("drop", prevent);
    window.addEventListener("contextmenu", prevent, true);
    return () => {
      window.removeEventListener("dragover", prevent);
      window.removeEventListener("drop", prevent);
      window.removeEventListener("contextmenu", prevent, true);
    };
  }, []);

  // Measure the current rects of every deck except the one being dragged.
  // Done fresh on each move because the grid reflows as the drop-slot glides.
  const recalcRects = useCallback((id: string) => {
    const els = Array.from(
      document.querySelectorAll<HTMLElement>("[data-tile-id]"),
    ).filter((el) => el.dataset.tileId !== id);
    dragRects.current = els.map((el) => {
      const r = el.getBoundingClientRect();
      return { id: el.dataset.tileId ?? "", left: r.left, top: r.top, width: r.width, height: r.height };
    });
  }, []);

  const startTileDrag = useCallback((e: React.PointerEvent<HTMLElement>, id: string) => {
    if (e.button !== 0) return;
    e.preventDefault();
    recalcRects(id);
    const over = computeOver(dragRects.current, e.clientX, e.clientY);
    const d: DragState = { id, x: e.clientX, y: e.clientY, over };
    dragRef.current = d;
    document.body.classList.add("reorder-drag");
    setDrag(d);
  }, [recalcRects]);

  // Track the pointer while a deck reorder drag is in progress and commit the
  // new position on release. The drop slot is recomputed from the final pointer
  // coordinates with fresh rects, so a fast release always lands where the
  // cursor is rather than on a stale slot from an earlier move.
  useEffect(() => {
    if (!drag) return;
    const move = (ev: PointerEvent) => {
      const d = dragRef.current;
      if (!d) return;
      recalcRects(d.id);
      const over = computeOver(dragRects.current, ev.clientX, ev.clientY);
      const next = { ...d, x: ev.clientX, y: ev.clientY, over };
      dragRef.current = next;
      setDrag(next);
    };
    const finish = (ev: PointerEvent) => {
      const d = dragRef.current;
      dragRef.current = null;
      setDrag(null);
      document.body.classList.remove("reorder-drag");
      if (!d) return;
      recalcRects(d.id);
      const over = computeOver(dragRects.current, ev.clientX, ev.clientY);
      // Dropping back on the deck's own slot cancels the reorder.
      const from = useStore.getState().snapshot.tiles.findIndex((t) => t.id === d.id);
      if (from >= 0 && over !== from) void moveTileTo(d.id, over);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", finish);
    window.addEventListener("pointercancel", finish);
    return () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", finish);
      window.removeEventListener("pointercancel", finish);
    };
  }, [drag, moveTileTo, recalcRects]);

  // Native (Tauri/WebView2): the OS intercepts file drags before the DOM, so
  // HTML5 drop events never fire. Use Tauri's drag-drop events, which deliver
  // absolute file paths; hit-test the drop position to target the deck under
  // the cursor.
  useEffect(() => {
    if (!native) return;
    let disposed = false;
    let unlisten: (() => void) | undefined;
    let hoverId: string | null = null;
    const clearHover = () => {
      if (!hoverId) return;
      document.querySelector(`[data-tile-id="${hoverId}"]`)?.classList.remove("dragover");
      hoverId = null;
    };
    const deckAt = (x: number, y: number): string | undefined => {
      const dpr = window.devicePixelRatio || 1;
      const hit = document
        .elementsFromPoint(x / dpr, y / dpr)
        .find((n) => n instanceof HTMLElement && n.closest("[data-tile-id]"));
      return hit instanceof HTMLElement
        ? hit.closest("[data-tile-id]")?.getAttribute("data-tile-id") ?? undefined
        : undefined;
    };
    (async () => {
      const { getCurrentWebviewWindow } = await import("@tauri-apps/api/webviewWindow");
      unlisten = await getCurrentWebviewWindow().onDragDropEvent((event) => {
        if (disposed) return;
        const ev = event.payload;
        if (ev.type === "enter" || ev.type === "over") {
          const next = deckAt(ev.position.x, ev.position.y) ?? null;
          if (next !== hoverId) {
            clearHover();
            if (next) {
              hoverId = next;
              document.querySelector(`[data-tile-id="${next}"]`)?.classList.add("dragover");
            }
          }
        } else if (ev.type === "drop") {
          const target = deckAt(ev.position.x, ev.position.y);
          clearHover();
          void loadDropped([], ev.paths, target);
        } else if (ev.type === "leave") {
          clearHover();
        }
      });
    })().catch(() => undefined);
    return () => {
      disposed = true;
      clearHover();
      unlisten?.();
    };
  }, [native, loadDropped]);

  const tiles = snapshot.tiles;
  const dragTile = drag ? tiles.find((t) => t.id === drag.id) ?? null : null;

  return (
    <div className="app">
      <TopBar />

      {!native ? (
        <div className="mode-banner">
          Running in browser mode — audio engine and output-device routing are simulated. Install as a
          desktop app for native multi-device playback.
        </div>
      ) : null}

      {error ? <div className="error-banner">{error}</div> : null}

      <main
        className="deck-area"
        onDragOver={(e) => {
          e.preventDefault();
          e.stopPropagation();
        }}
        onDrop={(e) => {
          e.preventDefault();
          e.stopPropagation();
          const paths = droppedPaths(e.dataTransfer);
          void loadDropped(Array.from(e.dataTransfer.files), paths);
        }}
      >
        {tiles.length === 0 ? (
          <div className="empty-state">
            <h2>No decks yet</h2>
            <p>Add a deck, then import audio from your library to start mixing.</p>
            <button className="btn primary" onClick={() => void addDecks(4)}>
              Add 4 Decks
            </button>
          </div>
        ) : drag ? (
          <div className="deck-grid">
            {tiles
              .filter((t) => t.id !== drag.id)
              .slice(0, drag.over)
              .map((tile) => (
                <TileCard key={tile.id} tile={tile} onStartDrag={startTileDrag} />
              ))}
            <div className="drop-slot" key="__drop-slot" />
            {tiles
              .filter((t) => t.id !== drag.id)
              .slice(drag.over)
              .map((tile) => (
                <TileCard key={tile.id} tile={tile} onStartDrag={startTileDrag} />
              ))}
          </div>
        ) : (
          <div className="deck-grid">
            {tiles.map((tile) => (
              <TileCard key={tile.id} tile={tile} onStartDrag={startTileDrag} />
            ))}
          </div>
        )}
      </main>

      {dragTile && drag ? (
        <div className="drag-ghost" style={{ left: drag.x, top: drag.y }}>
          <TileCard tile={dragTile} dimmed />
        </div>
      ) : null}

      <MediaLibrary />
      <SettingsPanel />
      <UpdateBubble />
      <UpdateDialog />

      {snapshot.tiles.length > 0 ? (
        <button
          className="clear-decks"
          title="Stop all playback and reset every deck to an empty state"
          onClick={() => void clearAll()}
        >
          Clear all decks
        </button>
      ) : null}
    </div>
  );
}