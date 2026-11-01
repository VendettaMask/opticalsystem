"""Compare frozen optical outputs and actual work, without optical calculations."""
from pathlib import Path
import json,sys,statistics
root=Path(sys.argv[1]).resolve();candidate=Path(sys.argv[2]).resolve();rows=[];errors=[]
def seconds(value):
 h,m,s=value.split(':');return int(h)*3600+int(m)*60+float(s)
def no_time(value):
 if isinstance(value,dict):return {k:no_time(v) for k,v in value.items() if k not in ('elapsed','persistenceElapsed')}
 if isinstance(value,list):return [no_time(v) for v in value]
 return value
for seed in range(42,47):
 a=json.loads((root/'baseline'/f'seed{seed}'/'run.preparation.json').read_text())['checkpoint']
 b=json.loads((candidate/f'seed{seed}'/'run.preparation.json').read_text())['checkpoint']
 difference=[]
 for field in ('planFingerprint','protocolFingerprint','randomState','stage','cursor','status','stopReason','endpoint','assessment','candidates','phases','consumed'):
  if no_time(a[field])!=no_time(b[field]):difference.append(field)
 for arm,state in (('baseline',a),('candidate',b)):
  work=state['consumed']
  if not(state['status']=='completed' and state['result']['seeds'] and work['functionEvaluations']<=600 and work['gradientEvaluations']<=600 and work['tracedRays']<=10000000 and seconds(work['elapsed'])<=600):
   difference.append(arm+' original qualification/caps')
 if difference:errors.append(dict(seed=seed,differences=difference))
 first=seconds(a['consumed']['elapsed']);second=seconds(b['consumed']['elapsed'])
 rows.append(dict(seed=seed,exact=not difference,baselineSeconds=first,candidateSeconds=second,speedup=first/second,
  relativeTimeSaved=1-second/first,work=b['consumed'],lostChecks=sum(x['checkAttemptedRays']-x['checkValidRays'] for x in b['assessment']['fields'])))
result=dict(rows=rows,errors=errors,allOriginalQualified=not errors,medianSpeedup=statistics.median(x['speedup'] for x in rows),
 totalActiveSpeedup=sum(x['baselineSeconds'] for x in rows)/sum(x['candidateSeconds'] for x in rows),
 scope='Five fixed original F8 seeds, one frozen run per arm/seed; timings are observations, not a general speed guarantee.',
 originalGatesAndCapsChanged=False,comparesMeasuredElapsedExactly=False)
path=root/'comparison.json'
if path.exists():raise SystemExit('Never overwrite comparison evidence.')
path.write_text(json.dumps(result,indent=2),encoding='utf8')
print(json.dumps(result,indent=2))
if errors:raise SystemExit('Physical or discrete-work difference requires investigation.')
