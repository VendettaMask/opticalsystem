"""Radiometric-loss weighting from TRAINING targets only; independent development diagnostic."""
from pathlib import Path
import json,numpy as np
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;out=[]
for name in ['acryl_blue','mystique','cayman']:
    rows=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    x=rows[:,1:4].astype(float);s=x.sum(1);f=rows[:,4].astype(float);v=rows[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0);c=np.divide(x,s[:,None],out=np.zeros_like(x),where=s[:,None]>0);r=[]
    for fn,xx in [('LogXYZ',np.log(x+1e-12)),('ColourLogS',np.c_[c[:,:2],np.log(s+1e-12)])]:
        xx=np.ascontiguousarray(xx/xx[tr].std(0));signal=np.sqrt(np.mean(f[tr]**2))
        for k in [32,64,96,192]:
            for exponent in [1,2]:
                for smooth in [1e-6,.0001,.01,.1]:
                    try:
                        diagonal=smooth*(signal/(f[tr]+1e-6))**exponent
                        p=np.exp(RBFInterpolator(xx[tr],np.log(f[tr]+1e-6),neighbors=k,kernel='thin_plate_spline',smoothing=diagonal)(xx[te]));e=p-f[te]
                        if np.all(np.isfinite(p)):r.append(dict(Features=fn,Neighbours=k,WeightExponent=exponent,Smoothing=smooth,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[te])),Maximum=float(max(abs(e)))))
                    except (ValueError,np.linalg.LinAlgError):pass
    best=sorted(r,key=lambda p:p['Nrmse'])[:6];print(name,best,flush=True);out.append(dict(Material=name,Models=r,Best=best,Exported=False))
(root/'rbf-weighted.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
