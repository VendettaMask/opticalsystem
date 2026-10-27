"""Validation only. Product export is native C#; this script reads finished PDFs."""
from pathlib import Path
import json, re, hashlib, unicodedata
import pypdf
import pypdfium2 as pdfium
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
evidence = root / 'validation/rechecks/2026-10-27/user-guide/pdf-review'
evidence.mkdir(parents=True, exist_ok=True)
sources = '\n'.join((root / ('src/OpticalSystemDesign.App/Panels/' + name)).read_text(encoding='utf-8-sig')
                    for name in ['UserGuidePanel.Computation.cs', 'UserGuidePanel.Computation.Details.cs'])
chapters = re.findall(r'yield return Calculation\("([^"]+)", "([^"]+)", "([^"]+)", "([^"]+)"', sources)
assert len(chapters) == 27
reports = []
radical_aliases = {0x2ec6: ord('角')}
def comparable(text):
    return ''.join(unicodedata.normalize('NFKC', text).translate(radical_aliases).replace('·', '・').split())
for language, column in [('zh-CN', 1), ('en-US', 2), ('ja-JP', 3)]:
    path = root / 'output/pdf' / f'Optical-System-Design-Calculations-{language}.pdf'
    reader = pypdf.PdfReader(path)
    texts = [page.extract_text() or '' for page in reader.pages]
    compact = comparable(''.join(texts))
    missing = [chapter[column] for chapter in chapters if comparable(chapter[column]) not in compact]
    assert not missing, (language, missing)
    assert '\ufffd' not in compact
    report = dict(language=language, pages=len(reader.pages), chapters=27, missing_headings=missing,
                  sha256=hashlib.sha256(path.read_bytes()).hexdigest(), bytes=path.stat().st_size,
                  text_characters=len(compact), extraction_normalization='Unicode NFKC; same-glyph CJK horn radical and middle-dot aliases')
    reports.append(report)
    if language == 'zh-CN':
        document = pdfium.PdfDocument(str(path))
        previews = []
        for index in range(len(document)):
            page = document[index]
            bitmap = page.render(scale=1.25)
            image = bitmap.to_pil().convert('RGB')
            destination = evidence / f'page-{index + 1:03}.png'
            image.save(destination)
            thumb = image.copy(); thumb.thumbnail((300, 425))
            previews.append((index + 1, thumb))
            bitmap.close(); page.close()
        document.close()
        for batch in range(0, len(previews), 12):
            sheet = Image.new('RGB', (4 * 320, 3 * 460), '#dce1e8')
            draw = ImageDraw.Draw(sheet)
            for cell, (number, thumb) in enumerate(previews[batch:batch + 12]):
                x, y = (cell % 4) * 320 + 10, (cell // 4) * 460 + 25
                sheet.paste(thumb, (x, y)); draw.text((x, y - 18), f'Page {number}', fill='black')
            sheet.save(evidence / f'contact-{batch // 12 + 1}.png')
        report['chapter_pages'] = {chapter[0]: [i + 1 for i, text in enumerate(texts) if comparable(chapter[1]) in comparable(text)] for chapter in chapters}
json.dump(reports, (evidence / 'inspection.json').open('w', encoding='utf-8'), ensure_ascii=False, indent=2)
print(json.dumps([{k: v for k, v in r.items() if k != 'chapter_pages'} for r in reports], ensure_ascii=True, indent=2))
