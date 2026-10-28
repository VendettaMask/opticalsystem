"""Offline verification only; does not trace rays or execute optimization."""
import json,hashlib
from pathlib import Path
root=Path(__file__).parent;read=lambda p:json.loads(p.read_text())
p=read(root/'protocol.json');original=read(Path(p['Endpoint']));expected=read(Path(p['Input']));s=read(root/'paired/summary.json');rows=[]
for key,h in [('Checkpoint','Sha256'),('Endpoint','EndpointSha256'),('Input','InputSha256')]:assert hashlib.sha256(Path(p[key]).read_bytes()).hexdigest()==p[h]
inputs=[read(root/f'paired/v{v}/input.json') for v in [5,14]]
for i in inputs:
 assert i['Source']==original['Topology']
 for key in ['Variables','Targets','Constraints']:assert i[key]==expected[key]
for stage in ['Local','Full']:
 a=dict(inputs[0][stage]);b=dict(inputs[1][stage]);a.pop('TransmissionPolicyVersion');b.pop('TransmissionPolicyVersion');assert a==b
for x in s['Runs']:
 final=x['Final'];assert final['Completed']
 assert abs(max(f['RmsRadiusMillimeters'] for f in x['Initial']['Fields'])*1000-58.75208791787859)<1e-8
 assert abs(max(f['MaximumRadiusMillimeters'] for f in x['Initial']['Fields'])*1000-167.34036843237768)<1e-8
 assert x['ActualRays']==sum(x[k] for k in ['InitialRays','LocalRays','FullRays','FinalRays'])<=2000000
 assert x['FunctionEvaluations']<=600 and x['GradientEvaluations']<=100 and x['ActualSeconds']<=60
 assert x['LocalRays']<=400000 and x['FullRays']<=400000 and x['FinalRays']<=800000
 for stage in ['local','full']:
  state=read(root/f'paired/v{x["Version"]}/{stage}.json')
  for q,v in zip(state['Continuation']['Adam']['Coordinates'],expected['Variables']):assert v['LowerBound']/v['Scale']<=q<=v['UpperBound']/v['Scale']
 rows.append(dict(version=x['Version'],meetsTargets=final['MeetsTargets'],rmsUm=max(f['RmsRadiusMillimeters'] for f in final['Fields'])*1000,maximumUm=max(f['MaximumRadiusMillimeters'] for f in final['Fields'])*1000,distortionPercent=max(abs(f['DistortionFraction']) for f in final['Fields'])*100,rays=x['ActualRays'],functions=x['FunctionEvaluations'],gradients=x['GradientEvaluations'],seconds=x['ActualSeconds'],accepted=x['AcceptedUpdates'],allValid=all(f['ValidRays']==f['AttemptedRays']==960 and f['CheckValidRays']==f['CheckAttemptedRays']==99 for f in final['Fields']),violations=[v['Code'] for v in final['Violations']]))
assert s['Runs'][0]['Final']==read(root.parent/'two-element-tangent-correction/paired/summary.json')['Runs'][0]['Final']
audit=read(root/'native-audit/native-audits.json');assert all(x['Matches'] for x in audit)
chain=[]
for phase in ['local','full']:
 d=read(root/f'paired/v14/{phase}.json');suppressed=False;pending=False;fallbacks=0;consideredCount=0;acceptedFallbacks=0
 for step in d['Steps']:
  used=step['UsedFallbackCandidate'];considered=step['FallbackConsidered']
  assert not used or considered
  if suppressed:assert not considered and not used
  if pending:assert considered
  fallbacks+=used;consideredCount+=considered;acceptedFallbacks+=used and step['Accepted']
  suppressed,pending=(False,False) if step['Accepted'] else (suppressed or considered,not suppressed and not considered)
 assert d['Continuation'].get('SuppressFallbackAtCurrentPoint',False)==suppressed
 assert d['Continuation'].get('TryFallbackNext',False)==pending
 assert not any('fallbackAttempted=True' in m for m in d['Diagnostics'] if m.startswith('Projection attempt 1:'))
 chain.append(dict(phase=phase,fallbackCandidates=fallbacks,fallbackConsiderations=consideredCount,acceptedFallbacks=acceptedFallbacks,finalSuppression=suppressed,finalPending=pending))
triggers=[]
for version in [5,14]:
 for phase in ['local','full']:
  d=read(root/f'paired/v{version}/{phase}.json');messages=d['Diagnostics']
  triggers.append(dict(version=version,phase=phase,termination=d['Termination'],
   fallbackAttempts=sum('fallbackAttempted=True' in t for t in messages),
   feasibleFallbacks=sum('fallbackFeasible=True' in t for t in messages),
   selectedEvaluatedFallbacks=sum(t.startswith('Fallback candidate attempt') for t in messages),
   acceptedFallbacks=sum(t.startswith('Fallback candidate attempt') and 'accepted=True' in t for t in messages),
   acceptedSteps=d['AcceptedUpdates'],completedAttempts=len(d['Steps'])))
result=dict(verified=True,sourceSha256=p['EndpointSha256'],runs=rows,candidateChain=chain,
 totalSearchRaysAcrossTwoSeparateBudgets=sum(x['rays'] for x in rows),triggerCounts=triggers,
 additionalReadbackRays=sum(x['AdditionalAuditRays'] for x in audit),
 redRegressionRays=read(root/'red-summary.json')['rays'],
 greenRegressionRays=read(root/'green-summary.json')['rays'],
 notes=['All solver pivots run inside the original wall-time/deadline; no new optical evaluation path.',
 'Readback and regression costs are separate from the two fresh optimization arms.',
 'Fallback events without a completed candidate acceptance record may belong to the final quota-interrupted evaluation, not an accepted step.',
 'No default policy or Core changes; no physical impossibility claim.'])
(root/'verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
