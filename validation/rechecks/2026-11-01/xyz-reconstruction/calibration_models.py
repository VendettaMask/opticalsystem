"""Independent candidates only. Never reads validation labels during fit."""
from pathlib import Path
import json, importlib.util
import numpy as np
from scipy.spatial import cKDTree
from scipy.interpolate import RBFInterpolator, BSpline
from scipy.optimize import nnls
root=Path(__file__).resolve().parent; report=[]
for name in ['garnet_red','krylon_blue','acryl_blue','mystique','cayman']:
    a=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',delimiter=',',skiprows=1,dtype=str)
    xyz=a[:,1:4].astype(float);f=a[:,4].astype(float);v=a[:,5]=='1';s=xyz.sum(1);c=np.divide(xyz,s[:,None],out=np.zeros_like(xyz),where=s[:,None]>0)
    tr=(~v)&(s>0);te=v&(s>0);r=np.divide(f,s,out=np.zeros_like(f),where=s>0);models=[]
    def score(method,p):
        e=p-f[te];models.append(dict(Method=method,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[v])),Maximum=float(np.max(abs(e))),Negative=int(np.count_nonzero(p<0))))
    d,ids=cKDTree(c[tr]).query(c[te],k=48)
    for k in [3,6,12,24,48]:
        for p in [1,2,4]:
            for strength in [-2,-1,1,2]:
                w=(d[:,:1]/np.maximum(d[:,:k],1e-150))**p*(s[tr][ids[:,:k]]/np.median(s[tr]))**strength
                z=(w*r[tr][ids[:,:k]]).sum(1)/np.maximum(w.sum(1),1e-300)
                for j in np.flatnonzero(d[:,0]==0):z[j]=np.mean(r[tr][np.all(c[tr]==c[te][j],axis=1)])
                score(f'SignalWeightedK{k}D{p}S{strength}',z*s[te])
    for feature,x in [('Colour',c[:,:2]),('LogXYZ',np.log(np.maximum(xyz,1e-12))),('ColourLogS',np.c_[c[:,:2],np.log(np.maximum(s,1e-12))])]:
        scale=x[tr].std(0);scale[scale==0]=1;x=x/scale
        for floor in [5e-7,1e-6,1e-5,1e-4]:
            for k in [24,48,96]:
                y=np.log(f[tr]+floor)
                try:
                    p=np.exp(RBFInterpolator(x[tr],y,neighbors=k,kernel='thin_plate_spline')(x[te]))
                    score(f'{feature}LogOffset{floor}K{k}',p)
                except (ValueError,np.linalg.LinAlgError):pass
    # Positive tensor B-spline cone, weighted in original radiometric units.
    x=c[:,:2];low=x[tr].min(0);high=x[tr].max(0);x=(x-low)/(high-low)
    x=np.clip(x,0,1) # Diagnostic outside training rectangle; explicitly not an approved extrapolation.
    for n in [6,10,14,18]:
        b=[]
        for j in range(2):
            interior=np.linspace(0,1,n-2)[1:-1];knots=np.r_[np.zeros(4),interior,np.ones(4)]
            b.append(BSpline.design_matrix(x[:,j],knots,3).toarray())
        basis=(b[0][:,:,None]*b[1][:,None,:]).reshape(len(a),-1)
        mat=basis[tr]*s[tr,None];mat/=np.max(s[tr])
        try:
            coefficients,_=nnls(mat,f[tr]/np.max(s[tr]),maxiter=10000)
            score(f'PositiveBSpline{n}',basis[te]@coefficients*s[te])
        except RuntimeError:pass
    finite=[m for m in models if np.isfinite(m['Nrmse']) and np.isfinite(m['Maximum']) and m['Negative']==0]
    best=sorted(finite,key=lambda p:p['Nrmse'])[:5]
    report.append(dict(Material=name,Best=best,Models=models));print(name,best,flush=True)
(root/'calibration-models.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
