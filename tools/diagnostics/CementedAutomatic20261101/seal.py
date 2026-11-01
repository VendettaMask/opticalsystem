"""Read saved evidence, resolve retained archives and seal; no optical calculations."""
from pathlib import Path
import hashlib,json,sys,zipfile,xml.etree.ElementTree as ET
repo=Path(sys.argv[1]).resolve();root=repo/'validation/rechecks/2026-11-01/cemented-automatic';final=root/'final-authority'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def write(p,d):
 with p.open('x',encoding='utf8') as f:json.dump(d,f,indent=2,ensure_ascii=False)
def seconds(t):
 h,m,s=t.split(':');return int(h)*3600+int(m)*60+float(s)
assert not (root/'final-verification.json').exists(),'Never overwrite a sealed scope.'
summary=read(final/'verification-summary.json')
assert summary['defaultFullBuildPassed'] and summary['unchangedBeforeAfter'] and summary['readLeaseHeld'] and summary['defaultEqualsExecutedTestCopies']
assert not summary['foregroundApplicationStarted'] and not summary['userProcessesClosed']
for name in ['test-build.log','default-app-build.log','driver-build.log']:
 b=(final/name).read_text(encoding='utf-8-sig');assert '0 个警告' in b and '0 个错误' in b,name
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests=ET.parse(final/'tests/cemented-final.trx').findall('.//t:UnitTestResult',ns)
assert len(tests)==147 and all(t.get('outcome')=='Passed' for t in tests)
before={x['name']:x['sha256'] for x in read(final/'default-runtime-before.json')}
assert len(before)==8 and before=={x['name']:x['sha256'] for x in read(final/'default-runtime-after.json')}
audits=list((final/'runtime-audits').glob('*.json'));assert len(audits)==47,len(audits)
drivers=set()
for p in audits:
 d=read(p);assert not d['ForegroundWindowStarted'];drivers.add(d['ScenarioDriverSha256'])
 assert {x['Name']:x['Sha256'] for x in d['ProductAssemblies']}==before,p
 for x in d['ProductAssemblies']:assert Path(x['Path']).resolve()==(repo/'src/OpticalSystemDesign.App/bin/Release/net10.0'/x['Name']).resolve()
assert len(drivers)==1 and next(iter(drivers))==sha(final/'scenario-driver.dll')
archives={}
for kind in ('source','runtime'):
 rows=read(final/f'default-{kind}-sha256.json');storage=read(final/f'default-{kind}-storage.json')
 assert {r['path']:r['sha256'] for r in rows}=={r['path']:r['sha256'] for r in storage}
 for r in storage:
  with zipfile.ZipFile(root/r['archive']) as z:assert hashlib.sha256(z.read(r['path'])).hexdigest()==r['sha256']
 archives[kind]={'entries':len(rows),'storageIndex':f'final-authority/default-{kind}-storage.json'}
source={r['path']:r['sha256'] for r in read(final/'default-source-sha256.json')}
pdb=read(final/'default-pdb-sources.json');assert all(r['matched'] and source[r['path']]==r['sha256'] for r in pdb)
runtime={r['path']:r['sha256'] for r in read(final/'default-runtime-sha256.json')}
assert {k.removeprefix('app/'):v for k,v in runtime.items() if k.startswith('app/') and k.endswith('.dll')}==before

comparison=read(final/'formal/comparison.json');assert comparison['status']=='verified' and comparison['freshProcess'] and len(comparison['rows'])==5
costs={arm:{'functions':0,'gradients':0,'rays':0,'activitySeconds':0} for arm in ('continuous','resumed','control')}
external={'candidateChecks':0,'nativeReadbacks':0,'nativeExactFieldValues':0};identities=[]
def state(p):return read(p/'run.autonomous.json')['checkpoint']
def audit_case(p):
 audit=read(p/'independent-audit.json');assert not audit['searchBudgetInput']
 current=state(p)
 assert len(audit['candidates'])==len(current['candidates'])
 for row in audit['candidates']:
  c=next(c for c in current['candidates'] if c['id']==row['id']);assert row['assessment']==c['assessment'] and row['assessment']['completed']
 external['candidateChecks']+=len(audit['candidates'])
 for row in audit['native']:
  saved=read(p/(row['id']+'-native-audit.json'));assert saved['readback']['completed']
  assert saved['readback']['meetsTargets']==saved['original']['meetsTargets']
  assert len(saved['readback']['fields'])==len(saved['original']['fields'])==21
  if saved['readback']['fields']==saved['original']['fields']:external['nativeExactFieldValues']+=1
  external['nativeReadbacks']+=1
 return current
