"""Normalize the pinned Lucide geometry to the approved 32-grid semantic SVG style.

Approved originals live beside the generated Glyphs folder and are never rewritten.
Run with Python 3; generation is deterministic and uses no network or third-party modules.
"""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'src/OpticalSystemDesign.App/Assets/Icons'
DEST = ASSETS / 'Scientific/Glyphs'
DEST.mkdir(parents=True, exist_ok=True)
catalog = json.loads((ASSETS/'lucide-icon-nodes.json').read_text(encoding='utf-8'))
N = '{http://www.w3.org/2000/svg}'
ET.register_namespace('', N[1:-1])
CSS = '.structure{fill:none;stroke:#8298A8}.signal{fill:none;stroke:#398FF0}.secondary{fill:none;stroke:#28ADAF}.signal-fill{fill:#398FF0;stroke:none}.secondary-fill{fill:#28ADAF;stroke:none}.glyph{stroke-width:1.65;stroke-linecap:round;stroke-linejoin:round}.dark .structure{stroke:#A5978B}.dark .signal{stroke:#F0AE79}.dark .secondary{stroke:#D5B89C}.dark .signal-fill{fill:#F0AE79}.dark .secondary-fill{fill:#D5B89C}'
TOKENS = re.compile(r'[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?')
COUNTS = dict(M=2,L=2,H=1,V=1,C=6,S=4,Q=4,T=2,A=7,Z=0)

def f(v):
    return format(round(v, 6), '.6f').rstrip('0').rstrip('.') if v else '0'

def coord(v):
    return f(float(v)*1.25+1)

def length(v):
    return f(float(v)*1.25)

def path_data(source):
    number=re.compile(r'[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?')
    out=[]; index=0; command=None; first=True
    def skip(index):
        while index<len(source) and source[index] in ' \t\r\n,':index+=1
        return index
    while (index:=skip(index))<len(source):
        if source[index].isalpha():
            command=source[index];index+=1;out.append(command)
            if command.upper()=='Z':continue
        if command is None:raise ValueError(source)
        count=COUNTS[command.upper()]
        if count==0:raise ValueError(source)
        values=[]
        for k in range(count):
            index=skip(index)
            if command.upper()=='A' and k in (3,4):
                if index>=len(source) or source[index] not in '01':raise ValueError(source)
                values.append(source[index]);index+=1
            else:
                match=number.match(source,index)
                if match is None:raise ValueError(source)
                values.append(match.group());index=match.end()
        relative=command.islower()
        for k,value in enumerate(values):
            if command.upper()=='A':
                if k in (2,3,4):out.append(value)
                elif k in (0,1):out.append(length(value))
                else:out.append(length(value) if relative else coord(value))
            elif relative:
                out.append(coord(value) if first and command=='m' else length(value))
            else:out.append(coord(value))
        first=False
        # Repeated moveto pairs are implicit lineto pairs.
        if command=='M':command='L'
        elif command=='m':command='l'
    return ' '.join(out)

def node(tag, attrs):
    result={}
    for key,value in attrs.items():
        if key=='d':result[key]=path_data(value)
        elif key in {'cx','cy','x','y','x1','x2','y1','y2'}:result[key]=coord(value)
        elif key in {'r','rx','ry','width','height'}:result[key]=length(value)
        elif key=='points':result[key]=' '.join(coord(v) for v in re.split('[ ,]+',value.strip()))
        elif key!='key':result[key]=str(value)
    return ET.Element(N+tag,result)

ACTION_PREFIXES=('arrow','chevron','check','x','plus','minus','play','pause','skip','step','move','rotate','refresh','repeat','undo','redo','maximize','minimize','expand','shrink','corner','navigation','slash','equal','not-equal','less-than','greater-than','more-','ellipsis')
FRAME_PREFIXES=('file','folder','book','notebook','clipboard','calendar','panel','table','layout','monitor','screen','app-window','rectangle','square','circle','badge','mail','message','ticket','database','hard-drive','save','printer','image','camera')
DATA_PREFIXES=('chart','axis','grid','rows','columns','list','network','workflow','git-','route','scan','focus','target','telescope','microscope','aperture')

def roles(name,nodes):
    """Assign frames to structure, actions/data to signal, and small landmarks to secondary."""
    if name.startswith(ACTION_PREFIXES):return ['signal']*len(nodes)
    if name.startswith(FRAME_PREFIXES):
        # The pinned catalog stores the enclosing outline first; remaining nodes are the named feature.
        result=['structure']+['signal']*(len(nodes)-1)
        for k,(tag,a) in enumerate(nodes):
            if k and tag in ('circle','ellipse'):result[k]='secondary'
        return result
    if name.startswith(DATA_PREFIXES):
        result=['signal']*len(nodes)
        if len(nodes)>1:result[0]='structure'
        for k,(tag,a) in enumerate(nodes):
            if tag=='circle' and float(a.get('r','99'))<=2.5:result[k]='secondary'
        return result
    result=['signal']*len(nodes)
    for k,(tag,a) in enumerate(nodes):
        if tag=='rect' or tag in ('circle','ellipse') and float(a.get('r',a.get('rx','0')))>=4:result[k]='structure'
        elif tag=='circle':result[k]='secondary'
    return result

for name,nodes in sorted(catalog.items()):
    root=ET.Element(N+'svg',{'width':'32','height':'32','viewBox':'0 0 32 32','role':'img','aria-label':name})
    ET.SubElement(root,N+'title').text=name
    ET.SubElement(root,N+'style').text=CSS
    group=ET.SubElement(root,N+'g',{'class':'glyph'})
    for (tag,attrs),role in zip(nodes,roles(name,nodes)):
        item=node(tag,attrs);item.set('class',role);group.append(item)
    (DEST/(name+'.svg')).write_text(ET.tostring(root,encoding='unicode')+'\n',encoding='utf-8')

approved=sorted(p.stem for p in (ASSETS/'Scientific/Approved').glob('*.svg'))
designed=sorted(p.stem for p in (ASSETS/'Scientific/Designed').glob('*.svg'))
metadata={'style':'Optical Scientific Outline','version':'1.2','grid':[32,32],'strokeWidth':1.65,
          'derivedGeometry':{'source':'lucide-static 1.25.0','license':'ISC','count':len(catalog)},
          'approvedOriginals':approved,'designedOriginals':designed,'total':len(catalog)+len(approved)+len(designed)}
profiles=ASSETS/'Scientific/Themes/manifest.json'
if profiles.exists():
    metadata['version']='1.3'
    metadata['themeVariants']={p['theme']:p['count'] for p in json.loads(profiles.read_text(encoding='utf-8-sig'))['profiles']}
(ASSETS/'Scientific/catalog.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'Generated {len(catalog)} reserve SVGs; {len(approved)} approved and {len(designed)} newly authored drawings; total {metadata["total"]}.')
