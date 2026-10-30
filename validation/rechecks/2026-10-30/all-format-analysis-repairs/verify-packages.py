from pathlib import Path
from io import BytesIO
from zipfile import ZipFile
import json, hashlib, re
from PIL import Image
from pypdf import PdfReader
base=Path('validation/rechecks/2026-10-30/all-format-analysis-repairs')
def pixels(data):
 im=Image.open(BytesIO(data)).convert('RGB')
 assert im.size==(2404,1804), im.size
 return hashlib.sha256(im.tobytes()).hexdigest()
results=[]
for fmt,ext in [('Word','.docx'),('Pdf','.pdf'),('Markdown','.md'),('Html','.html')]:
 root=base/'exports'/fmt
 report=next(root.glob('*'+ext)); assets=next(root.glob('*_assets'))
 originals={pixels(p.read_bytes()):p.name for p in assets.glob('*.png')}
 assert len(originals)==2
 images=[]
 if fmt=='Word':
  with ZipFile(report) as z:images=[z.read(n) for n in z.namelist() if n.startswith('word/media/')]
 elif fmt=='Pdf':
  reader=PdfReader(report)
  images=[im.data for p in reader.pages for im in p.images]
 elif fmt=='Markdown':images=[p.read_bytes() for p in assets.glob('*.png')]
 else:
  import base64
  images=[base64.b64decode(b) for b in re.findall(r'data:image/png;base64,([A-Za-z0-9+/=]+)',report.read_text(encoding='utf-8'))]
 hashes=[pixels(b) for b in images]
 assert len(hashes)==2 and set(hashes)==set(originals),(fmt,hashes)
 results.append({'format':fmt,'report':str(report),'source_size':[2404,1804],'complete_original_images':2,'embedded_or_linked_pixels_match_sources':True,'sha256':hashlib.sha256(report.read_bytes()).hexdigest()})
(base/'package-inspection.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(results,ensure_ascii=False,indent=2))
