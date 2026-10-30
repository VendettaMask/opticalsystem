"""Replace only incomplete coefficient pages; retain every original figure page and image."""
import json
import re
import sys
import unicodedata
from pathlib import Path

sys.path.insert(0, str(Path("tmp/report-formats-qa/inspection-deps").resolve()))
import pymupdf

root, repair_root = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
generation = json.loads((root / "generation.json").read_text(encoding="utf-8-sig"))
repair = json.loads((repair_root / "repair.json").read_text(encoding="utf-8-sig"))
assert repair["SourceSha256"].lower() == generation["Sha256"]
base = pymupdf.open(generation["Result"]["ReportPath"])
tables = pymupdf.open(repair["Result"]["ReportPath"])


def groups(document):
    starts = []
    for index, page in enumerate(document):
        text = unicodedata.normalize("NFKC", page.get_text())
        marker = re.search(r"(?m)^(\d{2})\s*/\s*配置\s*1", text)
        if marker:
            starts.append((int(marker[1]), index))
    return {number: (start, starts[index + 1][1] - 1 if index + 1 < len(starts) else len(document) - 1)
            for index, (number, start) in enumerate(starts)}


original_sections, repaired_sections = groups(base), groups(tables)
assert len(original_sections) == 75 and len(repaired_sections) == 3
for number, coefficients in enumerate(repair["Coefficients"], 1):
    start, end = repaired_sections[number]
    text = unicodedata.normalize("NFKC", "\n".join(tables[index].get_text() for index in range(start, end + 1)))
    for label, value in coefficients["Rows"]:
        assert label in text and value in text, (coefficients["Key"], label, value)

replacements = {original_sections[number][0]: (number, repaired_sections[number - 63]) for number in [64, 65, 66]}
assert all(original_sections[number][0] == original_sections[number][1] for number in [64, 65, 66])
output = pymupdf.open()
for index in range(len(base)):
    if index not in replacements:
        output.insert_pdf(base, from_page=index, to_page=index)
        continue
    target_number, (start, end) = replacements[index]
    first = len(output)
    output.insert_pdf(tables, from_page=start, to_page=end)
    page = output[first]
    spans = [span for block in page.get_text("dict")["blocks"] if "lines" in block
             for line in block["lines"] for span in line["spans"]
             if 48 < span["origin"][1] < 70 and span["origin"][0] < 48]
    assert spans
    baseline = min(spans, key=lambda span: span["origin"][0])["origin"][1]
    # Update the two-digit section number only; preserve its existing slash and CJK label.
    page.add_redact_annot(pymupdf.Rect(35, baseline - 11, 47.3, baseline + 2), fill=(1, 1, 1))
    page.apply_redactions(images=0, graphics=0)
    page.insert_text((36, baseline), str(target_number), fontsize=9, fontname="helv", color=(54 / 255, 84 / 255, 113 / 255))

for number, page in enumerate(output, 1):
    cx, bottom = page.rect.width / 2, page.rect.height
    page.add_redact_annot(pymupdf.Rect(cx - 20, bottom - 34, cx + 20, bottom - 12), fill=(1, 1, 1))
    page.apply_redactions(images=0, graphics=0)
    text = str(number)
    width = pymupdf.get_text_length(text, fontname="helv", fontsize=8)
    page.insert_text((cx - width / 2, bottom - 22), text, fontsize=8, fontname="helv", color=(99 / 255, 113 / 255, 134 / 255))
output.set_metadata(base.metadata)
publication_dir = root / "publication"
publication_dir.mkdir(exist_ok=True)
file = publication_dir / "高斯28度全分析检查报告.pdf"
output.save(file, garbage=4, deflate=True)
generation["InitialReportPath"] = generation["Result"]["ReportPath"]
generation["Result"]["ReportPath"] = str(file)
generation["CoefficientTableRepair"] = {"report": repair["Result"]["ReportPath"], "terms": 111,
    "original_sections": [64, 65, 66], "source_sha256": repair["SourceSha256"],
    "assembly": "Only coefficient data pages replaced and page numbers updated; original figure contents not rearranged or rerendered."}
(root / "publication.json").write_text(json.dumps(generation, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"{len(output)} pages; all 111 coefficient terms verified; all original figure pages retained.")
