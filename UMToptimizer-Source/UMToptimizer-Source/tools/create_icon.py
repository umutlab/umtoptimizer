"""Build the multi-resolution Windows icon for UMToptimizer (stdlib only)."""

from __future__ import annotations

import math
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets" / "UMToptimizer.ico"
SIZES = (16, 24, 32, 48, 64, 128, 256)


def bezier(p0, p1, p2, p3, count=40):
    points = []
    for i in range(count + 1):
        t = i / count
        u = 1 - t
        points.append((
            u**3 * p0[0] + 3 * u**2 * t * p1[0] + 3 * u * t**2 * p2[0] + t**3 * p3[0],
            u**3 * p0[1] + 3 * u**2 * t * p1[1] + 3 * u * t**2 * p2[1] + t**3 * p3[1],
        ))
    return points


def segment_distance(x, y, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    length2 = dx * dx + dy * dy
    t = 0 if length2 == 0 else max(0, min(1, ((x-a[0])*dx + (y-a[1])*dy) / length2))
    return math.hypot(x - (a[0] + t * dx), y - (a[1] + t * dy))


def render(size):
    scale = 4
    high = size * scale
    radius = size * 0.226
    stroke = size * 0.09
    left_top = (size * 0.34, size * 0.29)
    left_bottom = (size * 0.34, size * 0.57)
    curve1 = bezier(left_bottom, (size * 0.34, size * 0.70), (size * 0.40, size * 0.75), (size * 0.50, size * 0.75))
    curve2 = bezier((size * 0.50, size * 0.75), (size * 0.60, size * 0.75), (size * 0.66, size * 0.70), (size * 0.66, size * 0.57))
    right_bottom = (size * 0.66, size * 0.57)
    right_top = (size * 0.66, size * 0.29)
    path = [left_top, left_bottom] + curve1[1:] + curve2[1:] + [right_top]
    dot = (size * 0.742, size * 0.254)
    dot_radius = size * 0.035
    start = (31, 42, 36)
    end = (23, 32, 27)
    white = (237, 244, 239)
    mint = (168, 201, 180)
    pixels = bytearray(high * high * 4)

    for hy in range(high):
        y = (hy + 0.5) / scale
        for hx in range(high):
            x = (hx + 0.5) / scale
            nearest_x = min(max(x, radius), size - radius)
            nearest_y = min(max(y, radius), size - radius)
            inside = (x-nearest_x)**2 + (y-nearest_y)**2 <= radius**2
            if inside:
                mix = max(0, min(1, 0.57 * x / size + 0.43 * y / size))
                color = tuple(round(start[c] * (1-mix) + end[c] * mix) for c in range(3))
                alpha = 255
            else:
                color, alpha = (0, 0, 0), 0

            distance = min(segment_distance(x, y, a, b) for a, b in zip(path, path[1:]))
            if distance <= stroke / 2:
                color, alpha = white, 255
            if math.hypot(x-dot[0], y-dot[1]) <= dot_radius:
                color, alpha = mint, 255

            i = (hy * high + hx) * 4
            pixels[i:i+4] = bytes((*color, alpha))

    output = bytearray(size * size * 4)
    for y in range(size):
        for x in range(size):
            accum = [0, 0, 0]
            alpha_total = 0
            for sy in range(scale):
                for sx in range(scale):
                    i = (((y*scale+sy)*high) + x*scale+sx) * 4
                    a = pixels[i+3]
                    alpha_total += a
                    for c in range(3):
                        accum[c] += pixels[i+c] * a
            out_alpha = round(alpha_total / (scale * scale))
            out = (y * size + x) * 4
            for c in range(3):
                output[out+c] = 0 if alpha_total == 0 else round(accum[c] / alpha_total)
            output[out+3] = out_alpha
    return output


def dib(size, rgba):
    xor = bytearray()
    for y in range(size-1, -1, -1):
        for x in range(size):
            i = (y * size + x) * 4
            r, g, b, a = rgba[i:i+4]
            xor.extend((b, g, r, a))
    mask_stride = ((size + 31) // 32) * 4
    mask = bytes(mask_stride * size)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(xor) + len(mask), 0, 0, 0, 0)
    return header + xor + mask


def main():
    images = [(size, dib(size, render(size))) for size in SIZES]
    directory_size = 6 + len(images) * 16
    entries = bytearray()
    payload = bytearray()
    offset = directory_size
    for size, image in images:
        dimension = 0 if size == 256 else size
        entries.extend(struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(image), offset))
        payload.extend(image)
        offset += len(image)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(struct.pack("<HHH", 0, 1, len(images)) + entries + payload)
    print(f"Created {OUTPUT} ({OUTPUT.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
