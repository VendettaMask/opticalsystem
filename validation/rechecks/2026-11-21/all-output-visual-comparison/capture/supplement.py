import json
from pathlib import Path
out=Path(__file__).resolve().parent.parent
data=json.loads((out/'inputs.json').read_text(encoding='utf-8'))
for key,ident,code,slug in [('Zernike vs Field','ZernikeCoefficientsVsField','Zvf','zernike-vs-field'),('Wavefront Map','WavefrontMap','Wfm','wavefront-map')]:
    if not any(x['key']==key for x in data['analyses']):data['analyses'].append({'key':key,'id':ident,'code':code,'slug':slug})
for lens in data['lenses']:
    d=out/lens['name']/'zemax';requests=d/'requests.tsv'
    content=requests.read_text(encoding='utf-8')
    if 'ZernikeCoefficientsVsField' not in content:requests.write_text(content+'zernike-vs-field\tZernikeCoefficientsVsField\tZvf\n',encoding='utf-8')
    lines=[f'LOADLENS "{d/"screenshot-input.ZMX"}"','UPDATE ALL']
    for slug,code,cfg in [('zernike-vs-field','Zvf','zernike-vs-field'),('wavefront-map','Wfm','wavefront')]:
        lines.extend([f'OPENANALYSISWINDOW "{code}", "{d/(cfg+".CFG")}"','PAUSE THREADS','window_number = WINL()',f'EXPORTJPG window_number, "{d/(slug+".jpg")}"','CLOSEWINDOW window_number'])
    lines.extend([f'OUTPUT "{d/"supplement-complete.txt"}"','PRINT "completed"','CLOSEWINDOW'])
    (d/'supplement-capture.zpl').write_text('\n'.join(lines)+'\n',encoding='utf-8')
(out/'inputs.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
