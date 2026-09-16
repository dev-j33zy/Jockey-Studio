type FileWithPath = File & { path?: string };

/** Resolve absolute file paths from a drag-and-drop DataTransfer.
 *
 * Native webviews expose `File.path` on the dropped `File` objects. When that
 * isn't available (e.g. other Chromium builds), falls back to parsing the
 * `text/uri-list` payload (`file:///C:/...` → Windows path).
 */
export function droppedPaths(dt: DataTransfer): string[] {
  const out: string[] = [];
  for (const f of Array.from(dt.files)) {
    const p = (f as FileWithPath).path;
    if (p) out.push(p);
  }
  if (out.length) return out;

  try {
    const uriList = dt.getData("text/uri-list") || "";
    for (const line of uriList.split("\n")) {
      const uri = line.trim();
      if (!uri.startsWith("file:///")) continue;
      let p = decodeURIComponent(uri).slice("file:///".length);
      if (p.startsWith("/") && !/^\/[A-Za-z]:/.test(p)) p = p.slice(1);
      if (/^[A-Za-z]:/.test(p)) p = p.replace(/\//g, "\\");
      if (p) out.push(p);
    }
  } catch {
    // Some engines throw when reading data during a drop.
  }
  return [...new Set(out)];
}