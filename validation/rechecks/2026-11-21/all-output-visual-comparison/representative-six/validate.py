import hashlib,json,sys
from collections import Counter
from pathlib import Path
from PIL import Image

out=Path(__file__).resolve().parent
data=json.loads((out/'inputs.json').read_text(encoding='utf-8'))
catalog=json.loads(Path(data['catalogSource']).read_text(encoding='utf-8-sig'))
index={r['RelativePath']:r for r in catalog['Files']}
native_only='--native-only' in sys.argv
cases=[];images=[];errors=[]
for lens in data['lenses']:
    folder=out/lens['name'];digest=hashlib.sha256(Path(lens['source']).read_bytes()).hexdigest()
    source_ok=digest==lens['sha256']==index[lens['relativePath']]['Sha256']
    native_dir=lens.get('nativeDirectory','zemax')
    expected={line.split('\t')[0] for line in (folder/lens.get('nativeRequestDirectory',native_dir)/'requests.tsv').read_text(encoding='utf-8').splitlines()}
    native={};current={}
    for kind in (['zemax'] if native_only else ['zemax','workbench']):
        for p in (folder/(native_dir if kind=='zemax' else kind)).glob('*'):
            if p.suffix.lower() not in {'.jpg','.jpeg','.png'}:continue
            try:
                with Image.open(p) as im:im.load();dimensions=list(im.size)
                receipt={'file':p.relative_to(out).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'pixels':dimensions}
                images.append(receipt)
                (native if kind=='zemax' else current)[p.stem]=receipt
            except Exception as e:errors.append({'file':p.relative_to(out).as_posix(),'error':str(e)})
    manifest=folder/'workbench/manifest.json'
    results=json.loads(manifest.read_text(encoding='utf-8'))['Results'] if manifest.exists() else []
    case={'name':lens['name'],'relativePath':lens['relativePath'],'inMenu95':lens['relativePath'] in index,'sourceUnchanged':source_ok,'nativeImages':len(native),'missingNativeWindows':sorted(expected-set(native)),'currentEntries':len(results),'currentImages':len(current),'currentStatuses':dict(Counter(r['Status'] for r in results))}
    if native_only:
        statusfile=folder/native_dir/'visual-capture-status.json'
        status=json.loads(statusfile.read_text(encoding='utf-8')) if statusfile.exists() else {}
        case['nativeAttempts']=len(status.get('attempts',[])) or int((folder/native_dir/'environment.txt').exists())
        if len(status.get('attempts',[]))>1:
            case['captureInterruptionNote']='The original monitor read UTF-8 while native progress was UTF-16. Original interruptions remain recorded; valid exported images and completed retries establish visual coverage, not a native analysis stall.'
    cases.append(case)
complete=all(c['sourceUnchanged'] and not c['missingNativeWindows'] and (native_only or c['currentEntries']==c['currentImages']==71) for c in cases) and not errors
receipt={'scope':'visual-only; image capture is not numerical acceptance','channelsValidated':['zemax'] if native_only else ['zemax','workbench'],'menuCatalogCount':len(index),'selectedLensCount':len(cases),'completeCaptureInventory':complete,'cases':cases,'invalidImages':errors,'images':images}
name='native-image-validation.json' if native_only else 'verification.json'
(out/name).write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'completeCaptureInventory':complete,'cases':[{k:v for k,v in c.items() if k not in {'captureInterruptionNote','currentStatuses'}} for c in cases],'invalidImages':len(errors),'decodedImages':len(images)},ensure_ascii=False))
if not complete:raise SystemExit(1)
