import { deflateSync } from "node:zlib";
import { writeFileSync } from "node:fs";

const SIZE = 1024;

function crc32(buf) {
  let table = crc32.table;
  if (!table) {
    table = crc32.table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      table[n] = c;
    }
  }
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = table[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const typeBuf = Buffer.from(type, "ascii");
  const crcBuf = Buffer.alloc(4);
  crcBuf.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0);
  return Buffer.concat([len, typeBuf, data, crcBuf]);
}

const rgba = new Uint8Array(SIZE * SIZE * 4);

function setPx(x, y, r, g, b, a = 255) {
  if (x < 0 || y < 0 || x >= SIZE || y >= SIZE) return;
  const i = (y * SIZE + x) * 4;
  rgba[i] = r;
  rgba[i + 1] = g;
  rgba[i + 2] = b;
  rgba[i + 3] = a;
}

function inRoundRect(x, y, cx, cy, w, h, r) {
  const dx = Math.abs(x - cx);
  const dy = Math.abs(y - cy);
  const halfW = w / 2;
  const halfH = h / 2;
  if (dx > halfW || dy > halfH) return false;
  if (dx <= halfW - r || dy <= halfH - r) return true;
  const cornerDx = dx - (halfW - r);
  const cornerDy = dy - (halfH - r);
  return cornerDx * cornerDx + cornerDy * cornerDy <= r * r;
}

function fillRoundRect(cx, cy, w, h, r, color) {
  const x0 = Math.floor(cx - w / 2);
  const y0 = Math.floor(cy - h / 2);
  const x1 = Math.ceil(cx + w / 2);
  const y1 = Math.ceil(cy + h / 2);
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      if (inRoundRect(x + 0.5, y + 0.5, cx, cy, w, h, r)) {
        setPx(x, y, color[0], color[1], color[2]);
      }
    }
  }
}

function lerp(a, b, t) {
  return a + (b - a) * t;
}

for (let y = 0; y < SIZE; y++) {
  for (let x = 0; x < SIZE; x++) {
    const t = (x + y) / (2 * SIZE);
    setPx(x, y, Math.round(lerp(15, 30, t)), Math.round(lerp(23, 42, t)), Math.round(lerp(42, 60, t)));
  }
}

const decks = [
  { cx: 0.28, c: [34, 211, 238] },
  { cx: 0.44, c: [232, 121, 249] },
  { cx: 0.6, c: [251, 191, 36] },
  { cx: 0.76, c: [74, 222, 128] },
];

for (const deck of decks) {
  fillRoundRect(deck.cx * SIZE, 0.5 * SIZE, 120, 300, 26, deck.c);
  fillRoundRect(deck.cx * SIZE, 0.5 * SIZE, 46, 560, 23, [15, 23, 42]);
}

const raw = Buffer.alloc((SIZE * 4 + 1) * SIZE);
for (let y = 0; y < SIZE; y++) {
  raw[y * (SIZE * 4 + 1)] = 0;
  Buffer.from(rgba.buffer, rgba.byteOffset + y * SIZE * 4, SIZE * 4).copy(raw, y * (SIZE * 4 + 1) + 1);
}

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk("IHDR", (() => {
    const b = Buffer.alloc(13);
    b.writeUInt32BE(SIZE, 0);
    b.writeUInt32BE(SIZE, 4);
    b[8] = 8;
    b[9] = 6;
    b[10] = 0;
    b[11] = 0;
    b[12] = 0;
    return b;
  })()),
  chunk("IDAT", deflateSync(raw, { level: 9 })),
  chunk("IEND", Buffer.alloc(0)),
]);

writeFileSync(new URL("../ui/src/assets/app-icon.png", import.meta.url), png);
console.log("wrote ui/src/assets/app-icon.png", png.length, "bytes");