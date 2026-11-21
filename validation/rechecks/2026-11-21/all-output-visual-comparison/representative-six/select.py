import hashlib,json
from pathlib import Path

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[4]
SAMPLES=ROOT/'src/OpticalSystemDesign.App/Assets/SequentialExamples'
catalog=json.loads((SAMPLES/'index.json').read_text(encoding='utf-8-sig'))
assert len(catalog['Files'])==95
index={r['RelativePath']:r for r in catalog['Files']}
chosen=[
 ('double-gauss','Objectives/Double Gauss 28 degree field.zmx','常规折射物镜','多片折射、轴上与离轴像差、色差和常规成像图'),
 ('schmidt-cassegrain','Telescopes/Schmidt-Cassegrain.zmx','折反射望远镜','反射路径、非球面、中心遮拦及环形光瞳'),
 ('yolo','Telescopes/Yolo.zmx','离轴反射系统','倾斜偏心、坐标变换及不对称光线布局'),
 ('afocal-riflescope','Afocal/Afocal Riflescope.ZMX','无焦系统','无焦光束、角度坐标及无焦分析适用性'),
 ('zoom-lens','Zoom systems/Zoom lens.zmx','多配置变焦','多配置处方、变焦光路及配置矩阵显示'),
 ('grating','Diffractive components/Grating.zmx','衍射光栅系统','衍射方向、波长分离与衍射系统图形'),
]
lenses=[]
for name,relative,kind,reason in chosen:
    assert relative in index
    source=SAMPLES/relative
    digest=hashlib.sha256(source.read_bytes()).hexdigest()
    assert digest==index[relative]['Sha256'],relative
    lens={'name':name,'file':source.name,'relativePath':relative,'source':str(source),'sha256':digest,'kind':kind,'selectionReason':reason,'inMenu95':True}
    lenses.append(lens)
    (OUT/name/'zemax').mkdir(parents=True,exist_ok=True)
old=json.loads((OUT.parent/'inputs.json').read_text(encoding='utf-8'))
reports={'Single Ray Trace','First Order','Cardinal Points Data','Seidel Coefficients','Zernike Fringe','Zernike Standard','Zernike Annular','Prescription Report','System Data Report','Classified Data Report'}
images={'Image Simulation','Geometric Image Analysis','Geometric Bitmap Image Analysis','Light Source Analysis','Partially Coherent Image Analysis','Extended Diffraction Image Analysis'}
rows=[r for r in old['analyses'] if r.get('code') and r['key'] not in reports|images]
for lens in lenses:
    folder=OUT/lens['name']/'zemax'
    (folder/'requests.tsv').write_text(''.join(f"{r['slug']}\t{r['id']}\t{r['code']}\n" for r in rows),encoding='utf-8')
    lines=[f'LOADLENS "{folder/"screenshot-input.ZMX"}"','UPDATE ALL']
    for row in rows:
        lines.extend([f'OUTPUT "{folder/"progress.txt"}"',f'PRINT "{row["slug"]}"',f'OPENANALYSISWINDOW "{row["code"]}", "{folder/(row["slug"]+".CFG")}"','PAUSE THREADS','window_number = WINL()',f'EXPORTJPG window_number, "{folder/(row["slug"]+".jpg")}"','CLOSEWINDOW window_number'])
    lines.extend([f'OUTPUT "{folder/"complete.txt"}"','PRINT "completed"','CLOSEWINDOW'])
    (folder/'capture.zpl').write_text('\n'.join(lines)+'\n',encoding='utf-8')
data={'scope':'representative-six-selected-from-menu95','catalogSource':str(SAMPLES/'index.json'),'catalogCount':95,'lenses':lenses,'analyses':old['analyses']}
(OUT/'inputs.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'selected':len(lenses),'allFromMenu95':True,'hashesMatchMenuIndex':True,'nativeGraphicalWindows':len(rows)},ensure_ascii=False))
