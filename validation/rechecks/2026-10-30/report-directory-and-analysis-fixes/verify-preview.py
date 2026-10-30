from pathlib import Path
import sys, json, hashlib
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0,str(Path('tmp/report-formats-qa/inspection-deps').resolve()))
import pymupdf
from pypdf import PdfReader
from PIL import Image
base=Path('validation/rechecks/2026-10-30/report-directory-and-analysis-fixes')
pdf=next((base/'preview-final/Pdf').glob('*.pdf'))
r=PdfReader(pdf);d=pymupdf.open(pdf)
checks=[]
for pi,page in enumerate(r.pages):
 for ar in page.get('/Annots',[]):
  a=ar.get_object()
  if a.get('/Subtype')!='/Link':continue
  dest=r.named_destinations[a['/Dest']]
  actual=r.get_destination_page_number(dest)+1
  x0,y0,x1,y1=map(float,a['/Rect']); h=float(page.mediabox.height)
  label=d[pi].get_text('text',clip=pymupdf.Rect(x1-40,h-y1,x1+1,h-y0)).strip()
  assert label==str(actual),(label,actual)
  checks.append({'directory_page':pi+1,'destination':str(a['/Dest']),'actual_page':actual,'printed_page':label})
source={}
for p in (base/'preview-final/source-images/Pdf').glob('*.png'):
 im=Image.open(p).convert('RGB');assert im.size==(2404,1804)
 source[hashlib.sha256(im.tobytes()).hexdigest()]=p.name
placements=[]
for i,page in enumerate(d):
 for img in page.get_images(full=True):
  data=d.extract_image(img[0]); import io
  im=Image.open(io.BytesIO(data['image'])).convert('RGB')
  digest=hashlib.sha256(im.tobytes()).hexdigest()
  assert digest in source
  for rect in page.get_image_rects(img[0]):
   assert abs(rect.width-523.28)<.03 and abs(rect.height-392.6776)<.03
   placements.append({'page':i+1,'source':source[digest],'pixels_unchanged':True,'frame_points':[rect.width,rect.height]})
assert len(checks)==7 and len(placements)==5
out={'pdf':str(pdf),'pages':len(d),'toc_links':checks,'figures':placements,'source_sha256':hashlib.sha256(Path('src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx').read_bytes()).hexdigest()}
(base/'preview-final/inspection.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(out,ensure_ascii=False,indent=2))

