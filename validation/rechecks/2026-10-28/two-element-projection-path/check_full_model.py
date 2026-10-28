"""One saved-data LP reference; no optical calls or production Python dependency."""
import json,time
from pathlib import Path
import numpy as np
from scipy.optimize import linprog
r=Path(__file__).parent;source=r.parent/'two-element-selection-consistency/paired/v5'
initial=json.loads((source/'full.json').read_text())['InitialEvaluation'];inp=json.loads((source/'input.json').read_text());audit=json.loads((r/'run-with-coordinates/audit.json').read_text());q=np.array(next(x for x in audit['Rows'] if x['Version']==5 and x['Phase']=='full-first')['InitialCoordinates']);ids=json.loads((r.parent/'two-element-active-constraints/diagnostic-final/diagnosis.json').read_text())['ResidualIds']
rows=initial['ConstraintTangents'];gradient=np.zeros(12);constraints=[]
for t in rows:
 i=t['Row'];v=t['Value'];n=np.array(t['Derivatives']);rms=ids[i].endswith(':rms');clearance=ids[i].endswith(':clearance')
 violation=max(abs(v)-.95,0) if i<2 else max(v+(.02 if rms else 0),0)
 if not clearance:gradient+=2*violation*(np.sign(v) if i<2 else 1)*n
 if i<2:
  band=max(.95,abs(v));constraints.extend([(v-band,n),(-v-band,-n)])
 else:constraints.append((min(v+(.02 if rms else 0),0),n))
for k,var in enumerate(inp['Variables']):
 n=np.zeros(12);n[k]=1;constraints.extend([(-min(1,var['UpperBound']/var['Scale']-q[k]),n),(-min(1,q[k]-var['LowerBound']/var['Scale']),-n)])
descent=gradient/np.linalg.norm(gradient);a=np.array([n for c,n in constraints]);b=-np.array([c for c,n in constraints]);norm=np.linalg.norm(a,axis=1);mask=norm>1e-14
start=time.monotonic();lp=linprog(descent,A_ub=a[mask]/norm[mask,None],b_ub=b[mask]/norm[mask],bounds=[(None,None)]*12,method='highs',options={'time_limit':5,'primal_feasibility_tolerance':1e-9,'dual_feasibility_tolerance':1e-9})
result=dict(success=lp.success,message=lp.message,seconds=time.monotonic()-start,timeCapSeconds=5,tracedRays=0)
if lp.success:
 constraints.append((.01,descent));maximum=max(c+float(n@lp.x) for c,n in constraints)
 result.update(normalizedDescent=float(lp.fun),maxOriginalViolation=maximum,witness=lp.x.tolist(),primaryExhaustedAt=.01)
 if maximum<=1e-10:
  fixture=dict(dimension=12,preferred=(-descent).tolist(),witness=lp.x.tolist(),constraints=[dict(offset=c,normal=n.tolist()) for c,n in constraints])
  (r/'full-feasible-cone.json').write_text(json.dumps(fixture,indent=2)+'\n')
(r/'full-model-reference.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
