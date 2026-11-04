"""Read-only font embedding/Unicode audit of actual test PDFs (requires pypdf)."""
import json
from pathlib import Path
from pypdf import PdfReader

root = Path(__file__).resolve().parent
results = []
for name in ("Light", "Isekai", "Light-guide", "Isekai-guide", "Light-engineering", "Isekai-engineering"):
    path = root / "artifacts" / (name + ".pdf")
    reader = PdfReader(path)
    fonts = {}
    for page in reader.pages:
        for ref in page.get("/Resources", {}).get_object().get("/Font", {}).get_object().values():
            font = ref.get_object()
            leaf = font.get("/DescendantFonts", [ref])[0].get_object()
            descriptor = leaf.get("/FontDescriptor", {}).get_object()
            key = str(font.get("/BaseFont", leaf.get("/BaseFont", descriptor.get("/FontName"))))
            outlines = font.get("/Subtype") == "/Type3" and bool(font.get("/CharProcs")) and all(
                len(proc.get_object().get_data()) > 0 for proc in font["/CharProcs"].values())
            fonts[key] = {"representation": "embedded Type3 glyph outlines" if outlines else "embedded font file",
                          "embedded": outlines or any(k in descriptor for k in ("/FontFile", "/FontFile2", "/FontFile3")),
                          "unicode": "/ToUnicode" in font}
    text = "\n".join(page.extract_text() for page in reader.pages)
    (path.with_suffix(".txt")).write_text(text, encoding="utf-8")
    missing = [] if "-" in name else [s for s in ("50.000", "0.5876", "0.012345", "字体管理", "波", "λ", "ρ") if s not in text]
    # Noto's U+957F and U+2ED3 share one glyph; Skia's ToUnicode chooses the radical.
    # Record this existing extraction alias instead of claiming exact Chinese round-trip.
    aliases = {"U+957F -> U+2ED3": text.count("\u2ed3")} if "\u2ed3" in text else {}
    results.append({"file": path.name, "pages": len(reader.pages), "fonts": fonts,
                    "missing_text": missing, "known_glyph_aliases": aliases, "text_characters": len(text)})
    assert fonts and all(v["embedded"] and v["unicode"] for v in fonts.values()), (name, fonts)
    assert not missing, (name, missing, text)
(root / "pdf-font-audit.json").write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
print(json.dumps(results, indent=2, ensure_ascii=False))
