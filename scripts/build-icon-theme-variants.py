"""Extend the approved HUD / sword-and-magic studies across the actual semantic icon set."""
from pathlib import Path
from copy import deepcopy
import hashlib,html,json,math,re,xml.etree.ElementTree as ET

REPO=Path(__file__).resolve().parents[1]
ASSETS=REPO/'src/OpticalSystemDesign.App/Assets/Icons/Scientific'
PROPOSAL=REPO/'scripts/icon-theme-studies'
META=json.loads((ASSETS/'Designed/manifest.json').read_text(encoding='utf-8-sig'))
N='{http://www.w3.org/2000/svg}';ET.register_namespace('',N[1:-1])
PALS={'Lumen':dict(structure='#6C8D9D',signal='#78DCFF',secondary='#C4F5FF',fill='#D6AC6B',hot='#E2FBFF'),
      'Isekai':dict(structure='#A18B5F',signal='#EACC8B',secondary='#A6B48B',fill='#A6B48B',hot='#EACC8B')}
BASE_HASH=hashlib.sha256()
def p(d,role='structure',opacity=None,dash=None):
    e=ET.Element(N+'path',{'class':role,'d':d})
    if opacity:e.set('opacity',str(opacity))
    if dash:e.set('stroke-dasharray',dash)
    return e
def canon(s):return re.sub(r'\s+','',s)
def rect_path(e,cut):
    x,y,w,h=[float(e.get(k,'0')) for k in ('x','y','width','height')];k=min(cut,w/4,h/4)
    f=lambda n:f'{n:g}'
    result=p(f'M{f(x+k)} {f(y)}H{f(x+w-k)}L{f(x+w)} {f(y+k)}V{f(y+h-k)}L{f(x+w-k)} {f(y+h)}H{f(x+k)}L{f(x)} {f(y+h-k)}V{f(y+k)}Z',e.get('class','structure'))
    if e.get('opacity'):result.set('opacity',e.get('opacity'))
    return result
def circle_arcs(e):
    x,y,rad=[float(e.get(k)) for k in ('cx','cy','r')];parts=[]
    for start in (4,94,184,274):
        end=start+72;a=math.radians(start);b=math.radians(end)
        parts.append(f'M{x+rad*math.cos(a):.5f} {y+rad*math.sin(a):.5f}A{rad} {rad} 0 0 1 {x+rad*math.cos(b):.5f} {y+rad*math.sin(b):.5f}')
    result=p(' '.join(parts),e.get('class','structure'))
    if e.get('opacity'):result.set('opacity',e.get('opacity'))
    return result
AXES={canon('M4.5 5.5v22h24'):'M4.5 5.5v19l3 3h21M10 27.5v-2M18 27.5v-2M26 27.5v-2',
      canon('M4.5 12v15.5h24'):'M4.5 12v12.5l3 3h21M10 27.5v-2M18 27.5v-2M26 27.5v-2'}
PAGE=canon('M19 3.5H7a2 2 0 0 0-2 2v21a2 2 0 0 0 2 2h18a2 2 0 0 0 2-2V11L19 3.5Z M19 3.5V11h8')
SCROLL='M8 6h16c5 0 5 6 0 6h-2M8 6c-5 0-5 6 0 6h2M8 9v16c0 4 17 4 17 0V11'
INSTRUMENT=('mtf-','psf-','rms-','encircled-','aberration-','wavefront-','alignment-','optical-','system-aperture','ray-','material-','action-search','action-zoom')

def themed_group(base,id_,theme):
    group=deepcopy(base)
    # Semantic component changes: cut instrument panels, segmented HUD rings, and bound ledgers.
    for parent in list(group.iter()):
        for index,element in list(enumerate(list(parent))):
            tag=element.tag.split('}')[-1];role=element.get('class','')
            replacement=None
            if role=='structure' and tag=='rect' and float(element.get('width','0'))>=9 and float(element.get('height','0'))>=8:
                replacement=rect_path(element,2 if theme=='Lumen' else 1.5)
            elif theme=='Lumen' and role=='structure' and tag=='circle' and float(element.get('r','0'))>=5:
                replacement=circle_arcs(element)
            elif role=='structure' and tag=='path':
                d=canon(element.get('d',''))
                if d in AXES:
                    replacement=p(AXES[d])
                    if theme=='Isekai':
                        # Etched scale remains attached to the plotted axes, not an unrelated badge.
                        replacement=p(element.get('d')+'M2.5 12h2M2.5 19h2M11 27.5v-2M20 27.5v-2')
                elif d==PAGE:
                    replacement=p('M5 10V7l3-3h13l7 7v14l-3 3H8l-3-3V17M21 4v7h7') if theme=='Lumen' else p(SCROLL)
            if replacement is not None:parent.remove(element);parent.insert(index,replacement)
    if theme=='Isekai' and id_.startswith(INSTRUMENT):
        circles=[e for e in group.iter() if e.tag==N+'circle' and e.get('class')=='structure' and float(e.get('r','0'))>=7]
        if circles:
            e=max(circles,key=lambda e:float(e.get('r')));x,y,rad=[float(e.get(k)) for k in ('cx','cy','r')]
            marks=[]
            for a in (0,90,180,270):
                a=math.radians(a);r1=rad-.8;r2=min(rad+1.2,x-1.5,30.5-x,y-1.5,30.5-y)
                if r2>r1:marks.append(f'M{x+r1*math.cos(a):.4f} {y+r1*math.sin(a):.4f}L{x+r2*math.cos(a):.4f} {y+r2*math.sin(a):.4f}')
            if marks:group.append(p(' '.join(marks),'secondary'))
    return group

