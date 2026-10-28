"""Offline verification only; does not trace rays or execute optimization."""
import json,hashlib
from pathlib import Path
root=Path(__file__).parent;read=lambda p:json.loads(p.read_text())
p=read(root/'protocol.json');original=read(Path(p['Endpoint']));expected=read(Path(p['Input']));s=read(root/'paired/summary.json');rows=[]
for key,h in [('Checkpoint','Sha256'),('Endpoint','EndpointSha256'),('Input','InputSha256')]:assert hashlib.sha256(Path(p[key]).read_bytes()).hexdigest()==p[h]
inputs=[read(root/f'paired/v{v}/input.json') for v in [5,11]]
for i in inputs:
 assert i['Source']==original['Topology']
 for key in ['Variables','Targets','Constraints']:assert i[key]==expected[key]
for stage in ['Local','Full']:
 a=dict(inputs[0][stage]);b=dict(inputs[1][stage]);a.pop('TransmissionPolicyVersion');b.pop('TransmissionPolicyVersion');assert a==b
for x in s['Runs']:
 final=x['Final'];assert final['Completed'] and not final['MeetsTargets']
 assert abs(max(f['RmsRadiusMillimeters'] for f in x['Initial']['Fields'])*1000-58.75208791787859)<1e-8
 assert abs(max(f['MaximumRadiusMillimeters'] for f in x['Initial']['Fields'])*1000-167.34036843237768)<1e-8
 assert x['ActualRays']==sum(x[k] for k in ['InitialRays','LocalRays','FullRays','FinalRays'])<=2000000
 assert x['FunctionEvaluations']<=600 and x['GradientEvaluations']<=100 and x['ActualSeconds']<=60
 assert x['LocalRays']<=400000 and x['FullRays']<=400000 and x['FinalRays']<=800000
 for stage in ['local','full']:
  state=read(root/f'paired/v{x["Version"]}/{stage}.json')
  for q,v in zip(state['Continuation']['Adam']['Coordinates'],expected['Variables']):assert v['LowerBound']/v['Scale']<=q<=v['UpperBound']/v['Scale']
 rows.append(dict(version=x['Version'],rmsUm=max(f['RmsRadiusMillimeters'] for f in final['Fields'])*1000,maximumUm=max(f['MaximumRadiusMillimeters'] for f in final['Fields'])*1000,distortionPercent=max(abs(f['DistortionFraction']) for f in final['Fields'])*100,rays=x['ActualRays'],functions=x['FunctionEvaluations'],gradients=x['GradientEvaluations'],seconds=x['ActualSeconds'],accepted=x['AcceptedUpdates'],allValid=all(f['ValidRays']==f['AttemptedRays']==960 and f['CheckValidRays']==f['CheckAttemptedRays']==99 for f in final['Fields']),violations=[v['Code'] for v in final['Violations']]))
assert s['Runs'][0]['Final']==s['Runs'][1]['Final']
assert s['Runs'][0]['Final']==read(root.parent/'two-element-tangent-correction/paired/summary.json')['Runs'][0]['Final']
result=dict(verified=True,sourceSha256=p['EndpointSha256'],runs=rows,totalSearchRaysAcrossTwoSeparateBudgets=sum(x['rays'] for x in rows),testDiagnosticRays=None,notes=['Six independent saved-data LP diagnostics and the reproduction regression used zero rays.','Nine fallback regressions passed; optical mapping/resume tests were separate from search and did not persist exact ray counters. Their stage function/gradient limits remain enforced; do not report this work as zero.','v11 retained as explicit bounded planar solver fallback, default unchanged; paired final assessment identical to v5.','Neither result qualifies; not proof of physical impossibility.'])
(root/'verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
