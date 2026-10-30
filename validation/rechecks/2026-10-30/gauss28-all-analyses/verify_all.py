"""Check the all-selection report, keep a complete outcome ledger and render every page."""
import csv
import hashlib
import io
import json
import logging
import re
import subprocess
import sys
import unicodedata
from collections import Counter
from pathlib import Path

import pdfplumber
from PIL import Image, ImageDraw

logging.getLogger("pdfminer").setLevel(logging.ERROR)
sys.stdout.reconfigure(encoding="utf-8")
root = Path(sys.argv[1]).resolve()
qa = Path(sys.argv[2]).resolve()
qa.mkdir(parents=True, exist_ok=True)
generation = json.loads((root / ("publication.json" if (root / "publication.json").exists() else "generation.json")).read_text(encoding="utf-8-sig"))
selection = json.loads((root / "selection.json").read_text(encoding="utf-8-sig"))
pdf = Path(generation["Result"]["ReportPath"])
assert generation["SelectedCount"] == len(selection) == 73
assert generation["Result"]["Succeeded"] + generation["Result"]["Failed"] == 75
assert generation["SourceUnchanged"]
assert hashlib.sha256(Path(generation["Source"]).read_bytes()).hexdigest() == generation["Sha256"]
assert b"/DCTDecode" not in pdf.read_bytes()
subprocess.run([sys.argv[3], "-r", "95", "-png", str(pdf), str(qa / "page")],
               check=True, capture_output=True)
pages, frames, outcomes, sections = [], [], [], []
section_started = False
with pdfplumber.open(pdf) as document:
    for number, page in enumerate(document.pages, 1):
        text = unicodedata.normalize("NFKC", page.extract_text() or "")
        marker = re.search(r"(?m)^(\d{2,})\s*/\s*配置\s*(\d+)", text)
        if marker:
            section_started = True
            sections.append({"number": int(marker[1]), "page": number, "text": text})
        elif section_started and sections:
            sections[-1]["text"] += "\n" + text
        if not section_started:
            # The first overview's metrics/grid share vertical edges; table-edge
            # inference drops its first column, while the actual text retains every row.
            for line in text.splitlines():
                match = re.fullmatch(r"1\s+(.+?)\s+(未完成|完成)", line)
                if match:
                    outcomes.append({"title": match[1], "status": match[2]})
        outside = [char for char in page.chars if char["x0"] < -.5 or char["x1"] > page.width + .5
                   or char["top"] < -.5 or char["bottom"] > page.height + .5]
        assert not outside, (number, len(outside))
        images = []
        for image in page.images:
            assert page.width < page.height, number
            assert tuple(image["srcsize"]) == (2404, 1804)
            assert abs(image["width"] - 523.28) < .02
            assert abs(image["height"] - 392.6776) < .02
            assert image["top"] >= 139.98 and image["bottom"] < page.height - 40
            frame = {key: image[key] for key in ["x0", "top", "width", "height", "srcsize"]}
            images.append(frame)
            frames.append(frame)
        pages.append({"page": number, "width": page.width, "height": page.height,
                      "text": text, "outside": 0, "images": images})
        (qa / f"text-{number:03}.txt").write_text(text, encoding="utf-8")
assert len(outcomes) == len(sections) == 75, (len(outcomes), len(sections))
assert [section["number"] for section in sections] == list(range(1, 76))
assert sum(row["status"] == "完成" for row in outcomes) == generation["Result"]["Succeeded"]
assert sum(row["status"] != "完成" for row in outcomes) == generation["Result"]["Failed"]
for row, section in zip(outcomes, sections):
    row["page"] = section["page"]
    failure = re.search(r"未完成[：:](.*)", section["text"], re.S)
    row["reason"] = re.sub(r"\n\d+\s*$", "", failure[1]).strip() if row["status"] != "完成" and failure else ""
