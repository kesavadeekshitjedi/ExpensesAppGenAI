// Generates the PWA / home-screen PNG icons from a simple drawn "house" glyph, using only Node
// built-ins (no image libraries). Run from src/web:  node scripts/gen-icons.mjs
import { deflateSync } from 'node:zlib'
import { writeFileSync, mkdirSync } from 'node:fs'

const BRAND = [134, 59, 255] // #863bff, matching favicon.svg
const WHITE = [255, 255, 255]

const crcTable = (() => {
  const t = new Uint32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    t[n] = c >>> 0
  }
  return t
})()

function crc32(buf) {
  let c = 0xffffffff
  for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function chunk(type, data) {
  const len = Buffer.alloc(4)
  len.writeUInt32BE(data.length, 0)
  const typeBuf = Buffer.from(type, 'ascii')
  const body = Buffer.concat([typeBuf, data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body), 0)
  return Buffer.concat([len, body, crc])
}

function pointInTriangle(px, py, ax, ay, bx, by, cx, cy) {
  const d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
  const a = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d
  const b = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d
  const c = 1 - a - b
  return a >= 0 && b >= 0 && c >= 0
}

function drawIcon(size) {
  const raw = Buffer.alloc(size * (size * 4 + 1))
  const S = size
  for (let y = 0; y < S; y++) {
    const rowStart = y * (S * 4 + 1)
    raw[rowStart] = 0 // filter: none
    for (let x = 0; x < S; x++) {
      let color = BRAND
      const roof = pointInTriangle(x, y, 0.5 * S, 0.2 * S, 0.17 * S, 0.52 * S, 0.83 * S, 0.52 * S)
      const walls = x >= 0.27 * S && x <= 0.73 * S && y >= 0.5 * S && y <= 0.82 * S
      const door = x >= 0.44 * S && x <= 0.56 * S && y >= 0.62 * S && y <= 0.82 * S
      if ((roof || walls) && !door) color = WHITE
      const o = rowStart + 1 + x * 4
      raw[o] = color[0]
      raw[o + 1] = color[1]
      raw[o + 2] = color[2]
      raw[o + 3] = 255
    }
  }

  const sig = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(S, 0)
  ihdr.writeUInt32BE(S, 4)
  ihdr[8] = 8 // bit depth
  ihdr[9] = 6 // color type RGBA
  ihdr[10] = 0
  ihdr[11] = 0
  ihdr[12] = 0
  return Buffer.concat([sig, chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))])
}

mkdirSync('public', { recursive: true })
for (const [name, size] of [
  ['public/icon-192.png', 192],
  ['public/icon-512.png', 512],
  ['public/apple-touch-icon.png', 180],
]) {
  writeFileSync(name, drawIcon(size))
  console.log(`wrote ${name} (${size}x${size})`)
}