def defs(root,id_):
    d=ET.SubElement(root,N+'defs');g=ET.SubElement(d,N+'linearGradient',{'id':'steel','gradientUnits':'userSpaceOnUse','x1':'0','y1':'0','x2':'32','y2':'32'})
    for offset,color in [('0','#A2BDCC'),('.42','#456878'),('.66','#A3BAC6'),('1','#4D6D7B')]:ET.SubElement(g,N+'stop',{'offset':offset,'stop-color':color})
    glow=ET.SubElement(d,N+'filter',{'id':'signal-glow','filterUnits':'userSpaceOnUse','x':'-2','y':'-2','width':'36','height':'36'})
    ET.SubElement(glow,N+'feGaussianBlur',{'stdDeviation':'.5','result':'soft'})
    merge=ET.SubElement(glow,N+'feMerge');ET.SubElement(merge,N+'feMergeNode',{'in':'soft'});ET.SubElement(merge,N+'feMergeNode',{'in':'SourceGraphic'})

profiles=[]
for theme in ('Lumen','Isekai'):
    folder=ASSETS/'Themes'/theme;folder.mkdir(parents=True,exist_ok=True);overrides=[]
    for item in META['usedDrawings']:
        id_=item['id'];source=ASSETS/('Designed' if id_ in META['newlyAuthored'] else 'Approved')/(id_+'.svg')
        raw=source.read_bytes()
        if theme=='Lumen':BASE_HASH.update(id_.encode()+b'\0'+raw+b'\0')
        base=ET.fromstring(raw).find(N+'g');assert base is not None
        study=PROPOSAL/'svg'/('hud' if theme=='Lumen' else 'isekai')/(id_+'.svg')
        if study.exists():
            group=deepcopy(ET.parse(study).getroot().find(N+'g'));overrides.append(id_)
        else:group=themed_group(base,id_,theme)
        # Sword-and-magic vocabulary: grimoire clasp and magical aperture landmarks.
        if theme=='Isekai' and id_=='user-guide':group.append(p('m16 13 1.4 3-1.4 3-1.4-3Z','secondary'))
        if theme=='Isekai' and id_=='wavefront-map':group.append(p('m16 1.8 1 1.2-1 1.2-1-1.2Z M2.2 15l1.2 1-1.2 1-1.2-1 M29 15l1.2 1-1.2 1-1.2-1','secondary'))
        root=ET.Element(N+'svg',{'width':'32','height':'32','viewBox':'0 0 32 32','role':'img','aria-label':item['label'],'data-icon-finish':theme})
        ET.SubElement(root,N+'title').text=item['label']
        pal=PALS[theme]
        if theme=='Lumen':defs(root,id_)
        css=f'.structure{{fill:none;stroke:{"url(#steel)" if theme=="Lumen" else pal["structure"]}}}.signal{{fill:none;stroke:{pal["signal"]}}}.secondary{{fill:none;stroke:{pal["secondary"]}}}.signal-fill{{fill:{pal["signal"]};stroke:none}}.secondary-fill{{fill:{pal["fill"]};stroke:none}}.hot-fill{{fill:{pal["hot"]};stroke:none}}.glyph{{stroke-width:1.65;stroke-linecap:round;stroke-linejoin:round}}'
        if theme=='Lumen':css+='.signal{filter:url(#signal-glow)}'
        ET.SubElement(root,N+'style').text=css
        root.append(group)
        (folder/(id_+'.svg')).write_text(ET.tostring(root,encoding='unicode')+'\n',encoding='utf-8',newline='\n')
    profiles.append(dict(theme=theme,count=len(META['usedDrawings']),directStudyDrawings=overrides,palette=PALS[theme],
        grammar='Segmented instrument rings, chamfered panels, exposed steel and cyan signal traces.' if theme=='Lumen' else 'Sword-and-magic world: grimoires, scrolls, runic calibration and alchemical instruments; simple actions retain legible silhouettes.'))
manifest=dict(version='1.3',usedIcons=221,themeVariants=442,profiles=profiles,unchangedThemes=['Light','Dark','Pixel'],baseMasterCombinedSha256=BASE_HASH.hexdigest())
(ASSETS/'Themes/manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(dict(usedIcons=221,variants=442,profiles=['Lumen HUD','Isekai sword-and-magic']),ensure_ascii=False))
