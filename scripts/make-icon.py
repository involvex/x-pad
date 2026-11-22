#!/usr/bin/env python3
"""Generate X-Pad app icon with true alpha (no white halo) + multi-size ICO."""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"
PNG_OUT = ASSETS / "xpad-icon-512.png"
ICO_OUT = ASSETS / "xpad.ico"

# Brand colors
BG = (30, 30, 30, 255)          # #1E1E1E chrome
ACCENT = (0, 122, 204, 255)     # #007ACC
X_COLOR = (245, 245, 245, 255)  # near-white
EDGE = (55, 55, 58, 255)        # subtle rim so dark taskbars still read a shape


def rounded_rect_mask(size: int, radius: float) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def draw_x(draw: ImageDraw.ImageDraw, size: int, stroke: float, color: tuple[int, int, int, int]) -> None:
    # Keep the glyph in the upper-middle so the accent bar has clear air below.
    pad_x = size * 0.27
    top = size * 0.20
    bottom = size * 0.68
    x0, y0 = pad_x, top
    x1, y1 = size - pad_x, bottom
    half = stroke / 2

    def thick_line(ax: float, ay: float, bx: float, by: float) -> None:
        dx, dy = bx - ax, by - ay
        length = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / length * half, dx / length * half
        draw.polygon(
            [
                (ax + nx, ay + ny),
                (ax - nx, ay - ny),
                (bx - nx, by - ny),
                (bx + nx, by + ny),
            ],
            fill=color,
        )

    thick_line(x0, y0, x1, y1)
    thick_line(x1, y0, x0, y1)


def draw_underscore(draw: ImageDraw.ImageDraw, size: int, color: tuple[int, int, int, int]) -> None:
    # Clear gap under the X; reads as a caret/underscore at 16–32px.
    y = size * 0.80
    half_w = size * 0.20
    h = max(2.0, size * 0.06)
    cx = size / 2
    draw.rounded_rectangle(
        (cx - half_w, y - h / 2, cx + half_w, y + h / 2),
        radius=h,
        fill=color,
    )


def render(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    radius = size * 0.22
    mask = rounded_rect_mask(size, radius)

    body = Image.new("RGBA", (size, size), BG)
    # Subtle edge stroke inside the rounded rect for taskbar contrast
    edge = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    edge_draw = ImageDraw.Draw(edge)
    inset = max(1, size // 64)
    edge_draw.rounded_rectangle(
        (inset, inset, size - 1 - inset, size - 1 - inset),
        radius=max(1, radius - inset),
        outline=EDGE,
        width=max(1, size // 48),
    )

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(body, (0, 0))
    canvas.alpha_composite(edge)
    canvas.putalpha(mask)

    draw = ImageDraw.Draw(canvas)
    stroke = max(2.0, size * 0.11)
    draw_x(draw, size, stroke, X_COLOR)
    draw_underscore(draw, size, ACCENT)

    # Re-apply mask so anti-aliased corners stay transparent (no white fringe)
    alpha = canvas.split()[-1]
    alpha = Image.composite(alpha, Image.new("L", (size, size), 0), mask)
    canvas.putalpha(alpha)
    return canvas


def write_ico(images: list[Image.Image], path: Path) -> None:
    # Pillow writes a proper multi-resolution ICO with alpha.
    largest = images[0]
    largest.save(
        path,
        format="ICO",
        sizes=[(im.width, im.height) for im in images],
        append_images=images[1:],
    )


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    master = render(512)
    master.save(PNG_OUT, "PNG")

    sizes = [256, 128, 64, 48, 32, 24, 16]
    images = [render(s) for s in sizes]
    # Also include a high-quality downscale of master for 256 as primary
    write_ico(images, ICO_OUT)
    print(f"Wrote {PNG_OUT}")
    print(f"Wrote {ICO_OUT} sizes={sizes}")


if __name__ == "__main__":
    main()
