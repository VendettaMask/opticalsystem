"""Independent audit, never a product dependency. Frozen inputs are read-only."""
from pathlib import Path
import json
import numpy as np
from scipy.io import loadmat
from scipy.interpolate import CubicSpline, PchipInterpolator
from numpy.polynomial.legendre import leggauss
from scipy.spatial import cKDTree
root=Path(__file__).resolve().parent
repo=root.parents[3]
old=root.parent/'xyz-angular'
sources={n:repo/'validation/rechecks/2026-11-01/xyz-radiometry/measured'/f'{n}.astm' for n in ['garnet_red','krylon_blue','acryl_blue']}
sources['mystique']=repo/'validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm'
sources['cayman']=repo/'validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm'
report=[]
nodes,weights=leggauss(12)
for name,path in sources.items():
    a=np.array([[float(v) for v in s.split(',')] for s in path.read_text().splitlines() if s and s[0].isdigit()])
    powers=[]
    for incidence in np.unique(a[:,0]):
        rows=a[a[:,0]==incidence]; theta=np.unique(rows[:,2]); mu=np.cos(theta)[::-1]; rings=[]
        for t in theta[::-1]:
            ring=rows[rows[:,2]==t]; phi=np.mod(ring[:,3],2*np.pi); ids=np.argsort(phi)
            rings.append((phi[ids],ring[ids,19]))
        # Adjacent ring union only; numerical Gauss12 and Gauss24 convergence below.
        for scale in [1e-6]:
            totals=[]
            for order in [12,24]:
                gauss,ws=leggauss(order); total=0
                for i in range(len(mu)-1):
                    axis=np.unique(np.r_[0,2*np.pi,rings[i][0],rings[i+1][0]])
                    g0=np.interp(axis,rings[i][0],np.log1p(rings[i][1]/scale),period=2*np.pi)
                    g1=np.interp(axis,rings[i+1][0],np.log1p(rings[i+1][1]/scale),period=2*np.pi)
                    q=(gauss+1)/2
                    for j in range(len(axis)-1):
                        l0=g0[j]*(1-q)+g0[j+1]*q;l1=g1[j]*(1-q)+g1[j+1]*q
                        g=l0[:,None]*(1-q[None,:])+l1[:,None]*q[None,:]
                        f=scale*np.expm1(g); mus=mu[i]*(1-q)+mu[i+1]*q
                        total+=np.sum(f*mus[None,:]*ws[:,None]*ws[None,:])*(mu[i+1]-mu[i])*(axis[j+1]-axis[j])/4
                totals.append(total)
            powers.append(dict(IncidenceDegrees=float(np.degrees(incidence)),Power=float(totals[-1]),Convergence=float(abs(totals[1]-totals[0]))))
    pairs=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    angles=np.array([list(map(float,s.split('/'))) for s in pairs[:,0]])
    xyz=pairs[:,1:4].astype(float); total=xyz.sum(1); target=pairs[:,4].astype(float); val=pairs[:,5]=='1'
    ratio=np.divide(target,total,out=np.zeros_like(target),where=total>0)
    scores=[]
    colour=np.divide(xyz,total[:,None],out=np.zeros_like(xyz),where=total[:,None]>0)
    base_train=(~val)&(total>0);base_dist,base_ids=cKDTree(colour[base_train]).query(colour,k=6)
    base_weights=(base_dist[:,:1]/np.maximum(base_dist,1e-150))**2
    base_ratio=(base_weights*ratio[base_train][base_ids]).sum(1)/np.maximum(base_weights.sum(1),1e-300)
    for jj in np.flatnonzero(base_dist[:,0]==0):base_ratio[jj]=ratio[base_train][np.all(colour[base_train]==colour[jj],axis=1)].mean()
    for transform in ['ratio','logratio','sqrt-ratio']:
        for interp in ['periodic-linear','periodic-cubic','pchip']:
            predictions=np.zeros(len(pairs));missing=[]
            for j in np.flatnonzero(val):
                train=(~val)&(angles[:,0]==angles[j,0])&(angles[:,2]==angles[j,2])&(total>0)
                ids=np.flatnonzero(train)
                if total[j]==0:continue
                if len(ids)==0:
                    missing.append(int(j))
                    # A singleton ring has no training point at this incidence.
                    # Use training-only polar-axis interpolation across incidence,
                    # otherwise the original colour model. No held-out target is read.
                    other=(~val)&(angles[:,2]==angles[j,2])&(total>0)
                    ids2=np.flatnonzero(other)
                    if len(ids2)>1:
                        xx=np.cos(angles[ids2,0]);order=np.argsort(xx)
                        rr=float(np.interp(np.cos(angles[j,0]),xx[order],ratio[ids2[order]]))
                    else:rr=base_ratio[j]
                    predictions[j]=rr*total[j];continue
                phi=np.mod(angles[ids,3],2*np.pi); order=np.argsort(phi); phi=phi[order]; y=ratio[ids[order]]; query=angles[j,3]%(2*np.pi)
                if transform=='logratio':y=np.log1p(y/1e-6)
                if transform=='sqrt-ratio':y=np.sqrt(y)
                if len(y)==1:r=y[0]
                elif interp=='periodic-linear':r=np.interp(query,phi,y,period=2*np.pi)
                else:
                    xp=np.r_[phi[-2:]-2*np.pi,phi,phi[:2]+2*np.pi];yp=np.r_[y[-2:],y,y[:2]]
                    r=float((PchipInterpolator if interp=='pchip' else CubicSpline)(xp,yp)(query))
                if transform=='logratio':r=np.expm1(r)*1e-6
                if transform=='sqrt-ratio':r=r*r
                predictions[j]=r*total[j]
            err=predictions[val]-target[val]
            scores.append(dict(Transform=transform,Interpolation=interp,Nrmse=float(np.linalg.norm(err)/np.linalg.norm(target[val])),Maximum=float(np.max(abs(err))),Negative=int(np.sum(predictions[val]<0)),Missing=len(missing)))
    geometry=None
    matpath=old/'full-precision'/f'{name}.mat'
    if matpath.exists():
        mat=loadmat(matpath);di=mat['dir_in'];do=mat['dir_out']
        geometry=dict(InCosineError=float(np.max(abs(np.cos(a[:,0])-di[:,2]))),OutCosineError=float(np.max(abs(np.cos(a[:,2])-do[:,2]))),InSineError=float(np.max(abs(np.sin(a[:,0])-di[:,2]))),OutSineError=float(np.max(abs(np.sin(a[:,2])-do[:,2]))))
    report.append(dict(Material=name,Geometry=geometry,LogInterpolationScale=1e-6,Powers=powers,DirectionalRatioFits=scores))
    print(name,'powers',[(round(p['IncidenceDegrees']),round(p['Power'],8)) for p in powers], 'best-direction',min(scores,key=lambda s:s['Nrmse']))
    if geometry:print('geometry',geometry)
(root/'diagnosis.json').write_text(json.dumps(report,indent=2,allow_nan=False),encoding='utf-8')
