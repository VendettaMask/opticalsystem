"""Render every page and verify actual PDF geometry and lossless image embedding."""
import hashlib
import io
import json
import logging
import subprocess
import sys
from collections import Counter
from pathlib import Path

import pdfplumber
from PIL import Image, ImageDraw

logging.getLogger("pdfminer").setLevel(logging.ERROR)
sys.stdout.reconfigure(encoding="utf-8")
root = Path(sys.argv[1]).resolve()
qa = Path(sys.argv[2]).resolve()
poppler = Path(sys.argv[3]).resolve()
qa.mkdir(parents=True, exist_ok=True)
generation = json.loads((root / "generation.json").read_text(encoding="utf-8-sig"))
pdf = Path(generation["Results"][0]["ReportPath"])
assert generation["Results"][0]["Succeeded"] == 8 and generation["Results"][0]["Failed"] == 0
assert generation["SourceUnchanged"]
assert hashlib.sha256(Path(generation["Source"]).read_bytes()).hexdigest() == generation["Sha256"]
assert b"/DCTDecode" not in pdf.read_bytes()
subprocess.run([str(poppler), "-r", "130", "-png", str(pdf), str(qa / "page")],
               check=True, capture_output=True)
pages = []
frames = []
with pdfplumber.open(pdf) as document:
    for number, page in enumerate(document.pages, 1):
        text = page.extract_text() or ""
        outside = [char for char in page.chars if char["x0"] < -.5 or char["x1"] > page.width + .5
                   or char["top"] < -.5 or char["bottom"] > page.height + .5]
        assert not outside, number
        images = []
        for image in page.images:
            assert page.width < page.height
            assert tuple(image["srcsize"]) == (2404, 1804)
            assert abs(image["width"] - 523.28) < .02
            assert abs(image["height"] - 392.6776) < .02
            assert abs(image["x0"] - 36) < .02
            assert abs(image["top"] - 140) < .02
            frame = {key: image[key] for key in ["x0", "top", "width", "height", "srcsize"]}
            frame["width_mm"] = image["width"] * 25.4 / 72
            frame["height_mm"] = image["height"] * 25.4 / 72
            images.append(frame)
            frames.append(frame)
        (qa / f"page-{number:02}.txt").write_text(text, encoding="utf-8")
        pages.append({"page": number, "width": page.width, "height": page.height,
                      "text": text, "outside": len(outside), "images": images})
assert len(pages) == 9 and len(frames) == 7
all_text = "\n".join(page["text"] for page in pages)
for excluded in ["完整处方", "请求指纹", "计算记录", "工程修订", "D:\\optical", "C:\\Users"]:
    assert excluded not in all_text, excluded

# The installed PDF-inspection dependency is separate from product/runtime code.
sys.path.insert(0, str(Path("tmp/report-formats-qa/inspection-deps").resolve()))
import pymupdf


def pixels(image):
    return hashlib.sha256(image.convert("RGB").tobytes()).hexdigest()


sources = sorted((root / "source-images" / "Pdf").glob("*.png"))
assert len(sources) == 7
expected = []
for file in sources:
    with Image.open(file) as image:
        assert image.size == (2404, 1804)
        expected.append(pixels(image))
embedded = []
with pymupdf.open(pdf) as document:
    for page in document:
        for image in page.get_images(full=True):
            with Image.open(io.BytesIO(document.extract_image(image[0])["image"])) as bitmap:
                embedded.append(pixels(bitmap))
assert Counter(embedded) == Counter(expected)

files = sorted(qa.glob("page-*.png"))
sheet = Image.new("RGB", (1200, 1800), "#e6e9ed")
draw = ImageDraw.Draw(sheet)
for index, file in enumerate(files):
    with Image.open(file) as original:
        preview = original.convert("RGB")
    preview.thumbnail((380, 555))
    x, y = index % 3 * 400, index // 3 * 600
    sheet.paste(preview, (x + (400 - preview.width) // 2, y + 28))
    draw.text((x + 12, y + 8), f"Page {index + 1}", fill="black")
sheet.save(qa / "contact.png")
summary = {"pdf": str(pdf), "sha256": hashlib.sha256(pdf.read_bytes()).hexdigest(),
           "page_count": len(pages), "image_count": len(frames), "image_pixels_match_sources": len(embedded),
           "source_unchanged": True, "source_sha256": generation["Sha256"],
           "uniform_width_mm": frames[0]["width_mm"], "uniform_height_mm": frames[0]["height_mm"],
           "outside": 0, "pages": pages}
(qa / "inspection.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: value for key, value in summary.items() if key != "pages"}, ensure_ascii=False))
