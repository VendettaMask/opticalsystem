from pathlib import Path
import numpy as np,json
from scipy.interpolate import LinearNDInterpolator,CloughTocher2DInterpolator
from scipy.spatial import cKDTree
root=Path(__file__).resolve().parent;result=[]
for name in ['garnet_red','krylon_blue','acryl_blue','mystique','cayman']:
    rows=np.loadtxt(root.parent/'xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',dtype=str,delimiter=',',skiprows=1)
    x=rows[:,1:4].astype(float);s=x.sum(1);f=rows[:,4].astype(float);v=rows[:,5]=='1';tr=(~v)&(s>0);te=v&(s>0);c=np.divide(x,s[:,None],out=np.zeros_like(x),where=s[:,None]>0)
    models=[]
    for fn,xx in [('XYZ',x),('LogXYZ',np.log(x+1e-10)),('ColourLogS',np.column_stack((c[:,:2],np.log(s+1e-10))))]:
        xx=xx/xx[tr].std(0);ds,ids=cKDTree(xx[tr]).query(xx[te],k=6);w=1/np.maximum(ds,1e-12)**2
        for response in ['f','ratio','logf']:
            y=f[tr] if response=='f' else f[tr]/s[tr] if response=='ratio' else np.log(f[tr]+1e-6)
            interp=LinearNDInterpolator(xx[tr],y);p=interp(xx[te]);missing=~np.isfinite(p);p[missing]=(w[missing]*y[ids[missing]]).sum(1)/w[missing].sum(1)
            if response=='ratio':p*=s[te]
            if response=='logf':p=np.exp(p)
            e=p-f[te];models.append(dict(Feature=fn,Response=response,Nrmse=float(np.linalg.norm(e)/np.linalg.norm(f[v])),Maximum=float(np.max(abs(e))),Missing=int(missing.sum()),Negative=int(np.sum(p<0))))
    result.append(dict(Material=name,Models=models));print(name,sorted(models,key=lambda m:m['Nrmse'])[:3],flush=True)
(root/'tetra-check.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
