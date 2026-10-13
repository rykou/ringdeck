#!/usr/bin/env python3
"""Writes ringdeck.ico, installer/banner.bmp (493x58) and installer/dialog.bmp (493x312). Standard library only."""
import math, os, struct, zlib

BG, BG2, ACCENT, DOT, WHITE = (24, 24, 32), (44, 40, 72), (76, 194, 255), (232, 234, 240), (255, 255, 255)

def ring(x, y, cx, cy, r, dot):
    """Colour of the 8-dot ring motif at (x, y), or None outside it."""
    for i in range(8):
        a = math.tau * i / 8
        dx, dy = x - (cx + math.sin(a) * r), y - (cy - math.cos(a) * r)
        if dx * dx + dy * dy <= dot * dot:
            return ACCENT if i == 1 else DOT
    d = math.hypot(x - cx, y - cy)
    if r * 0.30 <= d <= r * 0.42:
        return ACCENT
    return None

def icon_pixel(size):
    def f(x, y):
        m, rad = size * 0.04, size * 0.22
        qx = min(x - m, size - m - x); qy = min(y - m, size - m - y)
        if qx < 0 or qy < 0 or (qx < rad and qy < rad and math.hypot(rad - qx, rad - qy) > rad):
            return (0, 0, 0, 0)
        c = ring(x, y, size / 2, size / 2, size * 0.30, size * 0.085)
        return (*(c or BG), 255)
    return f

def banner_pixel(x, y):
    c = ring(x, y, 493 - 30, 29, 17, 4.2)
    if c: return (*c, 255)
    return (*(BG if math.hypot(x - (493 - 30), y - 29) <= 26 else WHITE), 255)

def dialog_pixel(x, y):
    if x >= 164: return (*WHITE, 255)
    t = y / 312
    base = tuple(round(BG[i] + (BG2[i] - BG[i]) * t) for i in range(3))
    return (*(ring(x, y, 82, 120, 48, 11) or base), 255)

def render(w, h, f, ss=3):
    rows = []
    for py in range(h):
        row = []
        for px in range(w):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    r, g, b, a = f(px + (sx + 0.5) / ss, py + (sy + 0.5) / ss)
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a
            a = acc[3] / (ss * ss)
            row.append(tuple(round(acc[i] / acc[3]) if acc[3] else 0 for i in range(3)) + (round(a),))
        rows.append(row)
    return rows

def png(rows):
    w, h = len(rows[0]), len(rows)
    raw = b"".join(b"\0" + bytes(v for p in row for v in p) for row in rows)
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

def write_ico(path, sizes=(16, 24, 32, 48, 64, 256)):
    images = [png(render(s, s, icon_pixel(s))) for s in sizes]
    out, offset = struct.pack("<HHH", 0, 1, len(sizes)), 6 + 16 * len(sizes)
    for s, data in zip(sizes, images):
        out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as fh:
        fh.write(out + b"".join(images))

def write_bmp(path, w, h, f):
    rows, pad = render(w, h, f, ss=2), (4 - w * 3 % 4) % 4
    body = b"".join(b"".join(bytes((p[2], p[1], p[0])) for p in row) + b"\0" * pad for row in reversed(rows))
    with open(path, "wb") as fh:
        fh.write(b"BM" + struct.pack("<IHHI", 54 + len(body), 0, 0, 54)
                 + struct.pack("<IiiHHIIiiII", 40, w, h, 1, 24, 0, len(body), 2835, 2835, 0, 0) + body)

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    os.makedirs(os.path.join(here, "installer"), exist_ok=True)
    write_ico(os.path.join(here, "ringdeck.ico"))
    write_bmp(os.path.join(here, "installer", "banner.bmp"), 493, 58, banner_pixel)
    write_bmp(os.path.join(here, "installer", "dialog.bmp"), 493, 312, dialog_pixel)
