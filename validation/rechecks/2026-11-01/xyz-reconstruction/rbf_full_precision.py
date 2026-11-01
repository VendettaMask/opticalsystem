"""Original MAT spectra; independently project official CIE tables and preserve the original split."""
from pathlib import Path
import numpy as np,json
from scipy.io import loadmat
from scipy.interpolate import RBFInterpolator
root=Path(__file__).resolve().parent;repo=root.parents[3]
cmf=np.loadtxt(repo/'validation/rechecks/2026-10-31/commercial-bsdf-calibration/derived-xyz-candidates/CIE_xyz_1931_2deg.csv',delimiter=',');spd=np.loadtxt(root.parent/'xyz-radiometry/CIE_std_illum_D65.csv',delimiter=',')
waves=np.array([w+(1+g)/2 for w in range(400,700) for g in [-1/np.sqrt(3),1/np.sqrt(3)]])
basis=np.stack([np.interp(waves,np.arange(400,701,10),np.eye(31)[i]) for i in range(31)],axis=1)
response=np.stack([np.interp(waves,cmf[:,0],cmf[:,j])*np.interp(waves,spd[:,0],spd[:,1]) for j in range(1,4)],axis=1)
projection=basis.T@response/response[:,1].sum();results=[]
for name in ['acryl_blue','mystique','cayman']:
    old=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',delimiter=',',skiprows=1,dtype=str);part={tuple(map(float,r[0].split('/'))):r[5]=='1' for r in old}
    source=repo/'validation/rechecks/2026-11-01/xyz-radiometry/measured'/f'{name}.astm' if name=='acryl_blue' else repo/'validation/rechecks/2026-10-31'/('commercial-bsdf-calibration/mystique.astm' if name=='mystique' else 'white-specular-runtime/xyz-measured/cayman.astm')
    rows=np.array([[float(v) for v in r.split(',')] for r in source.read_text().splitlines() if r and r[0].isdigit()]);v=np.array([part[tuple(r[:4])] for r in rows])
    fmat=loadmat(root.parent/'xyz-angular/full-precision'/('mystique.mat' if name=='mystique' else name+'.mat'))['fr'].T
    x=fmat@projection;f=fmat[:,15];s=x.sum(1);tr=(~v)&(s>0);te=v&(s>0);models=[]
    for fn,xx,k,smooth in [('colour_strength',np.c_[x[:,0]/s,x[:,1]/s,np.log(s+1e-12)],96,.01),('log_xyz',np.log(x+1e-12),192,.0001)]:
        xx=np.ascontiguousarray(xx/xx[tr].std(0));p=np.exp(RBFInterpolator(xx[tr],np.log(f[tr]+1e-6),neighbors=k,kernel='thin_plate_spline',smoothing=smooth)(xx[te]));e=p-f[te];models.append(dict(Configuration=fn,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[te])),Maximum=float(max(abs(e)))))
    results.append(dict(Material=name,Models=models,Exported=False,IndependentDiagnosticOnly=True));print(name,models,flush=True)
(root/'rbf-full-precision.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
