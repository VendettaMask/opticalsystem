import json,sys
from pathlib import Path
from PIL import Image

out=Path(__file__).resolve().parent
original=out/'zoom-lens/zemax'
folder=out/'zoom-lens/zemax-config1';folder.mkdir(exist_ok=True)
(folder/'force-first-configuration.txt').write_text('Use configuration 1 for ordinary views; matrix analysis may display all configurations.',encoding='utf-8')
(folder/'requests.tsv').write_text('',encoding='utf-8')
rows=[line.split('\t') for line in (original/'requests.tsv').read_text(encoding='utf-8').splitlines()]
lines=[]
for slug,ident,code in rows:
    lines.extend([f'LOADLENS "{folder/"screenshot-input.ZMX"}"','UPDATE ALL',f'OPENANALYSISWINDOW "{code}", "{original/(slug+".CFG")}"','PAUSE THREADS','window_number = WINL()',f'EXPORTJPG window_number, "{folder/(slug+".jpg")}"','CLOSEWINDOW window_number'])
lines.extend([f'OUTPUT "{folder/"complete.txt"}"','PRINT "completed"','CLOSEWINDOW'])
(folder/'capture.zpl').write_text('\n'.join(lines)+'\n',encoding='utf-8')
if '--activate' in sys.argv:
    for slug,ident,code in rows:
        path=next(p for p in folder.iterdir() if p.stem==slug and p.suffix.lower()=='.jpg')
        with Image.open(path) as im:im.load()
    data=json.loads((out/'inputs.json').read_text(encoding='utf-8'))
    lens=next(l for l in data['lenses'] if l['name']=='zoom-lens')
    lens.update({'nativeDirectory':'zemax-config1','nativeRequestDirectory':'zemax','nativeSnapshotConfiguration':1,'reloadNativeBeforeEachWindow':True,'previousNativeCaptureDirectory':'zemax'})
    (out/'inputs.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Activated independently reloaded configuration-1 native images after complete pixel decoding')
print('Prepared a configuration-1 snapshot reloaded independently before every native window')
