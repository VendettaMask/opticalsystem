"""Independent exploration on the original development split, not blind validation."""
from pathlib import Path
import json,numpy as np
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;all_results=[]
for name in ['acryl_blue','mystique','cayman']:
    rows=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    x=rows[:,1:4].astype(float);s=x.sum(1);f=rows[:,4].astype(float);v=rows[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0);c=np.divide(x,s[:,None],out=np.zeros_like(x),where=s[:,None]>0);r=[]
    for fn,xx in [('LogXYZ',np.log(x+1e-12)),('ColourLogS',np.column_stack((c[:,:2],np.log(s+1e-12)))),('XYZ',x)]:
        xx=np.ascontiguousarray(xx/xx[tr].std(0))
        for kernel in ['thin_plate_spline','multiquadric']:
            for power in [.25,.5,1]:
                for k in [32,64,128,192]:
                    for smooth in [1e-6,.001,.1]:
                        try:
                            pred=RBFInterpolator(xx[tr],(f[tr]+1e-6)**power,neighbors=k,kernel=kernel,smoothing=smooth,epsilon=1)(xx[te]);p=abs(pred)**(1/power);e=p-f[te]
                            if np.all(np.isfinite(p)):r.append(dict(Features=fn,Kernel=kernel,Power=power,Neighbours=k,Smoothing=smooth,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[te])),Maximum=float(max(abs(e))),NegativeLatent=int(np.count_nonzero(pred<0))))
                        except (ValueError,np.linalg.LinAlgError):pass
    best=sorted(r,key=lambda r:r['Nrmse'])[:10];print(name,best,flush=True);all_results.append(dict(Material=name,Models=r,Best=best))
(root/'rbf-response.json').write_text(json.dumps(all_results,indent=2),encoding='utf-8')