coefficient_terms_verified = 0
if "CoefficientTableRepair" in generation:
    repair = json.loads((Path(generation["CoefficientTableRepair"]["report"]).parent / "repair.json").read_text(encoding="utf-8-sig"))
    for number, item in zip([64, 65, 66], repair["Coefficients"]):
        text = sections[number - 1]["text"]
        for label, value in item["Rows"]:
            assert label in text and value in text, (number, label, value)
            coefficient_terms_verified += 1
    assert coefficient_terms_verified == 111
with (qa / "分析结果清单.csv").open("w", encoding="utf-8-sig", newline="") as stream:
    writer = csv.writer(stream)
    writer.writerow(["分析项目", "执行状态", "PDF页码", "未完成原因", "图面检查备注"])
    for row in outcomes:
        visual_note = "默认多子图的轴标拥挤、部分图框过窄，属于源图问题，待优化。" if row["title"].replace(" ", "") in ["辐射强度", "光源分析"] else ""
        writer.writerow([row["title"], row["status"], row["page"], row["reason"], visual_note])
all_text = "\n".join(page["text"] for page in pages)
for excluded in ["完整处方", "请求指纹", "工程修订", "D:\\optical", "C:\\Users"]:
    assert excluded not in all_text, excluded

sys.path.insert(0, str(Path("tmp/report-formats-qa/inspection-deps").resolve()))
import pymupdf


def pixels(image):
    return hashlib.sha256(image.convert("RGB").tobytes()).hexdigest()


expected = []
for file in (root / "source-images").glob("*.png"):
    with Image.open(file) as image:
        assert image.size == (2404, 1804)
        expected.append(pixels(image))
actual = []
with pymupdf.open(pdf) as document:
    for page in document:
        for image in page.get_images(full=True):
            with Image.open(io.BytesIO(document.extract_image(image[0])["image"])) as bitmap:
                actual.append(pixels(bitmap))
assert Counter(actual) == Counter(expected)
assert len(frames) == len(actual) == len(generation["Images"])

files = sorted(qa.glob("page-*.png"))
for group in range((len(files) + 8) // 9):
    sheet = Image.new("RGB", (1200, 1800), "#e6e9ed")
    draw = ImageDraw.Draw(sheet)
    for index, file in enumerate(files[group * 9:group * 9 + 9]):
        with Image.open(file) as original:
            preview = original.convert("RGB")
        preview.thumbnail((380, 555))
        x, y = index % 3 * 400, index // 3 * 600
        sheet.paste(preview, (x + (400 - preview.width) // 2, y + 28))
        draw.text((x + 12, y + 8), f"Page {group * 9 + index + 1}", fill="black")
    sheet.save(qa / f"contact-{group + 1:02}.png")
summary = {"pdf": str(pdf), "sha256": hashlib.sha256(pdf.read_bytes()).hexdigest(),
           "selected_analyses": 73, "items": 75, "succeeded": generation["Result"]["Succeeded"],
           "failed": generation["Result"]["Failed"], "pages": len(pages), "images": len(frames),
           "uniform_frame_pt": [523.28, 392.6776], "image_pixels_match_sources": len(actual),
           "coefficient_terms_verified": coefficient_terms_verified,
           "source_unchanged": True, "source_sha256": generation["Sha256"], "outside": 0,
           "source_visual_findings": [{"analysis": "Radiant Intensity / Light Source Analysis", "finding": "Crowded axes and narrow subplots in the original multi-pane drawings; retained unchanged for this diagnostic report, not certified as visually accepted."},
                                      {"analysis": "PDF overview", "finding": "The full-selection overview note flows onto an otherwise empty page 4. This is a diagnostic output with a known pagination defect, not publication-ready layout acceptance."}],
           "outcomes": outcomes, "page_details": pages}
(qa / "inspection.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: value for key, value in summary.items() if key not in ["outcomes", "page_details"]}, ensure_ascii=False))
print(json.dumps([row for row in outcomes if row["status"] != "完成"], ensure_ascii=False, indent=2))
