import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { TileState } from "../types";
import { DEFAULT_DEVICE_ID } from "../types";
import { useStore } from "../state/store";
import { formatDuration, formatTime } from "../lib/util";
import { droppedPaths } from "../lib/dropPaths";
import {
  IconCheck,
  IconOutput,
  IconPause,
  IconPlay,
  IconRepeat,
  IconShuffle,
  IconVolume,
  IconVolumeMute,
} from "./icons";

const LOOP_TITLES: Record<TileState["loopMode"], string> = {
  off: "Loop: off — plays once",
  endless: "Loop: endlessly until stopped",
  x2: "Loop: plays the track 2 times",
  x3: "Loop: plays the track 3 times",
  x4: "Loop: plays the track 4 times",
  x5: "Loop: plays the track 5 times",
};

const LOOP_OPTIONS: { value: TileState["loopMode"]; label: string }[] = [
  { value: "off", label: "Off" },
  { value: "endless", label: "Loop endlessly" },
  { value: "x2", label: "2×" },
  { value: "x3", label: "3×" },
  { value: "x4", label: "4×" },
  { value: "x5", label: "5×" },
];

function LoopBadgeIcon({
  value,
  size = 18,
}: {
  value: TileState["loopMode"];
  size?: number;
}) {
  const digit =
    value === "x2" ? 2 : value === "x3" ? 3 : value === "x4" ? 4 : value === "x5" ? 5 : null;
  return (
    <span className="loop-icon">
      <IconRepeat size={size} />
      {value === "endless" ? <span className="loop-icon-badge">∞</span> : null}
      {digit != null ? <span className="loop-icon-badge">{digit}</span> : null}
    </span>
  );
}

interface TileCardProps {
  tile: TileState;
  onStartDrag?: (e: React.PointerEvent<HTMLElement>, id: string) => void;
  dimmed?: boolean;
}

