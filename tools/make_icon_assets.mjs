// Builds RingDeck.App/Assets from Microsoft's MIT-licensed Fluent assets (see THIRD-PARTY-NOTICES.md):
//   emoji.zip   one 96x96 PNG per emoji (from the Flat SVGs of microsoft/fluentui-emoji) plus emoji.json
//   icons.json  name -> code point for the 24px Regular glyphs of microsoft/fluentui-system-icons
//   FluentSystemIcons-Regular.ttf copied as is
//   brands.zip  one white 80x80 PNG per brand logo (from the simple-icons npm package) plus brands.json; only logos
//               that carry no license of their own or CC0 are included
// Usage: node tools/make_icon_assets.mjs <fluentui-emoji checkout> <fluent-icons folder> <simple-icons package folder>
// Needs playwright-core and a Chromium (headless shell) to draw the SVGs.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { chromium } from 'playwright-core';

const [emojiRepo, iconsDir, brandsDir] = process.argv.slice(2);
const out = path.join(path.dirname(new URL(import.meta.url).pathname), '..', 'RingDeck.App', 'Assets');
const SIZE = 96;

// ---- icons ----
const glyphs = JSON.parse(fs.readFileSync(path.join(iconsDir, 'FluentSystemIcons-Regular.json'), 'utf8'));
const icons = Object.entries(glyphs)
  .filter(([n]) => n.endsWith('_24_regular'))
  .map(([n, c]) => ({ n: n.replace(/^ic_fluent_/, '').replace(/_24_regular$/, '').replaceAll('_', ' '), c }))
  .sort((a, b) => a.n.localeCompare(b.n));
fs.writeFileSync(path.join(out, 'icons.json'), JSON.stringify(icons));
fs.copyFileSync(path.join(iconsDir, 'FluentSystemIcons-Regular.ttf'), path.join(out, 'FluentSystemIcons-Regular.ttf'));

// ---- emoji ----
const entries = [];
for (const dir of fs.readdirSync(path.join(emojiRepo, 'assets')).sort()) {
  const base = path.join(emojiRepo, 'assets', dir);
  const metaFile = path.join(base, 'metadata.json');
  if (!fs.existsSync(metaFile)) continue;
  const meta = JSON.parse(fs.readFileSync(metaFile, 'utf8'));
  const flat = [path.join(base, 'Flat'), path.join(base, 'Default', 'Flat')].find(fs.existsSync);
  const svg = flat && fs.readdirSync(flat).find(f => f.endsWith('.svg'));
  if (!svg || !meta.unicode) continue;
  const id = meta.unicode.replaceAll(' ', '-');
  entries.push({ u: id, n: meta.cldr ?? dir.toLowerCase(), g: meta.group ?? '', k: meta.keywords ?? [], c: meta.glyph ?? '', file: path.join(flat, svg) });
}

// Standard emoji order: by group, then by code point (smileys first, flags last).
const GROUPS = ['Smileys & Emotion', 'People & Body', 'Animals & Nature', 'Food & Drink', 'Travel & Places', 'Activities', 'Objects', 'Symbols', 'Flags'];
const rank = g => { const i = GROUPS.indexOf(g); return i < 0 ? GROUPS.length : i; };
// The classic faces (U+1F600-1F64F) lead; older symbols below U+1F000 (☠ ❤ ☕) come last in their group.
const code = u => { const parts = u.split('-').map(p => parseInt(p, 16)); const c = parts[0]; return [c >= 0x1F600 && c <= 0x1F64F ? 0 : c < 0x1F000 ? 2 : 1, ...parts]; };
entries.sort((a, b) => rank(a.g) - rank(b.g) || (() => { const x = code(a.u), y = code(b.u); for (let i = 0; i < Math.max(x.length, y.length); i++) { const d = (x[i] ?? -1) - (y[i] ?? -1); if (d) return d; } return 0; })());

