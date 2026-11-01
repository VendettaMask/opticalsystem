"""Train-only feature covariance metrics; original development labels only score."""
from pathlib import Path
import json,numpy as np
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;out=[]
for name in ['acryl_blue','mystique','cayman']:
    rows=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    x=rows[:,1:4].astype(float);s=x.sum(1);f=rows[:,4].astype(float);v=rows[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0);c=np.divide(x,s[:,None],out=np.zeros_like(x),where=s[:,None]>0);r=[]
    for fn,raw in [('LogXYZ',np.log(x+1e-12)),('ColourLogS',np.column_stack((c[:,:2],np.log(s+1e-12))))]:
        raw=raw/raw[tr].std(0);cov=np.cov(raw[tr].T,bias=True);eig,vec=np.linalg.eigh(cov)
        for ridge in [0,.001,.01,.1]:
            transform=vec@np.diag(1/np.sqrt(eig+ridge))@vec.T;xx=np.ascontiguousarray(raw@transform)
            for kernel in ['thin_plate_spline','multiquadric']:
                for k in [32,64,96,192]:
                    for smooth in [1e-6,.0001,.01]:
                        try:
                            p=np.exp(RBFInterpolator(xx[tr],np.log(f[tr]+1e-6),neighbors=k,kernel=kernel,smoothing=smooth,epsilon=1)(xx[te]));e=p-f[te]
                            if np.all(np.isfinite(p)):r.append(dict(Features=fn,Kernel=kernel,Ridge=ridge,Neighbours=k,Smoothing=smooth,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[te])),Maximum=float(max(abs(e)))))
                        except (ValueError,np.linalg.LinAlgError):pass
    best=sorted(r,key=lambda r:r['Nrmse'])[:10];print(name,best,flush=True);out.append(dict(Material=name,Models=r,Best=best))
(root/'rbf-whitening.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