export default function TileCard({ tile, onStartDrag, dimmed }: TileCardProps) {
  const {
    play,
    pause,
    seek,
    setVolume,
    setMuted,
    setLoop,
    setFades,
    setTileDevice,
    removeTile,
    snapshot,
    loadDropped,
  } = useStore();

  const [dragging, setDragging] = useState<number | null>(null);
  const [openPop, setOpenPop] = useState<"vol" | "dev" | "loop" | null>(null);
  const [anchor, setAnchor] = useState<{
    left: number;
    right: number;
    top: number;
    bottom: number;
    width: number;
  } | null>(null);
  const [popPos, setPopPos] = useState<{ top: number; left: number } | null>(null);
  const popEl = useRef<HTMLDivElement | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const [hoverTip, setHoverTip] = useState<{ left: number; secs: number } | null>(null);
  const seekRef = useRef<HTMLInputElement | null>(null);
  const seekingRef = useRef(false);
  const lastSeekAt = useRef(0);
  const showPos = dragging ?? tile.positionSecs;

  const isPlaying = tile.status === "playing";
  const hasMedia = !!tile.media;
  const popupOpen = openPop !== null;
  const media = snapshot.media.find((m) => m.id === tile.media) ?? null;

  const closePopups = () => {
    setOpenPop(null);
    setAnchor(null);
    setPopPos(null);
  };

  // Close on outside scroll/resize, but never because of scrolling inside the
  // popup's own device list.
  useEffect(() => {
    if (!openPop) return;
    const close = (e: Event) => {
      if (popEl.current?.contains(e.target as Node)) return;
      closePopups();
    };
    window.addEventListener("scroll", close, true);
    window.addEventListener("resize", close);
    return () => {
      window.removeEventListener("scroll", close, true);
      window.removeEventListener("resize", close);
    };
  }, [openPop]);

  // When a deck loses its media (e.g. clear all decks) drop any local seek
  // preview so the timestamp/seeker snap back to the start of the track.
  useEffect(() => {
    if (!hasMedia) {
      setDragging(null);
      setHoverTip(null);
      seekingRef.current = false;
    }
  }, [hasMedia]);

  const setAnchorFrom = (el: HTMLElement) => {
    const r = el.getBoundingClientRect();
    setAnchor({ left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width });
  };

  const toggleVol = (e: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorFrom(e.currentTarget);
    setOpenPop((v) => (v === "vol" ? null : "vol"));
  };

  const toggleDev = (e: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorFrom(e.currentTarget);
    setOpenPop((v) => (v === "dev" ? null : "dev"));
  };

  const toggleLoop = (e: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorFrom(e.currentTarget);
    setOpenPop((v) => (v === "loop" ? null : "loop"));
  };

  // Reposition the popup after mount so it is always fully inside the viewport
  // and aligned with its trigger button (measuring lets us clamp exactly
  // instead of guessing the popup's height).
  useLayoutEffect(() => {
    const el = popEl.current;
    if (!el || !anchor) return;
    const vh = window.innerHeight;
    const vw = window.innerWidth;
    const gap = 8;
    const w = el.offsetWidth;
    const h = el.offsetHeight;
    const wantUp =
      openPop === "vol" ? true : anchor.top - gap >= vh - anchor.bottom - gap;
    let top: number;
    if (wantUp) {
      top = anchor.top - gap - h;
      if (top < 8) top = Math.min(anchor.bottom + gap, vh - 8 - h);
      if (top < 8) top = 8;
    } else {
      top = anchor.bottom + gap;
      if (top + h > vh - 8) top = Math.max(anchor.top - gap - h, 8);
      if (top + h > vh - 8) top = vh - 8 - h;
      if (top < 8) top = 8;
    }
    const rightAligned = openPop === "dev" || openPop === "loop";
    const left = rightAligned ? anchor.right - w : anchor.left + anchor.width / 2 - w / 2;
    setPopPos({ top, left: Math.max(8, Math.min(left, vw - w - 8)) });
  }, [openPop, anchor]);

  const popupStyle = (kind: "vol" | "dev" | "loop"): React.CSSProperties => {
    if (popPos) return { left: popPos.left, top: popPos.top };
    if (!anchor) return {};
    const vh = window.innerHeight;
    const gap = 8;
    const w = kind === "dev" ? 308 : kind === "loop" ? 190 : 120;
    const up = kind === "vol" ? true : anchor.top - gap >= vh - anchor.bottom - gap;
    const rightAligned = kind === "dev" || kind === "loop";
    const left = rightAligned
      ? anchor.right - w
      : anchor.left + anchor.width / 2 - w / 2;
    return up ? { left, bottom: vh - anchor.top + gap } : { left, top: anchor.bottom + gap };
  };

  const togglePlay = () => {
    if (!hasMedia) return;
    if (isPlaying) void pause(tile.id);
    else void play(tile.id);
  };

  const toggleMute = () => void setMuted(tile.id, !tile.muted);

  const toggleAutoMix = () =>
    void setFades(tile.id, { ...tile.fades, autoMix: !tile.fades.autoMix });

  const seekCommit = (v: number) => {
    void seek(tile.id, v);
  };

  const seekMax = tile.durationSecs > 0 ? tile.durationSecs : 1;

  const valueFromPointer = (e: React.PointerEvent<HTMLInputElement>) => {
    const el = seekRef.current;
    if (!el) return 0;
    const rect = el.getBoundingClientRect();
    const pct = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
    return pct * seekMax;
  };

  const seekTo = (secs: number) => {
    const now = Date.now();
    if (now - lastSeekAt.current < 90) return;
    lastSeekAt.current = now;
    seekCommit(Math.min(secs, seekMax));
  };

  const updateHoverTip = (e: React.PointerEvent<HTMLInputElement>) => {
    const el = seekRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const pct = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
    const left = Math.max(24, Math.min(e.clientX - rect.left, rect.width - 24));
    setHoverTip({ left, secs: pct * seekMax });
  };

  const stopSeeking = () => {
    seekingRef.current = false;
    setDragging(null);
  };

  const fileType = media
    ? `${media.kind === "video" ? "Video" : "Audio"}${
        media.container ? ` · ${media.container.toUpperCase()}` : ""
      }`
    : "";

  const concreteDevices = snapshot.devices.filter((d) => d.id !== DEFAULT_DEVICE_ID);
  const defaultDevId = snapshot.settings.defaultDeviceId || DEFAULT_DEVICE_ID;
  const defaultDev = snapshot.devices.find((d) => d.id === defaultDevId);
  const defaultLabel =
    defaultDevId === DEFAULT_DEVICE_ID
      ? "System Default"
      : (defaultDev?.name ?? defaultDevId);

  const deviceTitle =
    tile.deviceId === DEFAULT_DEVICE_ID
      ? `Output: Default (${defaultLabel})`
      : `Output: ${tile.deviceId}`;

  return (
    <>
      <article
      className={`tile ${dragOver ? "dragover" : ""} ${dimmed ? "dragging" : ""} status-${
        tile.status
      }`}
      data-tile-id={tile.id}
      onDragOver={(e) => {
        e.preventDefault();
        e.stopPropagation();
        e.dataTransfer.dropEffect = "copy";
        setDragOver(true);
      }}
      onDragLeave={(e) => {
        if (e.currentTarget === e.target) setDragOver(false);
      }}
      onDrop={(e) => {
        e.preventDefault();
        e.stopPropagation();
        setDragOver(false);
        const paths = droppedPaths(e.dataTransfer);
        void loadDropped(Array.from(e.dataTransfer.files), paths, tile.id);
      }}
    >
      <header
        className="tile-header"
        onPointerDown={(e) => {
          if ((e.target as HTMLElement).closest("button")) return;
          onStartDrag?.(e, tile.id);
        }}
      >
        <span className={`tile-title ${hasMedia ? "" : "title-empty"}`} title={tile.title}>
          {tile.title || "Empty Deck"}
        </span>
        {hasMedia ? (
          <span className="tile-type" title={fileType}>
            {fileType}
          </span>
        ) : null}
        <div className="tile-tools">
          <button
            className="tool-btn"
            title="Remove deck"
            onClick={(e) => {
              e.stopPropagation();
              void removeTile(tile.id);
            }}
          >
            x
          </button>
        </div>
      </header>

      <div className="tile-body">
        <div className="tile-center">
          <button
            className={`play-btn ${isPlaying ? "playing" : ""}`}
            title={isPlaying ? "Pause" : "Play"}
            disabled={!hasMedia}
            onClick={(e) => {
              e.stopPropagation();
              togglePlay();
            }}
          >
            {isPlaying ? <IconPause size={30} /> : <IconPlay size={30} />}
          </button>
          <div className="tile-time">
            <span>{formatTime(showPos)}</span>
            <span>/ {formatDuration(tile.durationSecs)}</span>
          </div>
        </div>

        <div className="seek-wrap">
          <input
            ref={seekRef}
            className="seek"
            type="range"
            min={0}
            max={seekMax}
            step={0.1}
            value={Math.min(showPos, seekMax)}
            style={{ "--seek-pct": `${(Math.min(showPos, seekMax) / seekMax) * 100}%` } as React.CSSProperties}
            disabled={!hasMedia}
            onChange={(e) => {
              const v = Number(e.target.value);
              setDragging(v);
              if (seekingRef.current) seekTo(v);
            }}
            onPointerDown={(e) => {
              if (!hasMedia) return;
              seekingRef.current = true;
              const v = valueFromPointer(e);
              setDragging(v);
              seekTo(v);
              e.currentTarget.setPointerCapture(e.pointerId);
            }}
            onPointerMove={(e) => {
              if (!hasMedia) return;
              if (seekingRef.current) {
                const v = valueFromPointer(e);
                setDragging(v);
                seekTo(v);
              } else {
                updateHoverTip(e);
              }
            }}
            onPointerUp={stopSeeking}
            onPointerCancel={stopSeeking}
            onPointerLeave={() => {
              if (!seekingRef.current) setHoverTip(null);
            }}
            onLostPointerCapture={stopSeeking}
            onKeyUp={(e) => {
              if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
                seekCommit(Number((e.target as HTMLInputElement).value));
              }
            }}
          />
          {hoverTip && hasMedia ? (
            <span className="seek-tip" style={{ left: hoverTip.left }}>
              {formatTime(hoverTip.secs)}
            </span>
          ) : null}
        </div>
      </div>

      <footer className="tile-footer" onClick={(e) => e.stopPropagation()}>
        {popupOpen ? <div className="pop-veil" onClick={closePopups} /> : null}

        <div className="deck-ctl-row">
          <div className="ctl-item">
            <button
              className={`ctl-icon ${tile.loopMode !== "off" ? "active" : ""} ${openPop === "loop" ? "open" : ""}`}
              title={LOOP_TITLES[tile.loopMode]}
              onClick={toggleLoop}
            >
              <LoopBadgeIcon value={tile.loopMode} />
            </button>
          </div>

          <div className="ctl-item">
            <button
              className={`ctl-icon ${openPop === "vol" ? "open" : ""}`}
              title={tile.muted ? "Volume (muted)" : "Volume"}
              onClick={toggleVol}
            >
              {tile.muted ? <IconVolumeMute /> : <IconVolume />}
            </button>
          </div>

          <div className="ctl-item">
            <button
              className={`ctl-icon ${tile.fades.autoMix ? "active" : ""}`}
              title={
                tile.fades.autoMix
                  ? "Auto-mix: on — ducks the other auto-mix decks while this one is active"
                  : "Auto-mix: off — click to duck the other auto-mix decks while this one is active"
              }
              onClick={() => toggleAutoMix()}
            >
              <IconShuffle />
            </button>
          </div>

          <div className="ctl-item">
            <button
              className={`ctl-icon ${openPop === "dev" ? "open" : ""}`}
              title={deviceTitle}
              onClick={toggleDev}
            >
              <IconOutput />
            </button>
          </div>
        </div>
      </footer>

      {tile.error ? <p className="tile-error">{tile.error}</p> : null}
      </article>

      {openPop && anchor
        ? createPortal(
            openPop === "vol" ? (
              <div className="ctl-pop vol-pop pop-fixed" ref={popEl} style={popupStyle("vol")}>
                <button
                  className={`pop-mute ${tile.muted ? "active" : ""}`}
                  title={tile.muted ? "Unmute" : "Mute"}
                  onClick={() => toggleMute()}
                >
                  {tile.muted ? <IconVolumeMute size={16} /> : <IconVolume size={16} />}
                </button>
                <input
                  className="vol-v"
                  type="range"
                  min={0}
                  max={1}
                  step={0.01}
                  value={tile.volume}
                  onChange={(e) => void setVolume(tile.id, Number(e.target.value))}
                />
                <span className="vol-pct">{Math.round(tile.volume * 100)}</span>
              </div>
            ) : openPop === "dev" ? (
              <div className="ctl-pop dev-pop pop-fixed" ref={popEl} style={popupStyle("dev")}>
                <div className="pop-title">Output device</div>
                <div className="dev-list">
                  <button
                    className={`dev-item ${tile.deviceId === DEFAULT_DEVICE_ID ? "active" : ""}`}
                    title="Follow the default output device set in Settings"
                    onClick={() => {
                      void setTileDevice(tile.id, DEFAULT_DEVICE_ID);
                      closePopups();
                    }}
                  >
                    <span className="dev-name">Default device</span>
                    <span className="dev-sub">{defaultLabel}</span>
                    <span className="dev-mark">
                      {tile.deviceId === DEFAULT_DEVICE_ID ? <IconCheck size={14} /> : null}
                    </span>
                  </button>
                  {concreteDevices.map((d) => (
                    <button
                      key={d.id}
                      className={`dev-item ${tile.deviceId === d.id ? "active" : ""}`}
                      title={d.name}
                      onClick={() => {
                        void setTileDevice(tile.id, d.id);
                        closePopups();
                      }}
                    >
                      <span className="dev-name">{d.name}</span>
                      <span className="dev-mark">
                        {tile.deviceId === d.id ? <IconCheck size={14} /> : null}
                      </span>
                    </button>
                  ))}
                    </div>
                  </div>
                )
              : openPop === "loop" ? (
                  <div
                    className="ctl-pop loop-pop pop-fixed"
                    ref={popEl}
                    style={popupStyle("loop")}
                  >
                    <div className="pop-title">Loop repeats</div>
                    <div className="loop-list">
                      {LOOP_OPTIONS.map((opt) => (
                        <button
                          key={opt.value}
                          className={`loop-item ${tile.loopMode === opt.value ? "active" : ""}`}
                          title={LOOP_TITLES[opt.value]}
                          onClick={() => {
                            void setLoop(tile.id, opt.value);
                            closePopups();
                          }}
                        >
                          <LoopBadgeIcon value={opt.value} />
                          <span className="loop-name">{opt.label}</span>
                          <span className="dev-mark">
                            {tile.loopMode === opt.value ? <IconCheck size={14} /> : null}
                          </span>
                        </button>
                      ))}
                    </div>
                  </div>
                ) : null,
            document.body,
          )
        : null}
    </>
  );
}