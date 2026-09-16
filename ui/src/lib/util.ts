import type { Backend } from "./backend";
import { BrowserBackend } from "./browserBackend";
import { isTauriRuntime, TauriBackend } from "./tauriBackend";

export function createBackend(): Backend {
  return isTauriRuntime() ? new TauriBackend() : new BrowserBackend();
}

export function formatTime(secs: number): string {
  if (!Number.isFinite(secs) || secs < 0) secs = 0;
  const total = Math.floor(secs);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const mm = String(m).padStart(2, "0");
  const ss = String(s).padStart(2, "0");
  if (h > 0) return `${h}:${mm}:${ss}`;
  return `${mm}:${ss}`;
}

export function formatDuration(secs: number): string {
  if (!Number.isFinite(secs) || secs <= 0) return "--:--";
  return formatTime(secs);
}

export function statusLabel(status: string): string {
  switch (status) {
    case "playing":
      return "Playing";
    case "paused":
      return "Paused";
    case "loading":
      return "Loading";
    case "ended":
      return "Ended";
    case "error":
      return "Error";
    default:
      return "Stopped";
  }
}