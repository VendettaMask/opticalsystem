"""Read-only measurement of the two exposed strips in composed UI captures."""
import hashlib
import itertools
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent
OLD = ROOT.parent / "isekai-ribbon-details" / "workbench"
NEW = ROOT / "workbench"
FLAT = (244, 235, 221)  # Original result-container background, #F4EBDD.


def identity(path):
    return {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


measurements = []
for name in ("workbench-1600-13.png", "workbench-1120-18.png"):
    before_path, after_path = OLD / name, NEW / name
    before, after = (Image.open(p).convert("RGB") for p in (before_path, after_path))
    assert before.size == after.size
    rows = []
    for y in range(before.height):
        pixels = list(before.crop((0, y, before.width, y + 1)).getdata())
        xs = [x for x, pixel in enumerate(pixels) if pixel == FLAT]
        if len(xs) > before.width * .6:
            rows.append((y, min(xs), max(xs) + 1))
    bands = []
    for _, group in itertools.groupby(enumerate(rows), lambda entry: entry[1][0] - entry[0]):
        run = [row for _, row in group]
        if len(run) < 3:
            continue
        rect = (max(row[1] for row in run), run[0][0], min(row[2] for row in run), run[-1][0] + 1)
        old_pixels, new_pixels = (list(image.crop(rect).getdata()) for image in (before, after))
        old_flat = sum(pixel == FLAT for pixel in old_pixels)
        new_flat = sum(pixel == FLAT for pixel in new_pixels)
        colours = len(set(new_pixels))
        assert old_flat == len(old_pixels), (name, rect, "Baseline must be fully flat")
        assert new_flat < len(new_pixels) * .01, (name, rect, "Flat strip remains")
        assert colours > 100, (name, rect, "Parchment texture absent")
        bands.append({"rectPixels": rect, "areaPixels": len(old_pixels),
                      "beforeFlatPixels": old_flat, "afterFlatPixels": new_flat,
                      "afterDistinctRGB": colours})
    assert len(bands) == 2, (name, bands)
    measurements.append({"before": identity(before_path), "after": identity(after_path),
                         "sizePixels": before.size, "bands": bands})

result = {"flatRGB": FLAT, "captures": measurements,
          "scope": "Two composed high-DPI workbench captures; read-only pixels, no image edits."}
(ROOT / "pixel-measurements.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
