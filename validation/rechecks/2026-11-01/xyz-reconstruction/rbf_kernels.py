"""Independent development diagnostics, never a product dependency or blind test."""
from pathlib import Path
import numpy as np,json
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;out=[]
for name in ['acryl_blue','mystique','cayman']:
    rows=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    x=rows[:,1:4].astype(float);s=x.sum(1);f=rows[:,4].astype(float);v=rows[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0);c=x/s[:,None];results=[]
    for fn,xx in [('LogXYZ',np.log(x+1e-12)),('ColourLogS',np.column_stack((c[:,:2],np.log(s+1e-12)))),('XYZ',x),('ColourS',np.column_stack((c[:,:2],s)))]:
        xx=np.ascontiguousarray(xx/xx[tr].std(0))
        for kernel in ['linear','cubic','quintic','thin_plate_spline','multiquadric','inverse_multiquadric','gaussian']:
            for k in [32,64,128,256]:
                for smooth in [1e-6,1e-4,.01]:
                    try:
                        p=np.exp(RBFInterpolator(xx[tr],np.log(f[tr]+1e-6),neighbors=k,kernel=kernel,smoothing=smooth,epsilon=1)(xx[te]));e=p-f[te]
                        if np.all(np.isfinite(p)):results.append(dict(Features=fn,Kernel=kernel,Neighbours=k,Smoothing=smooth,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[te])),Maximum=float(np.max(abs(e)))))
                    except (ValueError,np.linalg.LinAlgError):pass
    best=sorted(results,key=lambda r:r['Nrmse'])[:10];print(name,best,flush=True);out.append(dict(Material=name,Models=results,Best=best))
(root/'rbf-kernels.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
