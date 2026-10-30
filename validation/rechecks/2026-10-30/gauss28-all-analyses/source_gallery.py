"""QA thumbnails only; original exported PNG files are never changed."""
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw

root, output = Path(sys.argv[1]), Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)
selection = json.loads((root / "selection.json").read_text(encoding="utf-8-sig"))
files = sorted((root / "source-images").glob("*.png"))
for group in range((len(files) + 8) // 9):
    sheet = Image.new("RGB", (1200, 990), "#e6e9ed")
    draw = ImageDraw.Draw(sheet)
    for index, file in enumerate(files[group * 9:group * 9 + 9]):
        match = re.search(r"analysis-(\d+)", file.stem)
        label = selection[int(match[1]) - 1]["Key"] if match else file.stem
        with Image.open(file) as source:
            assert source.size == (2404, 1804), file.name
            preview = source.convert("RGB")
        preview.thumbnail((380, 285))
        x, y = index % 3 * 400, index // 3 * 330
        sheet.paste(preview, (x + (400 - preview.width) // 2, y + 28))
        draw.text((x + 12, y + 8), label[:55], fill="black")
    sheet.save(output / f"source-contact-{group + 1:02}.png")
print(f"{len(files)} original images checked; QA thumbnails saved.")
