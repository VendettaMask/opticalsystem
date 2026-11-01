"""Independent SciPy reference for unscaled original held-out XYZ witnesses."""
from pathlib import Path
import sys,json,hashlib,numpy as np
from scipy.interpolate import RBFInterpolator
repo=Path(__file__).resolve().parents[4];calroot=Path(sys.argv[1]);output=Path(sys.argv[2]);cases=[]
for file in sorted(calroot.glob('*/*/approved-550-calibration.json')):
    config,name=file.parent.parent.name,file.parent.name;cal=json.loads(file.read_text(encoding='utf-8-sig'));m=cal['AbsoluteRbfModel']
    rows=np.loadtxt(repo/'validation/rechecks/2026-11-01/xyz-conditions/final-positive'/name/'known-conditions-pairs.csv',delimiter=',',skiprows=1,dtype=str)
    xyz=rows[:,1:4].astype(float);s=xyz.sum(1);eligible=(rows[:,5]=='1')&(s>0)&(xyz.max(1)*np.pi<1)
    ids=np.flatnonzero(eligible);idx=ids[np.argmax(s[ids])];q=xyz[idx];nodes=np.array([[p['X'],p['Y'],p['Z']] for p in m['Nodes']]);targets=np.array([p['RadiometricValues'][0] for p in m['Nodes']])
    def feature(x):
        strength=x.sum(1)
        return (np.log(x+1e-12) if m['Features']==0 else np.column_stack((x[:,0]/strength,x[:,1]/strength,np.log(strength+1e-12))))/np.array(m['CoordinateScales'])
    reference=float(np.exp(RBFInterpolator(np.ascontiguousarray(feature(nodes)),np.log(targets+m['ResponseOffsetPerSteradian']),neighbors=m['NeighbourCount'],kernel='thin_plate_spline',smoothing=m['Smoothing'])(feature(q[None,:]))[0]))
    cases.append(dict(Material=name,Configuration=config,CalibrationSha256=hashlib.sha256(file.read_bytes()).hexdigest(),CalibrationRelativePath=str(file.relative_to(calroot)).replace('\\','/'),SampleId=rows[idx,0],Xyz=q.tolist(),MeasuredTargetPerSteradian=float(rows[idx,4]),IndependentPredictionPerSteradian=reference,IntensityRescaled=False))
output.write_text(json.dumps(dict(Reference='SciPy TPS local RBF; original development holdout, not new blind data',Cases=cases),indent=2),encoding='utf-8')