for seed in range(42,47):
 states={}
 for arm in ('continuous','resumed','control'):
  p=final/f'formal/{arm}/seed{seed}';s=audit_case(p);states[arm]=s;w=s['consumed']
  assert s['status']=='completed' and s['pending'] is None and s['reservation'] is None
  assert (w['functionEvaluations'],w['gradientEvaluations'],w['tracedRays'])==((82 if arm=='control' else 101),27,202218)
  assert seconds(w['elapsed'])<=120 and s['plan']['options']['maximumFunctionEvaluations']==300 and s['plan']['options']['maximumGradientEvaluations']==200
  assert s['plan']['options']['maximumTracedRays']==3_000_000 and seconds(s['plan']['options']['timeLimit'])==120
  assert len(s['candidates'])==len(s['acceptedArchive'])==4
  segments=[read(p) for p in (p/'segments').glob('*.json')];assert len(segments)==(5 if arm=='resumed' else 1)
  assert sum(x['actualNewRays'] for x in segments)==w['tracedRays']
  for x in segments:assert x['consumed']['tracedRays']-x['previous']['tracedRays']==x['actualNewRays']
  if arm=='resumed':assert sum(x['pausedAtTarget'] for x in segments)==4
  costs[arm]['functions']+=w['functionEvaluations'];costs[arm]['gradients']+=w['gradientEvaluations'];costs[arm]['rays']+=w['tracedRays'];costs[arm]['activitySeconds']+=seconds(w['elapsed'])
 a,b=states['continuous'],states['resumed']
 for key in ('randomState','seedCursor','trialCursor','candidates','pool','acceptedArchive'):assert a[key]==b[key],(seed,key)
 assert {k:v for k,v in a['consumed'].items() if k!='elapsed'}=={k:v for k,v in b['consumed'].items() if k!='elapsed'}
 assert sum(c['topology'].get('cementedInterfaces') is not None for c in a['acceptedArchive'])==3
 identities.append([c['topology'] for c in a['candidates']])
assert all(x==identities[0] for x in identities), 'Repeated seeds are not independent designs.'
assert read(final/'new-process-reopen.json')=={'studies':15,'rays':0}
chain_a=audit_case(final/'chain/continuous');chain_b=audit_case(final/'chain/resumed')
for key in ('randomState','seedCursor','trialCursor','candidates','pool','acceptedArchive'):assert chain_a[key]==chain_b[key],('chain',key)
assert {k:v for k,v in chain_a['consumed'].items() if k!='elapsed'}=={k:v for k,v in chain_b['consumed'].items() if k!='elapsed'}
assert any(len(c['topology']['elements'])==3 for c in chain_a['candidates'])
chain_segments=[read(p) for p in (final/'chain/resumed/segments').glob('*.json')]
assert len(chain_segments)==3 and sum(s['actualNewRays'] for s in chain_segments)==chain_a['consumed']['tracedRays']
assert read(final/'chain-reopen.json')=={'studies':2,'rays':0}
guards=read(final/'original-tasks/summary.json');assert len(guards['guards'])==15 and guards['originalF8ExactRechecks']==5
for row in guards['guards']:assert row['tracedRays']==0
for seed in range(42,47):assert read(final/f'original-tasks/F8-old-qualified-{seed}.json')['exact']
product=read(final/'product-session/product-resume.json');repeat=read(final/'product-session/product-reopen.json')
assert product['loadRays']==repeat['loadRays']==repeat['runRays']==0
assert product['state']['status']==repeat['state']['status']=='completed'
assert len(list((final/'product-session').glob('*.autonomous.json')))==1
docs=read(root/'documentation-audit.json');assert not docs['BrokenLinks'] and not docs['MarkdownFenceIssues'] and not docs['GuideRepeatedParagraphs']
previous={}
for folder in ('2026-11-01/initial-performance','2026-10-31/preparation-checkpoint','2026-11-01/initial-product-m36','2026-11-01/spc-branches','2026-11-01/spc-automatic-resume','2026-11-01/spc-default-output'):
 seal=repo/'validation/rechecks'/folder/'final-verification.json';d=read(seal)
 for row in d['artifacts']:assert sha(seal.parent/row['path'])==row['sha256'],(folder,row['path'])
 previous[folder]={'sealSha256':sha(seal),'verifiedArtifacts':len(d['artifacts'])}
