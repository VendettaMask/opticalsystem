"""Independent diagnostic only; never a product computation dependency."""
from pathlib import Path
import json
import numpy as np
from scipy.interpolate import LinearNDInterpolator, RBFInterpolator
from scipy.spatial import cKDTree
root=Path(__file__).resolve().parent
old=root.parent/'xyz-conditions'/'final-positive'
report=[]
for material in ['garnet_red','krylon_blue','acryl_blue','mystique','cayman']:
    rows=np.loadtxt(old/material/'known-conditions-pairs.csv',delimiter=',',skiprows=1,dtype=str)
    xyz=rows[:,1:4].astype(float); f=rows[:,4].astype(float); val=rows[:,5]=='1'
    total=xyz.sum(axis=1); colour=np.divide(xyz,total[:,None],out=np.zeros_like(xyz),where=total[:,None]>0)
    ratio=np.divide(f,total,out=np.zeros_like(f),where=total>0)
    train=(~val)&(total>0); test=val&(total>0); x=colour[train,:2]; y=ratio[train]; q=colour[test,:2]
    def score(name,p):
        err=p*total[test]-f[test]
        return dict(Method=name,Nrmse=float(np.linalg.norm(err)/np.linalg.norm(f[val])),Max=float(np.max(np.abs(err))),Negative=int(np.count_nonzero(p<0)))
    models=[]
    linear=LinearNDInterpolator(x,y)(q); missing=~np.isfinite(linear)
    linear[missing]=y[cKDTree(x).query(q[missing])[1]]
    models.append(score('DelaunayLinearWithNearestOutside',linear)); models[-1]['Outside']=int(missing.sum())
    tree=cKDTree(colour[train]); ds,ids=tree.query(colour[test],k=64)
    for k in [1,3,6,12,24,48]:
        for power in [1,2,4,8]:
            w=np.power(ds[:,:1]/np.maximum(ds[:,:k],1e-150),power)
            estimate=np.sum(w*y[ids[:,:k]],axis=1)/np.maximum(w.sum(axis=1),1e-300)
            for ii in np.flatnonzero(ds[:,0]==0): estimate[ii]=np.mean(y[np.all(colour[train]==colour[test][ii],axis=1)])
            models.append(score(f'K{k}Power{power}',estimate))
    # This investigates model error only. Original policy and split are unchanged.
    for k in [12,24,48]:
        for kernel in ['thin_plate_spline','cubic']:
            try:
                p=np.exp(RBFInterpolator(x,np.log(np.maximum(y,1e-12)),neighbors=k,kernel=kernel)(q))
                models.append(score(f'LogRbf{k}{kernel}',p))
            except Exception as e: models.append(dict(Method=f'LogRbf{k}{kernel}',Error=str(e)))
    angles=np.array([[float(s) for s in key.split('/')] for key in rows[:,0]])
    # Geometry is an independent measured covariate; this is a different,
    # explicitly direction-conditioned calibration, never an XYZ-only claim.
    direction=np.column_stack((np.cos(angles[:,0]),np.sin(angles[:,2])*np.cos(angles[:,3]),np.sin(angles[:,2])*np.sin(angles[:,3]),np.cos(angles[:,2])))
    for feature_name,features in [('ColourDirection',np.column_stack((colour[:,:2],direction))),('Direction',direction),('ColourStrengthDirection',np.column_stack((colour[:,:2],np.log(np.maximum(total,1e-12)),direction)))]:
        scale=np.std(features[train],axis=0); scale[scale<1e-12]=1
        a=features[train]/scale; b=features[test]/scale
        for k in [24,48,96]:
            for kind,targets in [('LogRatio',np.log(np.maximum(ratio[train],1e-12))),('Ratio',ratio[train])]:
                try:
                    prediction=RBFInterpolator(a,targets,neighbors=k,kernel='thin_plate_spline',degree=0)(b)
                    if kind=='LogRatio': prediction=np.exp(prediction)
                    models.append(score(f'{feature_name}{kind}Rbf{k}',prediction))
                except Exception as e: models.append(dict(Method=f'{feature_name}{kind}Rbf{k}',Error=str(e)))
    for feature_name,features in [('AbsoluteXYZ',xyz),('LogXYZ',np.log(np.maximum(xyz,1e-12))),('ColourLogStrength',np.column_stack((colour[:,:2],np.log(np.maximum(total,1e-12)))) )]:
        scale=np.std(features[train],axis=0); scale[scale==0]=1
        a=features[train]/scale; b=features[test]/scale
        for k in [12,24,48,96]:
            try:
                # Positive exponential local interpolation. No measurement is changed.
                prediction=np.exp(RBFInterpolator(a,np.log(np.maximum(f[train],1e-12)),neighbors=k,kernel='thin_plate_spline')(b))
                err=prediction-f[test]
                models.append(dict(Method=f'{feature_name}LogRbf{k}',Nrmse=float(np.linalg.norm(err)/np.linalg.norm(f[val])),Max=float(np.max(np.abs(err))),Negative=0))
            except Exception as e: models.append(dict(Method=f'{feature_name}LogRbf{k}',Error=str(e)))
        for k in [24,48,96]:
            for kernel in ['thin_plate_spline','cubic']:
                for transform in ['SquareRoot','Linear']:
                    try:
                        targets=np.sqrt(f[train]) if transform=='SquareRoot' else f[train]
                        prediction=RBFInterpolator(a,targets,neighbors=k,kernel=kernel)(b)
                        if transform=='SquareRoot': prediction=prediction**2
                        err=prediction-f[test]
                        models.append(dict(Method=f'{feature_name}{transform}{kernel}{k}',Nrmse=float(np.linalg.norm(err)/np.linalg.norm(f[val])),Max=float(np.max(np.abs(err))),Negative=int(np.count_nonzero(prediction<0))))
                    except Exception as e: models.append(dict(Method=f'{feature_name}{transform}{kernel}{k}',Error=str(e)))
    report.append(dict(Material=material,Models=models,AngularAxes=[np.unique(angles[:,j]).tolist() for j in range(4)],
                       SpectralZeros=int(np.count_nonzero(f==0)),Best=sorted([m for m in models if 'Nrmse' in m],key=lambda m:m['Nrmse'])[:4]))
    print(material,json.dumps(report[-1]['Best']))
(root/'exploration.json').write_text(json.dumps(report,indent=2))
