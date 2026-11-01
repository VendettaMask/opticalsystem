"""Independent audit of the explicitly declared ring interpolant; diagnostics only."""
from pathlib import Path
import json
import numpy as np
root=Path(__file__).resolve().parent
repo=root.parents[3]
sources={n:repo/'validation/rechecks/2026-11-01/xyz-radiometry/measured'/f'{n}.astm' for n in ['garnet_red','krylon_blue','acryl_blue']}
sources['mystique']=repo/'validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm'
sources['cayman']=repo/'validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm'
report=[]
for name,path in sources.items():
    lines=path.read_text().splitlines(); a=np.array([[float(v) for v in s.split(',')] for s in lines if s and s[0].isdigit()])
    power=[]
    for incident in np.unique(a[:,0]):
        rows=a[a[:,0]==incident]; thetas=np.unique(rows[:,2]); phis=np.unique(np.r_[0,2*np.pi,np.mod(rows[:,3],2*np.pi)])
        mu=np.cos(thetas)[::-1]; values=[]
        for t in thetas[::-1]:
            ring=rows[rows[:,2]==t]; p=np.mod(ring[:,3],2*np.pi); ids=np.argsort(p); p=p[ids]; v=ring[ids,19]
            values.append(np.interp(phis,p,v,period=2*np.pi))
        values=np.array(values); total=0
        # Gauss2 independently integrates bilinear f times cosine exactly.
        for i in range(len(mu)-1):
            for g in [-1/np.sqrt(3),1/np.sqrt(3)]:
                t=(1+g)/2; f=values[i]*(1-t)+values[i+1]*t
                total+=np.trapezoid(f,phis)*(mu[i]*(1-t)+mu[i+1]*t)*(mu[i+1]-mu[i])/2
        power.append(dict(IncidentRadians=float(incident),IncidentDegrees=float(np.degrees(incident)),MeasuredRows=len(rows),Power=float(total),Passive=bool(total<=1)))
    report.append(dict(Material=name,Cases=power))
    print(name,[(round(p['IncidentDegrees'],3),round(p['Power'],6)) for p in power])
(root/'angular-power-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
