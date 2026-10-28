"""Read-only reduction-boundary evidence aggregation. No tracing/optimization."""
import hashlib,json
from pathlib import Path
root=Path(__file__).resolve().parents[4]
out=Path(__file__).parent
sources={
 'qualifiedParent':'validation/rechecks/2026-10-28/three-to-two-continuation/paired-runs/preparation.json',
 'deletionCoverage':'validation/rechecks/2026-10-28/three-to-two-continuation/verification.json',
 'originalInput':'validation/rechecks/2026-10-28/two-element-field-conflict/paired-runs/v5/input.json',
 'originalEndpoint':'validation/rechecks/2026-10-28/two-element-field-conflict/paired-runs/v5/full.json',
 'latestPair':'validation/rechecks/2026-10-28/two-element-primary-first/paired/summary.json'}
data={k:json.loads((root/v).read_text()) for k,v in sources.items()}
a=data['qualifiedParent']['InitialAssessment'];b=data['originalInput'];assert a['Completed'] and a['MeetsTargets'] and not a['Violations'];assert a['Targets']==b['Targets'] and a['StructuralConstraints']==b['Constraints']
parent=data['qualifiedParent']['Source'];assert len(parent['Elements'])==3
assert all(parent[k]==b['Source'][k] for k in ['Fields','Wavelengths','Stop','Image','Reference'])
assert len(a['Fields'])==35 and all(f['ValidRays']==f['AttemptedRays']==960 and f['CheckValidRays']==f['CheckAttemptedRays']==99 for f in a['Fields'])
assert hashlib.sha256((root/sources['originalEndpoint']).read_bytes()).hexdigest()=='091e12a43c57e531e1c80d6beca77206c9f377b6fc820a46d3c4edbeb63a0984'
assert data['deletionCoverage']['QualifiedTwoElementRuns']==0 and data['deletionCoverage']['QualifiedSourceRetained']
metrics=lambda a:dict(rmsUm=max(f['RmsRadiusMillimeters'] for f in a['Fields'])*1000,maximumUm=max(f['MaximumRadiusMillimeters'] for f in a['Fields'])*1000,distortionPercent=max(abs(f['DistortionFraction']) for f in a['Fields'])*100,qualified=a['MeetsTargets'])
runs=data['latestPair']['Runs'];assert [r['Version'] for r in runs]==[5,14] and all(not r['Final']['MeetsTargets'] for r in runs)
result=dict(verified=True,decision='Stop current reduction at the qualified three-element parent; no two-to-one dispatch.',minimumVerifiedElementCount=3,globalMinimumProven=False,parent=metrics(a),latestTwoElementArms=[dict(version=r['Version'],**metrics(r['Final'])) for r in runs],scope='Engineering stop under frozen constraints and bounded attempts; branch coverage and local solver failure do not certify global infeasibility.',additionalOpticalEvaluations=0,sources=[dict(role=k,path=v,sha256=hashlib.sha256((root/v).read_bytes()).hexdigest()) for k,v in sources.items()])
(out/'decision.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
