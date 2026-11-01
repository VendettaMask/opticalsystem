"""Summarize saved SPC evidence only. No optical calculations or candidate modification."""
from pathlib import Path
import json, sys, statistics, hashlib

repo = Path(sys.argv[1]).resolve()
root = repo / 'validation/rechecks/2026-11-01/spc-branches'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def write(p, data):
    with p.open('x', encoding='utf8') as f: json.dump(data, f, indent=2, ensure_ascii=False)
def seconds(s):
    h,m,t=s.split(':'); return int(h)*3600+int(m)*60+float(t)
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
rows=[]; details={}; costs={}
for batch in ('formal','isolated-formal'):
    batchrows=[]
    for arm in ('direct','spc'):
        arms=[]
        for seed in range(42,47):
            directory=root/batch/f'{arm}-seed{seed}'
            study=read(directory/'study.spc-study.json')['study']
            s=read(directory/'summary.json'); r=study['result']; w=r['work']
            assert r['stopReason']=='Completed' and len(r['qualifiedBranchIds'])==2
            assert w['functionEvaluations']<=600 and w['gradientEvaluations']<=600 and w['tracedRays']<=2_000_000 and seconds(w['elapsed'])<=120
            assert study['budget']['maximumFunctionEvaluations']==600 and study['policy']['maximumGradientEvaluations']==600
            assert s['originalParentQualified'] is False and s['stationaryParentQualified'] is True
            audit=read(directory/'audit.json')
            assert not audit['searchBudgetInput'] and len(audit['audits'])==2
            for b in r['branches']:
                assert b['assessment']['completed'] and b['assessment']['meetsTargets']
                assert all(not e['transition'] and e['thicknessMillimeters']>0 for e in b['endpoint']['elements'])
                for stage in b['stages']:
                    assert stage['assessment']['targets']==study['plan']['targets'] and stage['assessment']['structuralConstraints']==study['plan']['constraints']
                if arm=='spc':
                    assert b['nullMinimum']['certified'] and b['nullMinimum']['stableNegativeDirections']==0
                    assert b['nullInteriorStationary'] and b['optimizedObjective']<b['initialObjective']
                else: assert b['nullMinimum'] is None and b['initialObjective'] is None
            for a in audit['audits']: assert a['independent']['meetsTargets'] and a['nativeReadback']['meetsTargets']
            if arm=='spc':
                parent=r['parents'][0]
                assert parent['parentStationarity']['certified'] and parent['saddle']['certified'] and parent['saddle']['stableNegativeDirections']==1
            row={'batch':batch,'arm':arm,'seed':seed,'work':w,'qualifiedBranches':2,'endpointFingerprints':[b['endpointFingerprint'] for b in s['branches']],
                 'worstRmsMillimeters':[b['worstRms'] for b in s['branches']], 'worstMaximumMillimeters':[b['worstMaximum'] for b in s['branches']]}
            rows.append(row);batchrows.append(row);arms.append(row)
            details[(batch,arm,seed)]=study
        assert len({tuple(r['endpointFingerprints']) for r in arms})==1
    costs[batch]={'functions':sum(r['work']['functionEvaluations'] for r in batchrows), 'gradients':sum(r['work']['gradientEvaluations'] for r in batchrows),
        'rays':sum(r['work']['tracedRays'] for r in batchrows),'activitySeconds':sum(seconds(r['work']['elapsed']) for r in batchrows)}
for seed in range(42,47):
    a=details[('isolated-formal','direct',seed)];b=details[('isolated-formal','spc',seed)]
    assert a['plan']==b['plan'] and a['budget']==b['budget'] and a['policy']==b['policy']
    for arm in ('direct','spc'):
        old=details[('formal',arm,seed)]['result'];new=details[('isolated-formal',arm,seed)]['result']
        assert old['parents']==new['parents']
        for x,y in zip(old['branches'],new['branches']):
            assert x['endpoint']==y['endpoint'] and x['assessment']==y['assessment']
for folder in ('original-tasks','isolated-original-tasks'):
    originals=read(root/folder/'summary.json')
    assert originals['originalF8QualifiedEndpointsRecheckedExactly']==5 and len(originals['originalGuards'])==15
    for r in originals['originalGuards']: assert r['work']['tracedRays']==r['work']['functionEvaluations']==0
    for seed in range(42,47): assert read(root/folder/f'old-F8-qualified-seed{seed}.json')['exactSameAssessment']
reopen=read(root/'isolated-new-process-reopen.json');assert reopen['studies']==10 and reopen['rays']==0
spc=details[('isolated-formal','spc',42)]['result'];direct=details[('isolated-formal','direct',42)]['result']
parent=spc['parents'][0]
development=[]
for path in sorted(root.glob('search-probe-v*.json')):
    result=read(path)['result'];development.append({'file':path.name,'work':result['work'],'stopReason':result['stopReason'],'qualified':len(result['qualifiedBranchIds'])})
runtime={}
for name in ('formal','isolated'):
    entries=read(root/f'{name}-runtime-sha256.json')
    for e in entries: assert digest(root/f'{name}-runtime'/e['name'])==e['sha256']
    runtime[name]={'files':len(entries),'unchanged':True}
out={'status':'verified','scope':'Explicit one-level air-separated SPC library API, declared curvature subspace only; no automatic desktop/cemented scheduling or SPC resume.',
 'frozenCaps':{'functions':600,'gradients':600,'actualCpuRays':2_000_000,'seconds':120},'rows':rows,'searchCostsByBatch':costs,'runtimeIntegrity':runtime,
 'uniqueQualifiedPhysicalEndpointsPerArm':{'direct':2,'spc':2},'seedsCollapseToSameTwoDesigns':True,'qualifiedBranchesPerArmPerBatch':10,
 'parentRmsMillimeters':max(f['rmsRadiusMillimeters'] for f in parent['stationaryAssessment']['fields']),
 'saddleEigenvalues':parent['saddle']['fine']['eigenvalues'],'saddleCurvatureUncertainty':parent['saddle']['curvatureUncertainty'],
 'spcBranchMinimumEigenvalues':[b['nullMinimum']['fine']['eigenvalues'] for b in spc['branches']],
 'spcWorstRmsMillimeters':[max(f['rmsRadiusMillimeters'] for f in b['assessment']['fields']) for b in spc['branches']],
 'directWorstRmsMillimeters':[max(f['rmsRadiusMillimeters'] for f in b['assessment']['fields']) for b in direct['branches']],
 'spcFailedMaterialStages':[{'sign':b['sign'],'stage':s['name'],'material':s['material'],'violations':s['assessment']['violations']} for b in spc['branches'] for s in b['stages'] if not s['assessment']['meetsTargets']],
 'originalGuardsPerBatch':15,'originalF8ExactRechecksPerBatch':5,'physicalIndependentAndNativeChecksPerBatch':40,
 'newProcessReadbackRays':0,'newOriginalTaskSuccesses':0,'fewerLensOrParentParetoBenefit':False,'developmentProbes':development,
 'claimGeneralSpeedup':False,'gatesAperturesAndBudgetsRelaxed':False}
write(root/(sys.argv[2] if len(sys.argv)>2 else 'comparison.json'),out)
print(json.dumps({k:out[k] for k in ('searchCostsByBatch','saddleEigenvalues','parentRmsMillimeters','spcWorstRmsMillimeters','directWorstRmsMillimeters')},indent=2))
