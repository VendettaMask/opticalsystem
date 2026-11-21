import ast, hashlib, json, re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[5]
OUT=Path(__file__).resolve().parent.parent
tree=ast.parse((ROOT/'tools/zemax_parity/zosapi_capture_baseline.py').read_text(encoding='utf-8-sig'))
codes=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='ZPL_CODES' for t in n.targets))
registry=(ROOT/'tools/OpticalSystemDesign.ZemaxComparison/AnalysisComparisonRegistry.cs').read_text(encoding='utf-8-sig').split('private const string Mapping = """')[1].split('"""')[0]
mapping={r.split('|')[0]:r.split('|')[1] for r in registry.strip().splitlines()}
slug=lambda s:re.sub('[^a-z0-9]','-',s.lower()).strip('-')
rows=[{'key':k,'id':v,'code':codes.get(v),'slug':slug(k)} for k,v in mapping.items() if not k.startswith('Non-Sequential') and k!='Zernike']
for k,v in [('2D Layout','Draw2D'),('3D Layout','Draw3D'),('Solid Model','ShadedModel')]:
    rows.append({'key':k,'id':v,'code':codes.get(v),'slug':slug(k)})
items=[]
for name,file in [('cooke','Cooke 40 degree field.zmx'),('double-gauss','Double Gauss 28 degree field.zmx'),('petzval','Petzval.zmx')]:
    source=ROOT/'src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives'/file
    folder=OUT/name/'zemax';folder.mkdir(parents=True,exist_ok=True)
    (folder/'requests.tsv').write_text(''.join(f"{r['slug']}\t{r['id']}\t{r['code']}\n" for r in rows if r['code']),encoding='utf-8')
    items.append({'name':name,'file':file,'source':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest()})
(OUT/'inputs.json').write_text(json.dumps({'lenses':items,'analyses':rows},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'lenses':len(items),'nativeWindows':sum(bool(r['code']) for r in rows),'output':str(OUT)}))
