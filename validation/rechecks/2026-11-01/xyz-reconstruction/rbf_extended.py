from pathlib import Path
import json,numpy as np
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;results=[]
for name in ['garnet_red','krylon_blue','acryl_blue','mystique','cayman']:
    a=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    xyz=a[:,1:4].astype(float);f=a[:,4].astype(float);s=xyz.sum(1);v=a[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0)
    c=np.divide(xyz,s[:,None],out=np.zeros_like(xyz),where=s[:,None]>0)
    all_models=[]
    for fn,x in [('LogXYZ',np.log(np.maximum(xyz,1e-12))),('ColourLogS',np.c_[c[:,:2],np.log(np.maximum(s,1e-12))])]:
        scale=x[tr].std(0);scale[scale==0]=1;x=x/scale
        for k in [96,128,192,256,384,None]:
            for kernel in ['thin_plate_spline','cubic','quintic']:
                try:
                    p=np.exp(RBFInterpolator(x[tr],np.log(f[tr]+1e-6),neighbors=k,kernel=kernel)(x[te]));err=p-f[te]
                    all_models.append(dict(Features=fn,Neighbours=k,Kernel=kernel,Nrmse=float(np.linalg.norm(err)/np.linalg.norm(f[v])),Maximum=float(np.max(abs(err)))))
                except Exception as e:all_models.append(dict(Features=fn,Neighbours=k,Kernel=kernel,Error=str(e)))
    finite=[m for m in all_models if isinstance(m.get('Nrmse'),float) and np.isfinite(m['Nrmse'])]
    best=sorted(finite,key=lambda m:m['Nrmse'])[:4]
    print(name,best,flush=True);results.append(dict(Material=name,Models=all_models,Best=best))
(root/'rbf-extended.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
