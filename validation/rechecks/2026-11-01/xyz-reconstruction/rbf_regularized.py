from pathlib import Path
import json,numpy as np
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;results=[]
for name in ['acryl_blue','mystique','cayman']:
    rows=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    x=rows[:,1:4].astype(float);s=x.sum(1);f=rows[:,4].astype(float);v=rows[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0);c=np.divide(x,s[:,None],out=np.zeros_like(x),where=s[:,None]>0)
    models=[]
    for fn,xx in [('LogXYZ',np.log(x+1e-12)),('ColourLogS',np.column_stack((c[:,:2],np.log(s+1e-12))))]:
        xx=np.ascontiguousarray(xx/xx[tr].std(0))
        for k in [48,96,192,None]:
            for smooth in [1e-10,1e-8,1e-6,1e-4,.01,.1]:
                try:
                    p=np.exp(RBFInterpolator(xx[tr],np.log(f[tr]+1e-6),neighbors=k,kernel='thin_plate_spline',smoothing=smooth)(xx[te]));e=p-f[te]
                    models.append(dict(Features=fn,Neighbours=k,Smoothing=smooth,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[v])),Maximum=float(np.max(abs(e)))))
                except Exception as e:models.append(dict(Features=fn,Neighbours=k,Smoothing=smooth,Error=str(e)))
    best=sorted([m for m in models if np.isfinite(m.get('Nrmse',np.inf))],key=lambda m:m['Nrmse'])[:4]
    print(name,best,flush=True);results.append(dict(Material=name,Models=models,Best=best))
(root/'rbf-regularized.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