const exe = fs.globSync(`${process.env.HOME}/.cache/ms-playwright/chromium_headless_shell-*/*/chrome-headless-shell`).sort().pop();
const browser = await chromium.launch({ executablePath: exe });
const page = await browser.newPage();
await page.setContent('<canvas id="c"></canvas>');
const pngs = [];
for (const e of entries) {
  const svg = fs.readFileSync(e.file, 'utf8');
  const b64 = await page.evaluate(async ([svg, size]) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svg)));
    await img.decode();
    const c = document.getElementById('c');
    c.width = c.height = size;
    const g = c.getContext('2d');
    g.clearRect(0, 0, size, size);
    g.drawImage(img, 0, 0, size, size);
    return c.toDataURL('image/png').split(',')[1];
  }, [svg, SIZE]);
  pngs.push({ name: `${e.u}.png`, data: Buffer.from(b64, 'base64') });
}
const index = entries.map(({ file, ...rest }) => rest);
pngs.push({ name: 'emoji.json', data: Buffer.from(JSON.stringify(index)) });
fs.writeFileSync(path.join(out, 'emoji.zip'), zip(pngs));

// ---- brands ----
const brandData = JSON.parse(fs.readFileSync(path.join(brandsDir, 'data', 'simple-icons.json'), 'utf8'));
const brands = brandData.filter(b => !b.license || b.license.type === 'CC0-1.0');
const brandPngs = [];
for (const b of brands) {
  const file = path.join(brandsDir, 'icons', `${b.slug}.svg`);
  if (!fs.existsSync(file)) continue;
  const svg = fs.readFileSync(file, 'utf8').replace('<svg ', '<svg fill="#ffffff" ');
  const b64 = await page.evaluate(async ([svg, size]) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svg)));
    await img.decode();
    const c = document.getElementById('c');
    c.width = c.height = size;
    const g = c.getContext('2d');
    g.clearRect(0, 0, size, size);
    g.drawImage(img, 0, 0, size, size);
    return c.toDataURL('image/png').split(',')[1];
  }, [svg, 80]);
  brandPngs.push({ name: `${b.slug}.png`, data: Buffer.from(b64, 'base64') });
}
const brandIndex = brands.filter(b => brandPngs.some(p => p.name === `${b.slug}.png`))
  .map(b => ({ s: b.slug, n: b.title, h: b.hex, k: [...(b.aliases?.aka ?? []), ...(b.aliases?.dup ?? []).map(d => d.title)] }));
brandPngs.push({ name: 'brands.json', data: Buffer.from(JSON.stringify(brandIndex)) });
fs.writeFileSync(path.join(out, 'brands.zip'), zip(brandPngs));
await browser.close();
console.log(`${icons.length} icons, ${entries.length} emoji, ${brandIndex.length} brands (${brandData.length - brands.length} left out for their own licenses)`);

// Minimal zip writer (stored PNGs, deflated JSON), so no extra package is needed.
function zip(files) {
  const crcTable = new Uint32Array(256).map((_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc32 = buf => { let c = 0xFFFFFFFF; for (const b of buf) c = crcTable[(c ^ b) & 0xFF] ^ (c >>> 8); return (c ^ 0xFFFFFFFF) >>> 0; };
  const local = [], central = [];
  let offset = 0;
  for (const f of files) {
    const name = Buffer.from(f.name);
    const deflate = f.name.endsWith('.json');
    const body = deflate ? zlib.deflateRawSync(f.data, { level: 9 }) : f.data;
    const crc = crc32(f.data);
    const head = Buffer.alloc(30);
    head.writeUInt32LE(0x04034b50, 0); head.writeUInt16LE(20, 4); head.writeUInt16LE(0x0800, 6); head.writeUInt16LE(deflate ? 8 : 0, 8);
    head.writeUInt32LE(crc, 14); head.writeUInt32LE(body.length, 18); head.writeUInt32LE(f.data.length, 22); head.writeUInt16LE(name.length, 26);
    local.push(head, name, body);
    const dir = Buffer.alloc(46);
    dir.writeUInt32LE(0x02014b50, 0); dir.writeUInt16LE(20, 4); dir.writeUInt16LE(20, 6); dir.writeUInt16LE(0x0800, 8); dir.writeUInt16LE(deflate ? 8 : 0, 10);
    dir.writeUInt32LE(crc, 16); dir.writeUInt32LE(body.length, 20); dir.writeUInt32LE(f.data.length, 24); dir.writeUInt16LE(name.length, 28); dir.writeUInt32LE(offset, 42);
    central.push(dir, name);
    offset += 30 + name.length + body.length;
  }
  const size = central.reduce((s, b) => s + b.length, 0);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10); end.writeUInt32LE(size, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...local, ...central, end]);
}
