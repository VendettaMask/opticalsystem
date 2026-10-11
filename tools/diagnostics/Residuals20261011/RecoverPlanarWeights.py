"""Independent offline inverse check; recovered values never enter Core."""
import json, pathlib, sys
import numpy as np
root=pathlib.Path(sys.argv[1]).resolve()
out=pathlib.Path(sys.argv[2]).resolve(); out.mkdir(parents=True,exist_ok=False)
base=root/'artifacts/validation/residuals-20261011/followup'
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
r=read(base/'wide/real/Planar/data.json')['dataGrids'][0]
i=read(base/'wide/imaginary/Planar/data.json')['dataGrids'][0]
field=np.array(r['values'])+1j*np.array(i['values'])
# Wide output is explicitly truncated. Only its central captured support is used.
indices=np.arange(110,147)
coords=(indices*r['dx']+r['minX'])
xx,yy=np.meshgrid(coords,coords)
coefficient=read(base/'kernel-comparison/kernel-controls.json')[0]['coefficient']
envelope=np.sinc(coefficient*xx/np.pi)*np.sinc(coefficient*yy/np.pi)
target=(field[np.ix_(indices,indices)]/envelope).reshape(-1)
p=read(base/'native/pupil-zemax/pupil-grid.json')
l=np.array([v['l'] for v in p['rows']]); m=np.array([v['m'] for v in p['rows']])
matrix=np.exp(-1j*2*np.pi/.4861327*(xx.reshape(-1,1)*l+yy.reshape(-1,1)*m))
weights,residual,rank,singular=np.linalg.lstsq(matrix,target,rcond=1e-9)
error=np.linalg.norm(matrix@weights-target)/np.linalg.norm(target)
summary=dict(rank=int(rank),relativeL2=float(error),condition=float(singular[0]/singular[-1]),
             semantics='Diagnostic inverse amplitudes only; sinc coefficient estimated from independent Gaussian controls, never a product correction.',
             weights=[dict(px=v['px'],py=v['py'],amplitude=float(abs(w)),phase=float(np.angle(w))) for v,w in zip(p['inputs'],weights)])
(out/'recovered-weights.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print('rank',rank,'L2',error,'condition',singular[0]/singular[-1])
print('weights amplitude range',abs(weights).min(),abs(weights).max(),'median',np.median(abs(weights)))
print('lowest',sorted(summary['weights'],key=lambda p:p['amplitude'])[:20])
