#!/usr/bin/env python3
"""Apply a transparent rounded mask and rebuild desktop icon containers."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


ICON_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
MASK_SCALE = 4
CORNER_RADIUS = 196
MASK_INSET = 4
CONTENT_INSET = 56


def rounded_icon(source: Path) -> Image.Image:
    artwork = Image.open(source).convert("RGBA")
    if artwork.size != (1024, 1024):
        raise ValueError(f"Expected a 1024 x 1024 source icon, received {artwork.size}")

    mask = Image.new("L", (1024 * MASK_SCALE, 1024 * MASK_SCALE), 0)
    draw = ImageDraw.Draw(mask)
    inset = MASK_INSET * MASK_SCALE
    draw.rounded_rectangle(
        (inset, inset, mask.width - inset - 1, mask.height - inset - 1),
        radius=CORNER_RADIUS * MASK_SCALE,
        fill=255,
    )
    mask = mask.resize(artwork.size, Image.Resampling.LANCZOS)
    artwork.putalpha(mask)

    content_size = 1024 - (CONTENT_INSET * 2)
    content = artwork.resize((content_size, content_size), Image.Resampling.LANCZOS)
    icon = Image.new("RGBA", artwork.size, (0, 0, 0, 0))
    icon.alpha_composite(content, (CONTENT_INSET, CONTENT_INSET))
    return icon


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "assets",
        type=Path,
        help="Directory containing AppIcon.png, AppIcon.ico, and AppIcon.icns",
    )
    args = parser.parse_args()

    source = args.assets / "AppIconArtwork.png"
    icon = rounded_icon(source)
    icon.save(args.assets / "AppIcon.png", optimize=True)
    icon.save(args.assets / "AppIcon.ico", format="ICO", sizes=ICON_SIZES)
    icon.save(args.assets / "AppIcon.icns", format="ICNS")
    print(f"Rounded platform icons generated from {source}")


if __name__ == "__main__":
    main()