paths=[repo/p for p in ('docs/INITIAL_STRUCTURE_CEMENTED_AUTOMATIC_2026-11-01.md','docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md',
 'docs/INITIAL_STRUCTURE_M3_1_2026-10-30.md','docs/CURRENT_STATUS.md','docs/README.md','docs/USER_GUIDE.md','labs/InitialStructure/README.md')]
paths += [p for p in (repo/'tools/diagnostics/CementedAutomatic20261101').rglob('*') if p.is_file() and not {'bin','obj','__pycache__'}&set(p.parts)]
delivery=[]
with zipfile.ZipFile(root/'delivery-source.zip','x',zipfile.ZIP_DEFLATED) as z:
 for p in sorted(paths):
  name=p.relative_to(repo).as_posix();b=p.read_bytes();z.writestr(name,b);delivery.append({'path':name,'sha256':hashlib.sha256(b).hexdigest()})
write(root/'delivery-source-sha256.json',delivery)
artifacts=[{'path':p.relative_to(root).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(root.rglob('*')) if p.is_file() and p.name!='final-verification.json']
write(root/'final-verification.json',{'status':'complete','date':'2026-11-01','authority':'final-authority/',
 'scope':'Explicit autonomous cemented growth/material scheduling and exact safe normal-stop continuation; original fixed-aperture/image/manufacturing gates.',
 'tests':{'passed':147,'failed':0,'skipped':0},'defaultOutput':summary,'build':{'warnings':0,'errors':0,'builtInModules':2},
 'actualDefaultAssemblies':before,'runtimeAuditProcesses':len(audits),'archives':archives,'matchedPdbDocuments':len(pdb),
 'fiveSeedsFourFreshProcessPausePointsExact':True,'formalCosts':costs,'externalAudit':external,
 'replayedRays':0,'distinctIndependentFiveDesignClaim':False,'reusedQualifiedParentNotColdStart':True,'newCementedPrescriptionsPerRun':3,
 'bestDoubletWorstRmsMicrometers':min(max(f['rmsRadiusMillimeters'] for f in c['assessment']['fields'])*1000 for c in states['continuous']['candidates'] if len(c['topology']['elements'])==2),
 'chain':{'growthFrom':2,'growthTo':3,'freshProcessPausePoints':2,'work':chain_a['consumed'],'exact':True},
 'originalGrowthBranchGuards':10,'originalF4FullMainConflictStops':5,'oldF8ExactRechecks':5,
 'product':{'exactlyOneMainRecord':True,'loadAndRepeatedRunRays':0,'mainWork':product['state']['consumed']},
 'gatesAperturesAndManufacturingRelaxed':False,'f4ConflictRetained':True,'numericEnvironmentBound':True,
 'historicalEvidenceIntegrity':previous,'documentation':{'documents':docs['Documents'],'brokenLinks':0,'fences':0,'guideDuplicates':0},
 'remainingRequiredChecksForThisScope':[],'nativeGui':False,'wholeRepositoryTests':False,'gpuCertification':False,'crossPlatformCertification':False,
 'deepSeekUsed':False,'agentsUsed':False,'commitPushInstallPublish':False,
 'limitations':['Default group limit2; explicitly bounded3/4 permitted, three-member chain verified. No recursive SPC saddle search.',
 'Legacy individual deletion/split/merge/profile editing is not dispatched on cemented groups.',
 'Exact normal-stop continuation requires the original numeric environment; unknown in-flight reservations retain conservative charge/abandon semantics.',
 'Warm parent and repeated seeds are not new cold-start or five independent design success evidence; extra glass is not a reduction benefit.',
 'Original F8/21deg guards cover growth branches only; other originally permitted branches remain independent.',
 'Runtime/source archive storage indexes reuse hash-verified unchanged earlier entries; packages are not standalone installers.'],
 'artifacts':artifacts})
for row in artifacts:assert sha(root/row['path'])==row['sha256']
print(f'Sealed/read back {len(artifacts)} artifacts;147 tests,47 actual-default processes,five-seed four-point and three-member chain exact continuation.')
