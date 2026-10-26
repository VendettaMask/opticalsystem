"""Offline artwork authoring; the font is never a product/build dependency.

Use an authorized local Nasalization Regular OTF. Only the complete, fixed
wordmark is exported, as one path without live text, font data, or bitmaps.
"""

import argparse
import hashlib
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont


def build(font_path: Path, output: Path) -> None:
    with TTFont(font_path) as font:
        if "Nasalization" not in font["name"].getDebugName(1):
            raise ValueError("The artwork requires Nasalization, not a substitute font.")
        glyphs = font.getGlyphSet()
        cmap = font.getBestCmap()
        wordmark = "S.T.A.R.Labs"
        names = [cmap[ord(character)] for character in wordmark]
        kern = {}
        if "kern" in font:
            for table in font["kern"].kernTables:
                kern.update(table.kernTable)
        positions = []
        cursor = 0
        previous = None
        bounds = BoundsPen(glyphs)
        for name in names:
            cursor += kern.get((previous, name), 0)
            positions.append(cursor)
            glyphs[name].draw(TransformPen(bounds, (1, 0, 0, 1, cursor, 0)))
            cursor += font["hmtx"][name][0]
            previous = name
        left, bottom, right, top = bounds.bounds
        scale = 100 / (top - bottom)
        # Retain the existing approved artwork's wide 11.79:1 composition.
        # This transforms real font contours; it never traces or embeds pixels.
        horizontal_scale = 1179 / (right - left)
        padding = 2
        path = SVGPathPen(glyphs, ntos=lambda value: format(value, ".5f").rstrip("0").rstrip(".") or "0")
        for name, x in zip(names, positions):
            glyphs[name].draw(TransformPen(path, (
                horizontal_scale, 0, 0, -scale, (x - left) * horizontal_scale + padding, top * scale + padding)))
        width = 1179 + 2 * padding
        height = 100 + 2 * padding
        svg = (
            '<svg xmlns="http://www.w3.org/2000/svg" '
            f'viewBox="0 0 {width:.5f} {height}" role="img" aria-labelledby="title">\n'
            '  <title id="title">S.T.A.R.Labs</title>\n'
            '  <desc>Fixed wordmark outlined from Nasalization Regular. '
            'No embedded bitmap, live text, or font software.</desc>\n'
            f'  <path fill="#0B1F3F" d="{path.getCommands()}"/>\n'
            '</svg>\n'
        )
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(svg, encoding="utf-8", newline="\n")
        print(f"Font SHA-256: {hashlib.sha256(font_path.read_bytes()).hexdigest()}")
        print(f"Outlined SVG: {width:.5f} x {height}; {output.stat().st_size} bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--font", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=Path(
        "src/OpticalSystemDesign.App/Assets/Brand/CompanyLogo.svg"))
    arguments = parser.parse_args()
    build(arguments.font, arguments.output)
