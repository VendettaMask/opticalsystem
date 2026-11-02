from pathlib import Path
from PIL import Image, ImageDraw
from lxml import etree, html
from urllib.parse import unquote
import base64, hashlib, io, json, re, sys, zipfile
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0,'D:/optical system design/opticalsystem/tmp/report-formats-qa/inspection-deps')
import pymupdf
root=Path('D:/optical system design/opticalsystem/output/report-style-gauss28-20261102')
qa=root/'qa';qa.mkdir(exist_ok=True)
reports=root/'Reports';stem='Double Gauss 28 degree field_full_report'
images={p.name:p.read_bytes() for p in (root/'capture/source-images').glob('*.png')}
digests={hashlib.sha256(data).hexdigest():name for name,data in images.items()}
pixels={}
for name,data in images.items():
    with Image.open(io.BytesIO(data)) as image:pixels[hashlib.sha256(image.convert('RGB').tobytes()).hexdigest()]=name

tree=html.fromstring((reports/(stem+'.html')).read_text(encoding='utf-8'))
pngs=tree.xpath('//section[contains(concat(" ",@class," ")," document-page ")]//figure//img')
for image in pngs:assert hashlib.sha256(base64.b64decode(image.attrib['src'].split(',',1)[1])).hexdigest() in digests
assert len(pngs)==63
assert len(tree.xpath('//section[contains(concat(" ",@class," ")," document-page ")]//td[@class="formula-cell"]'))==111
with zipfile.ZipFile(reports/(stem+'.docx')) as archive:
    assert len(archive.namelist())==len(set(archive.namelist())),'Duplicate OOXML ZIP entries'
    media=[name for name in archive.namelist() if re.fullmatch(r'word/media/image\d+\.png',name)]
    assert len(media)==62
    assert {hashlib.sha256(archive.read(name)).hexdigest() for name in media}==set(digests)
    ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main','m':'http://schemas.openxmlformats.org/officeDocument/2006/math','wp':'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'}
    document=etree.fromstring(archive.read('word/document.xml'))
    extents=document.xpath('//wp:extent',namespaces=ns)
    assert len(extents)==63
    for extent in extents:
        assert abs(int(extent.attrib['cx'])/12700-523.28)<.02
        assert abs(int(extent.attrib['cy'])/12700-523.28*1804/2404)<.02
    equations=len(document.xpath('//m:oMath',namespaces=ns));assert equations>=111
    assert len(document.xpath('//m:sSup',namespaces=ns))>0 and len(document.xpath('//m:rad',namespaces=ns))>0
    ids=document.xpath('//wp:docPr/@id',namespaces=ns);assert len(ids)==len(set(ids))
    assert archive.read('word/media/brand.svg').lstrip().startswith(b'<svg')
md=(reports/(stem+'.md')).read_text(encoding='utf-8')
links=re.findall(r'!\[[^\]]*\]\(<([^>]+)>\)',md);assert len(links)==63
for link in links:
    file=reports/unquote(link);assert file.is_file()
    if file.suffix=='.png':assert hashlib.sha256(file.read_bytes()).hexdigest() in digests
body=md.split('## 附录 完整参数与计算记录')[0]
assert len(re.findall(r'^\| Z\d+ \|',body,re.M))==111
pdf=pymupdf.open(reports/(stem+'.pdf'))
placements=[];checked={};thumbs=[];text=''
for number,page in enumerate(pdf,1):
    text+=page.get_text()
    assert abs(page.rect.width-595.28)<1 and abs(page.rect.height-841.89)<1
    for image in page.get_image_info(xrefs=True):
        if image['width']<2000:continue
        assert (image['width'],image['height'])==(2404,1804)
        xref=image['xref']
        if xref not in checked:
            pix=pymupdf.Pixmap(pdf,xref)
            if pix.alpha:pix=pymupdf.Pixmap(pix,0)
            if pix.n!=3:pix=pymupdf.Pixmap(pymupdf.csRGB,pix)
            digest=hashlib.sha256(pix.samples).hexdigest();assert digest in pixels,('PDF altered original pixels',number)
            checked[xref]=pixels[digest]
        rect=pymupdf.Rect(image['bbox']);assert abs(rect.width-523.28)<.02
        assert abs(rect.height-523.28*1804/2404)<.02
        assert rect.x0>=35.9 and rect.x1<=page.rect.width-35.9 and rect.y1<page.rect.height-40
        placements.append({'page':number,'source':checked[xref],'width_pt':rect.width,'height_pt':rect.height})
    pix=page.get_pixmap(matrix=pymupdf.Matrix(.45,.45),alpha=False)
    thumbs.append(Image.frombytes('RGB',[pix.width,pix.height],pix.samples))
    if number in [1,2,5,10,69,70,72,75,78,80]:page.get_pixmap(matrix=pymupdf.Matrix(1.35,1.35),alpha=False).save(qa/f'pdf-page-{number:02d}.png')
assert len(placements)==63
assert len(re.findall(r'Z\s*3\s*7',text))>=3
for start in range(0,len(thumbs),20):
    canvas=Image.new('RGB',(1144,2130),'#e2e7ec');draw=ImageDraw.Draw(canvas)
    for j,thumb in enumerate(thumbs[start:start+20]):
        x=(j%4)*286+9;y=(j//4)*426+20;canvas.paste(thumb,(x,y));draw.text((x,y-16),f'PAGE {start+j+1:02d}',fill='#293f50')
    canvas.save(qa/f'pdf-contact-{start//20+1:02d}.png')
result={'status':'passed','selected_items':69,'source_pngs':len(images),'html_pages':len(tree.xpath('//section[contains(concat(" ",@class," ")," document-page ")]')),'pdf_pages':len(pdf),'formulas_per_family':37,'body_formulas':111,'word_omml_equations':equations,'html_pngs':len(pngs),'word_original_pngs':len(media),'markdown_links':len(links),'pdf_original_pngs':len(placements),'all_original_pixels':'identical','figure_size_points':[523.28,523.28*1804/2404],'placements':placements}
(qa/'artifact-inspection.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k!='placements'},ensure_ascii=False))
