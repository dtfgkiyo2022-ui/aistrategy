"""Generate the flat, transparent civilisation glyphs used by the HUD and world banners.

This intentionally uses only Python's standard library so the art can be regenerated on a clean checkout.
The symbols are small geometric marks, not downloaded or purchased artwork.
"""
from __future__ import annotations

import os
import struct
import zlib


SIZE = 48
OUT_DIR = os.path.join(
    os.path.dirname(__file__), "..", "UnityProject", "Assets", "RTS", "UI", "Resources", "Icons", "Civ"
)


def png_rgba(path: str, pixels: list[list[tuple[int, int, int, int]]]) -> None:
    raw = b"".join(b"\x00" + b"".join(bytes(pixel) for pixel in row) for row in pixels)

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    data = b"\x89PNG\r\n\x1a\n"
    data += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(raw, 9))
    data += chunk(b"IEND", b"")
    with open(path, "wb") as handle:
        handle.write(data)


def canvas() -> list[list[tuple[int, int, int, int]]]:
    return [[(0, 0, 0, 0) for _ in range(SIZE)] for _ in range(SIZE)]


def set_pixel(image, x, y, color=(224, 180, 82, 255)):
    if 0 <= x < SIZE and 0 <= y < SIZE:
        image[y][x] = color


def line(image, x0, y0, x1, y1, width=3, color=(224, 180, 82, 255)):
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(steps + 1):
        x = round(x0 + (x1 - x0) * i / steps)
        y = round(y0 + (y1 - y0) * i / steps)
        for oy in range(-width // 2, width // 2 + 1):
            for ox in range(-width // 2, width // 2 + 1):
                set_pixel(image, x + ox, y + oy, color)


def rect(image, x0, y0, x1, y1, color=(224, 180, 82, 255), fill=True, width=3):
    if fill:
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                set_pixel(image, x, y, color)
    else:
        line(image, x0, y0, x1, y0, width, color)
        line(image, x0, y1, x1, y1, width, color)
        line(image, x0, y0, x0, y1, width, color)
        line(image, x1, y0, x1, y1, width, color)


def circle(image, cx, cy, radius, color=(224, 180, 82, 255), fill=True, width=3):
    radius_sq = radius * radius
    inner = max(0, radius - width) ** 2
    for y in range(cy - radius, cy + radius + 1):
        for x in range(cx - radius, cx + radius + 1):
            distance = (x - cx) ** 2 + (y - cy) ** 2
            if (fill and distance <= radius_sq) or (not fill and inner <= distance <= radius_sq):
                set_pixel(image, x, y, color)


def polygon(image, points, color=(224, 180, 82, 255)):
    min_y = max(0, min(y for _, y in points))
    max_y = min(SIZE - 1, max(y for _, y in points))
    for y in range(min_y, max_y + 1):
        intersections = []
        for (x0, y0), (x1, y1) in zip(points, points[1:] + points[:1]):
            if y0 == y1:
                continue
            if min(y0, y1) <= y < max(y0, y1):
                intersections.append(round(x0 + (y - y0) * (x1 - x0) / (y1 - y0)))
        intersections.sort()
        for left, right in zip(intersections[::2], intersections[1::2]):
            for x in range(left, right + 1):
                set_pixel(image, x, y, color)


def glyph(name: str):
    image = canvas()
    gold = (224, 180, 82, 255)
    dark = (54, 42, 32, 255)
    if name == "primitive":
        circle(image, 24, 24, 13, gold, False, 4)
        line(image, 15, 24, 33, 24, 3, gold)
        line(image, 24, 15, 24, 33, 3, gold)
    elif name == "agrarian":
        line(image, 24, 36, 24, 10, 4, gold)
        for y, side in [(29, -1), (23, 1), (17, -1), (12, 1)]:
            line(image, 24, y, 15 if side < 0 else 33, y - 7, 3, gold)
    elif name == "metallurgy":
        circle(image, 24, 24, 8, gold, False, 4)
        for x, y in [(24, 7), (24, 41), (7, 24), (41, 24), (12, 12), (36, 36), (36, 12), (12, 36)]:
            line(image, 24, 24, x, y, 4, gold)
        circle(image, 24, 24, 4, dark, True)
    elif name == "forestry":
        rect(image, 21, 27, 27, 39, gold)
        polygon(image, [(24, 7), (10, 25), (17, 25), (7, 34), (41, 34), (31, 25), (38, 25)], gold)
    elif name == "masonry":
        rect(image, 10, 15, 38, 38, gold, False, 4)
        rect(image, 16, 23, 22, 38, gold)
        rect(image, 26, 23, 32, 38, gold)
        polygon(image, [(8, 15), (24, 7), (40, 15)], gold)
    elif name == "caravan":
        circle(image, 15, 34, 5, gold, False, 3)
        circle(image, 34, 34, 5, gold, False, 3)
        line(image, 10, 29, 15, 17, 4, gold)
        line(image, 15, 17, 29, 17, 4, gold)
        line(image, 29, 17, 35, 29, 4, gold)
        line(image, 15, 17, 21, 10, 4, gold)
    elif name == "cavalry":
        circle(image, 24, 24, 14, gold, False, 5)
        polygon(image, [(18, 11), (24, 5), (30, 11), (27, 16), (21, 16)], gold)
    elif name == "bridge":
        line(image, 8, 16, 40, 16, 4, gold)
        line(image, 8, 32, 40, 32, 4, gold)
        for x in (12, 24, 36):
            line(image, x, 16, x, 32, 3, gold)
        line(image, 8, 16, 16, 25, 3, gold)
        line(image, 16, 25, 24, 16, 3, gold)
        line(image, 24, 16, 32, 25, 3, gold)
        line(image, 32, 25, 40, 16, 3, gold)
    elif name == "academy":
        rect(image, 11, 10, 37, 38, gold, False, 4)
        line(image, 15, 18, 33, 18, 3, gold)
        line(image, 15, 25, 33, 25, 3, gold)
        line(image, 15, 32, 28, 32, 3, gold)
    elif name == "cult":
        line(image, 24, 38, 24, 12, 4, gold)
        line(image, 15, 19, 33, 19, 4, gold)
        circle(image, 24, 9, 4, gold, False, 3)
        polygon(image, [(14, 39), (24, 27), (34, 39)], gold)
    elif name == "fishing":
        line(image, 7, 27, 17, 20, 3, gold)
        line(image, 17, 20, 27, 27, 3, gold)
        line(image, 27, 27, 37, 20, 3, gold)
        polygon(image, [(36, 15), (43, 20), (36, 25)], gold)
    elif name == "mountain":
        polygon(image, [(7, 37), (20, 12), (28, 25), (33, 17), (43, 37)], gold)
        line(image, 20, 12, 23, 22, 3, dark)
    elif name == "tollgate":
        rect(image, 10, 12, 16, 39, gold)
        rect(image, 32, 12, 38, 39, gold)
        line(image, 13, 13, 35, 30, 4, gold)
        line(image, 13, 30, 35, 13, 4, gold)
    elif name == "metropolis":
        rect(image, 10, 20, 19, 38, gold)
        rect(image, 21, 12, 29, 38, gold)
        rect(image, 31, 7, 38, 38, gold)
        for x, y in [(14, 25), (25, 18), (35, 13)]:
            rect(image, x, y, x + 2, y + 3, dark)
    elif name == "sanctuary":
        polygon(image, [(24, 6), (29, 18), (42, 19), (32, 27), (35, 40), (24, 33), (13, 40), (16, 27), (6, 19), (19, 18)], gold)
    return image


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    names = [
        "primitive", "agrarian", "metallurgy", "forestry", "masonry", "caravan", "cavalry",
        "bridge", "academy", "cult", "fishing", "mountain", "tollgate", "metropolis", "sanctuary",
    ]
    for name in names:
        png_rgba(os.path.join(OUT_DIR, "civ_" + name + ".png"), glyph(name))


if __name__ == "__main__":
    main()
