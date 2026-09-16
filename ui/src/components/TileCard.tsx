import { useState } from "react";
import type { TileState } from "../types";
import { useStore } from "../state/store";
import { formatDuration, formatTime } from "../lib/util";
import { droppedPaths } from "../lib/dropPaths";
import {
  IconCheck,
  IconOutput,
  IconPause,
  IconPlay,
  IconRepeat,
  IconRepeatOne,
  IconShuffle,
  IconVolume,
  IconVolumeMute,
} from "./icons";

const LOOP_TITLES: Record<TileState["loopMode"], string> = {
  off: "Loop: Off — click to enable",
  one: "Loop: One track",
  all: "Loop: All tracks",
};

interface TileCardProps {
  tile: TileState;
  onStartDrag?: (e: React.PointerEvent<HTMLElement>, id: string) => void;
  dimmed?: boolean;
}

export default function TileCard({ tile, onStartDrag, dimmed }: TileCardProps) {
  const {
    selectTile,
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
  const [showVol, setShowVol] = useState(false);
  const [showDevices, setShowDevices] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const showPos = dragging ?? tile.positionSecs;

  const isPlaying = tile.status === "playing";
  const hasMedia = !!tile.media;
  const popupOpen = showVol || showDevices;
  const media = snapshot.media.find((m) => m.id === tile.media) ?? null;

  const closePopups = () => {
    setShowVol(false);
    setShowDevices(false);
  };

  const togglePlay = () => {
    if (!hasMedia) return;
    if (isPlaying) void pause(tile.id);
    else void play(tile.id);
  };

  const cycleLoop = () => {
    const next = tile.loopMode === "off" ? "one" : tile.loopMode === "one" ? "all" : "off";
    void setLoop(tile.id, next);
  };

  const toggleMute = () => void setMuted(tile.id, !tile.muted);

  const toggleAutoMix = () =>
    void setFades(tile.id, { ...tile.fades, autoMix: !tile.fades.autoMix });

  const seekCommit = (v: number) => {
    void seek(tile.id, v);
  };

  const fileType = media
    ? `${media.kind === "video" ? "Video" : "Audio"}${
        media.container ? ` · ${media.container.toUpperCase()}` : ""
      }`
    : "";

  const devices =
    snapshot.devices.length > 0
      ? snapshot.devices
      : [{ id: "default", name: "System Default", isDefault: true, isActive: false, channels: 0, sampleRate: 0 }];

  return (
    <article
      className={`tile ${dragOver ? "dragover" : ""} ${dimmed ? "dragging" : ""} status-${
        tile.status
      }`}
      data-tile-id={tile.id}
      onClick={() => selectTile(tile.id)}
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

        <input
          className="seek"
          type="range"
          min={0}
          max={tile.durationSecs > 0 ? tile.durationSecs : 1}
          step={0.1}
          value={Math.min(showPos, tile.durationSecs > 0 ? tile.durationSecs : 1)}
          disabled={!hasMedia}
          onChange={(e) => setDragging(Number(e.target.value))}
          onPointerUp={(e) => {
            const v = Number((e.target as HTMLInputElement).value);
            setDragging(null);
            seekCommit(v);
          }}
          onKeyUp={(e) => {
            if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
              const v = Number((e.target as HTMLInputElement).value);
              seekCommit(v);
            }
          }}
        />
      </div>

      <footer className="tile-footer" onClick={(e) => e.stopPropagation()}>
        {popupOpen ? <div className="pop-veil" onClick={closePopups} /> : null}

        <div className="deck-ctl-row">
          <div className="ctl-item">
            <button
              className={`ctl-icon ${tile.loopMode !== "off" ? "active" : ""}`}
              title={LOOP_TITLES[tile.loopMode]}
              onClick={() => cycleLoop()}
            >
              {tile.loopMode === "one" ? <IconRepeatOne /> : <IconRepeat />}
            </button>
          </div>

          <div className="ctl-item">
            <button
              className={`ctl-icon ${showVol ? "open" : ""}`}
              title={tile.muted ? "Volume (muted)" : "Volume"}
              onClick={() => {
                setShowVol((v) => !v);
                setShowDevices(false);
              }}
            >
              {tile.muted ? <IconVolumeMute /> : <IconVolume />}
            </button>
            {showVol ? (
              <div className="ctl-pop vol-pop">
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
            ) : null}
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
              className={`ctl-icon ${showDevices ? "open" : ""}`}
              title="Output device"
              onClick={() => {
                setShowDevices((v) => !v);
                setShowVol(false);
              }}
            >
              <IconOutput />
            </button>
            {showDevices ? (
              <div className="ctl-pop dev-pop">
                <div className="pop-title">Output device</div>
                <div className="dev-list">
                  {devices.map((d) => (
                    <button
                      key={d.id}
                      className={`dev-item ${tile.deviceId === d.id ? "active" : ""}`}
                      title={d.isDefault ? "System default output" : d.name}
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
            ) : null}
          </div>
        </div>
      </footer>

      {tile.error ? <p className="tile-error">{tile.error}</p> : null}
    </article>
  );
}