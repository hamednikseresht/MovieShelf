"""Draws the MovieShelf icon with the standard library only: a film reel (amber ring with five sprocket holes) with a play
triangle in its hub, on a dark rounded square. Writes MovieShelf.ico (the desktop shortcut) and
web\\logo.svg (the logo in the app and the browser tab).
Run:  python tools\\icon\\make_icon.py"""
import math
import os
import struct
import zlib

SIZES = [16, 24, 32, 48, 64, 128, 256]
BG, AMBER, WHITE = (24, 24, 27), (251, 191, 36), (250, 250, 250)
ROUND = .22                              # corner radius of the square
RING_OUT, RING_IN = .39, .255            # the reel ring
HOLE_AT, HOLE_R = .3225, .062            # five sprocket holes on the ring
HUB = (.5, .38, .5, .62, .70, .5)         # play triangle (unused here: see triangle())
HOLES = [(.5 + HOLE_AT * math.sin(i * 2 * math.pi / 5), .5 - HOLE_AT * math.cos(i * 2 * math.pi / 5)) for i in range(5)]


def inside_round_rect(x, y, r):
    cx, cy = min(max(x, r), 1 - r), min(max(y, r), 1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def triangle(x, y):
    ax, ay, bx, by, cx, cy = .435, .385, .435, .615, .625, .5   # a little right of centre: it looks centred
    d = lambda px, py, qx, qy, rx, ry: (px - rx) * (qy - ry) - (qx - rx) * (py - ry)
    d1, d2, d3 = d(x, y, ax, ay, bx, by), d(x, y, bx, by, cx, cy), d(x, y, cx, cy, ax, ay)
    return not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0))


def colour(x, y, simple):
    """The colour at a point of the unit square, or None outside the rounded square."""
    if not inside_round_rect(x, y, ROUND):
        return None
    d = math.hypot(x - .5, y - .5)
    if triangle(x, y):
        return WHITE
    if RING_IN <= d <= RING_OUT:
        if not simple and any(math.hypot(x - hx, y - hy) <= HOLE_R for hx, hy in HOLES):
            return BG
        return AMBER
    return BG


def render(n, ss=4):
    simple = n <= 32  # no holes at tiny sizes: they would only be noise
    rows = []
    for py in range(n):
        row = bytearray([0])  # PNG filter: none
        for px in range(n):
            acc, cover = [0, 0, 0], 0
            for sy in range(ss):
                for sx in range(ss):
                    c = colour((px + (sx + .5) / ss) / n, (py + (sy + .5) / ss) / n, simple)
                    if c:
                        cover += 1
                        acc[0] += c[0]; acc[1] += c[1]; acc[2] += c[2]
            if not cover:
                row += bytes((0, 0, 0, 0))
            else:
                row += bytes(round(a / cover) for a in acc) + bytes((round(255 * cover / (ss * ss)),))
        rows.append(bytes(row))
    raw = b''.join(rows)

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', n, n, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def svg():
    """The same drawing as an SVG (64 x 64 units)."""
    s = 64
    holes = ''.join(f'<circle cx="{hx * s:.2f}" cy="{hy * s:.2f}" r="{HOLE_R * s:.2f}" fill="#18181b"/>' for hx, hy in HOLES)
    mid = (RING_OUT + RING_IN) / 2 * s
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {s} {s}"><title>MovieShelf</title>'
            f'<rect width="{s}" height="{s}" rx="{ROUND * s:.2f}" fill="#18181b"/>'
            f'<circle cx="{s / 2}" cy="{s / 2}" r="{mid:.2f}" fill="none" stroke="#fbbf24" stroke-width="{(RING_OUT - RING_IN) * s:.2f}"/>'
            f'{holes}<path d="M{.435 * s:.2f} {.385 * s:.2f}V{.615 * s:.2f}L{.625 * s:.2f} {s / 2}Z" fill="#fafafa"/></svg>\n')


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    pngs = [(n, render(n)) for n in SIZES]
    head = struct.pack('<HHH', 0, 1, len(pngs))
    offset = 6 + 16 * len(pngs)
    entries, blobs = b'', b''
    for n, png in pngs:
        entries += struct.pack('<BBBBHHII', n % 256, n % 256, 0, 0, 1, 32, len(png), offset + len(blobs))
        blobs += png
    with open(os.path.join(here, 'MovieShelf.ico'), 'wb') as f:
        f.write(head + entries + blobs)
    with open(os.path.join(here, 'icon-256.png'), 'wb') as f:
        f.write(pngs[-1][1])
    with open(os.path.join(os.path.dirname(os.path.dirname(here)), 'app', 'web', 'logo.svg'), 'w', encoding='utf-8') as f:
        f.write(svg())
    print('wrote MovieShelf.ico, icon-256.png and app\\web\\logo.svg')


if __name__ == '__main__':
    main()
