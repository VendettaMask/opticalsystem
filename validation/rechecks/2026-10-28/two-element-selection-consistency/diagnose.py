"""Saved Jacobian-only diagnostic. SciPy is an independent reference, never product code."""
import json,time
from pathlib import Path
import numpy as np
from scipy.optimize import linprog
r=Path(__file__).parent;old=r.parent
ids=json.loads((old/'two-element-active-constraints/diagnostic-final/diagnosis.json').read_text())['ResidualIds']
start=time.monotonic();result=[]
for name,path,phase in [('v10-local',old/'two-element-curvature-backtrack/paired/v10','local'),('v5-full',old/'two-element-curvature-backtrack/paired/v5','full')]:
 data=json.loads((path/(phase+'.json')).read_text());inp=json.loads((path/'input.json').read_text());val=data['EndpointEvaluation'];q=np.array(data['Continuation']['Adam']['Coordinates']);variables=inp['Variables'];raw=val['ConstraintTangents'];G=np.array([x['Derivatives'] for x in raw]);v=np.array([x['Value'] for x in raw]);rows=[x['Row'] for x in raw];clear=np.array([ids[i].endswith(':clearance') for i in rows]);rms=np.array([ids[i].endswith(':rms') for i in rows]);violation=np.maximum(v+rms*.02,0);violation[:2]=np.maximum(abs(v[:2])-.95,0);signed=G.copy();signed[:2]*=np.sign(v[:2,None]);merit=2*np.sum(signed[(~clear)&(violation>0)]*violation[(~clear)&(violation>0),None],axis=0)
 activeclear=[row for row in rows if ids[row].endswith(':clearance') and val['Residuals'][row]>1e-10]
 assert not activeclear
 def select(gradient):
  first=int(np.argmax(abs(gradient)));unit=G[:,first];unit=unit/np.linalg.norm(unit);remainder=G-unit[:,None]*(unit@G)[None,:];scores=np.sum(remainder*remainder,axis=0);scores[first]=-1;return [first,int(np.argmax(scores))]
 for selection in ['legacy','merit','all']:
  active=select(np.array(val['Gradient']) if selection=='legacy' else merit) if selection!='all' else list(range(len(q)))
  A=[];b=[]
  for k,row in enumerate(rows):
   if row<2:
    band=max(.95,abs(v[k]));A.extend([G[k,active],-G[k,active]]);b.extend([band-v[k],band+v[k]])
   else:A.append(G[k,active]);b.append(-min(v[k]+(.02 if rms[k] else 0),0))
  A=np.array(A);b=np.array(b);norm=np.linalg.norm(A,axis=1);mask=norm>1e-14;An=A[mask]/norm[mask,None];bn=b[mask]/norm[mask]
  bounds=[(max(-1,variables[i]['LowerBound']/variables[i]['Scale']-q[i]),min(1,variables[i]['UpperBound']/variables[i]['Scale']-q[i])) for i in active]
  c=merit[active];c=c/np.linalg.norm(c)
  lp=linprog(c,A_ub=An,b_ub=bn,bounds=bounds,method='highs',options={'time_limit':5,'primal_feasibility_tolerance':1e-9,'dual_feasibility_tolerance':1e-9})
  result.append(dict(point=name,selection=selection,active=active,success=lp.success,status=lp.message,normalizedMeritDescent=float(lp.fun) if lp.success else None,maxOriginalViolation=float(np.max(A@lp.x-b)) if lp.success else None,coordinates=lp.x.tolist() if lp.success else None,meritGradient=merit.tolist(),rawGradient=val['Gradient']))
(r/'diagnosis.json').write_text(json.dumps(dict(rays=0,seconds=time.monotonic()-start,linearPrograms=6,perProgramTimeCap=5,rows=result),indent=2)+'\n')
for x in result:print({k:x[k] for k in ['point','selection','active','success','normalizedMeritDescent','maxOriginalViolation']})
