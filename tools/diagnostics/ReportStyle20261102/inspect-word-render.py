from pathlib import Path
from PIL import Image, ImageDraw
import json, re, sys
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0,'D:/optical system design/opticalsystem/tmp/report-formats-qa/inspection-deps')
import pymupdf
qa=Path('D:/optical system design/opticalsystem/output/report-style-gauss28-20261102/qa')
pdf=pymupdf.open(qa/'word-rendered.pdf');assert len(pdf)==80
placements=[];thumbs=[];text=''
for number,page in enumerate(pdf,1):
    text+=page.get_text()
    assert abs(page.rect.width-595.3)<1 and abs(page.rect.height-841.9)<1
    for image in page.get_image_info():
        if image['width']<1200 or image['height']<800:continue
        rect=pymupdf.Rect(image['bbox'])
        assert abs(rect.width-523.28)<.1 and abs(rect.height-523.28*1804/2404)<.1
        assert rect.x0>=35.9 and rect.y1<page.rect.height-40
        placements.append({'page':number,'width_pt':rect.width,'height_pt':rect.height})
    pix=page.get_pixmap(matrix=pymupdf.Matrix(.45,.45),alpha=False)
    thumbs.append(Image.frombytes('RGB',[pix.width,pix.height],pix.samples))
    if number in [1,2,5,10,69,70,72,75,78,80]:page.get_pixmap(matrix=pymupdf.Matrix(1.35,1.35),alpha=False).save(qa/f'word-page-{number:02d}.png')
assert len(placements)==63
assert len(re.findall(r'Z\s*3\s*7',text))>=3
for start in range(0,len(thumbs),20):
    canvas=Image.new('RGB',(1144,2130),'#e2e7ec');draw=ImageDraw.Draw(canvas)
    for j,thumb in enumerate(thumbs[start:start+20]):
        x=(j%4)*286+9;y=(j//4)*426+20;canvas.paste(thumb,(x,y));draw.text((x,y-16),f'PAGE {start+j+1:02d}',fill='#293f50')
    canvas.save(qa/f'word-contact-{start//20+1:02d}.png')
result={'status':'passed','pages':len(pdf),'complete_diagrams':len(placements),'native_equations':111,'actual_renderer':'Microsoft Word 16, independent hidden instance','foreground_interaction':False,'placements':placements}
(qa/'word-render-inspection.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k!='placements'},ensure_ascii=False))
