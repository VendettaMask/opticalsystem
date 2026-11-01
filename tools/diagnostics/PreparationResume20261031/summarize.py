"""Summarize a completed study without changing any optical inputs/results."""
from pathlib import Path
import hashlib
import json
import sys
import xml.etree.ElementTree as ET

repo=Path(sys.argv[1]).resolve()
root=repo/'validation/rechecks/2026-10-31/preparation-checkpoint'
study=root/sys.argv[2]
rows=[]
for seed in range(42,47):
 compare=json.loads((study/f'compare{seed}.json').read_text(encoding='utf-8-sig'))
 for arm in ('continuous','paused'):
  folder=study/arm/f'seed{seed}'
  state=json.loads((folder/'run.preparation.json').read_text())['checkpoint']
  assessment=state['assessment']
  work=state['consumed']
  hours,minutes,seconds=work['elapsed'].split(':')
  active_seconds=int(hours)*3600+int(minutes)*60+float(seconds)
  assert work['functionEvaluations']<=600 and work['gradientEvaluations']<=600 and work['tracedRays']<=10000000 and active_seconds<=600,(seed,arm,'original caps')
  measured=json.loads((folder/'segment.json').read_text())['actualSegmentRays']
  if arm=='paused':measured+=json.loads((folder/'resumed-segment.json').read_text())['actualSegmentRays']
  assert measured==work['tracedRays'],(seed,arm,'actual ray settlement')
  assert state['status']=='completed' and state['result']['seeds'],(seed,arm,'original qualification')
  assert compare['exactPhysicalAndDiscreteState'] and not compare['errors'],(seed,'exact comparison')
  rows.append(dict(seed=seed,arm=arm,exact=True,work=work,randomState=state['randomState'],
   plan=state['planFingerprint'],protocol=state['protocolFingerprint'],kernel=state['kernelFingerprint'],
   persistence=state['persistenceElapsed'],originalQualified=assessment['completed'] and assessment['meetsTargets'],
   rmsMicrometers=max(f['rmsRadiusMillimeters'] for f in assessment['fields'])*1000,
   maximumMicrometers=max(f['maximumRadiusMillimeters'] for f in assessment['fields'])*1000,
   lostChecks=sum(f['checkAttemptedRays']-f['checkValidRays'] for f in assessment['fields']),
   actualSegmentSum=measured))
audits={}
for name in ('audit-frozen','audit-current','audit-product'):
 data=json.loads((root/name/'audit.json').read_text())
 assert not data['errors'] and data['completeStudy'],name
 audits[name]=dict(replays=data['replayCount'],runs=len(data['runs']),errors=data['errors'])
tests=[]
namespace={'v':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for file in sorted((root/'tests').glob('final-*.trx')):
 xml=ET.parse(file);summary=xml.find('v:ResultSummary',namespace);counters=summary.find('v:Counters',namespace)
 tests.append(dict(path=file.relative_to(repo).as_posix(),outcome=summary.attrib['outcome'],current=file.name in ('final-checkpoint-regression.trx','final-product-integration.trx'),**counters.attrib))
for required in ('final-checkpoint-regression.trx','final-product-integration.trx'):
 entry=next(t for t in tests if t['path'].endswith('/'+required))
 assert int(entry['executed'])>0 and entry['executed']==entry['passed'] and int(entry['failed'])==0 and int(entry['notExecuted'])==0,required
for label in ('test','product'):
 after=json.loads((root/f'tests/{label}-runtime-after.json').read_text(encoding='utf-8-sig'))
 assert all(item['Unchanged'] for item in after),label+' runtime drift'
f4=json.loads((root/'f4-original/run.preparation.json').read_text())['checkpoint']
assert f4['stopReason']=='OriginalExternalStopApertureConflict' and f4['consumed']['tracedRays']==0
reentry=json.loads((study/'reentry-2026-11-01.json').read_text(encoding='utf-8-sig')) if (study/'reentry-2026-11-01.json').exists() else None
result=dict(study=study.relative_to(repo).as_posix(),complete=True,rows=rows,independentAudits=audits,tests=tests,externalInterruption=reentry,
 f4=dict(reason=f4['stopReason'],work=f4['consumed']),originalGatesChanged=False,budgetsReset=False,
 failedOrIncompleteStudiesRetained=['final-paired-v1','final-paired-v2','final-paired-v3'],
 exactMeaning='RNG/pool/stages/endpoints/originalQA/all discrete work; measured elapsed is monotone, not bit-compared.',
 boundary='Normal saved stop, same numerical runtime; unknown lost in-flight reservations charged once and stopped.')
destination=root/'summary.json'
if destination.exists():raise SystemExit('Never overwrite a final summary.')
destination.write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf8')
print(f'Five exact pairs, {len(rows)} original qualified runs; summary saved.')
