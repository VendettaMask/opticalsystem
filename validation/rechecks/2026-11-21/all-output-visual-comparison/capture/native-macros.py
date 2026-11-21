import json
from pathlib import Path

out=Path(__file__).resolve().parent.parent
inputs=json.loads((out/'inputs.json').read_text(encoding='utf-8'))
text_only={'Single Ray Trace','First Order','Cardinal Points Data','Seidel Coefficients','Zernike Fringe','Zernike Standard','Zernike Annular','Prescription Report','System Data Report'}
images={'Image Simulation','Geometric Image Analysis','Geometric Bitmap Image Analysis','Light Source Analysis','Partially Coherent Image Analysis','Extended Diffraction Image Analysis'}
for lens in inputs['lenses']:
    folder=out/lens['name']/'zemax'
    rows=[r for r in inputs['analyses'] if r['code'] and r['key'] not in text_only and r['key'] not in images]
    rows.sort(key=lambda r:r['key'] in images)
    lines=[f'LOADLENS "{folder / "screenshot-input.ZMX"}"','UPDATE ALL']
    for row in rows:
        slug=row['slug']
        lines.extend([f'OPENANALYSISWINDOW "{row["code"]}", "{folder / (slug+".CFG")}"','PAUSE THREADS','window_number = WINL()',f'EXPORTJPG window_number, "{folder / (slug+".jpg")}"','CLOSEWINDOW window_number'])
    lines.extend([f'OUTPUT "{folder / "complete.txt"}"','PRINT "completed"','CLOSEWINDOW'])
    # Existing settings permit a screenshot-only replay without optical data export.
    target=folder/('capture-only.zpl' if (folder/'screenshot-input.ZMX').exists() else 'capture.zpl')
    target.write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('Prepared graphical native window macros')
