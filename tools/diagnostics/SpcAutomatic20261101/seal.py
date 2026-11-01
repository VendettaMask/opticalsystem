"""Audit saved SPC automatic/resume evidence; no optical calculations."""
from pathlib import Path
import hashlib,json,sys,zipfile,xml.etree.ElementTree as ET

repo=Path(sys.argv[1]).resolve();root=repo/'validation/rechecks/2026-11-01/spc-automatic-resume'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,data):
    with p.open('x',encoding='utf8') as f:json.dump(data,f,indent=2,ensure_ascii=False)
def sec(text):
    h,m,s=text.split(':');return int(h)*3600+int(m)*60+float(s)
def strip(value):
    if isinstance(value,dict):return {k:strip(v) for k,v in value.items() if k!='elapsed'}
    if isinstance(value,list):return [strip(v) for v in value]
    return value
if (root/'final-verification.json').exists():raise SystemExit('Never overwrite sealed evidence.')
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests={}
for name,count in [('spc-frozen-final.trx',109),('spc-frozen-v2-final.trx',109),('spc-integration-v1.trx',82)]:
    rows=ET.parse(root/'tests'/name).findall('.//t:UnitTestResult',ns)
    assert len(rows)==count and all(r.get('outcome')=='Passed' for r in rows),name
    tests[name]={'passed':count,'failed':0,'skipped':0}
docs=read(root/'documentation-audit.json')
assert not docs['BrokenLinks'] and not docs['MarkdownFenceIssues'] and not docs['GuideRepeatedParagraphs']
build=(root/'frozen-driver-v2-build.log').read_text(encoding='utf-8-sig')
assert '0 个警告' in build and '0 个错误' in build
lock=read(root/'default-output-lock.json')
assert lock['dependencyOpenForWriteFailed'] and lock['applicationNotClosed'] and lock['independentBuildIsNotDefaultOutputAcceptance']
assert 'error ' not in (root/'default-compile-only.log').read_text(encoding='utf-8-sig').lower()

archive={}
for prefix in ('v1','v2'):
    sources=read(root/f'{prefix}-source-sha256.json');pdb=read(root/f'{prefix}-pdb-sources.json')
    assert len(sources)==2340 and len(pdb)==1981 and all(x['matched'] for x in pdb)
    with zipfile.ZipFile(root/f'{prefix}-source.zip') as z:
        for row in sources:assert hashlib.sha256(z.read(row['path'])).hexdigest()==row['sha256'],row['path']
        for row in pdb:assert hashlib.sha256(z.read(row['path'])).hexdigest()==row['sha256'],row['path']
    runtime=read(root/f'{prefix}-runtime-sha256.json')
    with zipfile.ZipFile(root/f'{prefix}-runtime.zip') as z:
        for row in runtime:assert hashlib.sha256(z.read(row['path'])).hexdigest()==row['sha256'],row['path']
    archive[prefix]={'sourcesAndResources':len(sources),'matchedPdbDocuments':len(pdb),'runtimeFiles':len(runtime)}
v2runtime={x['path']:x['sha256'] for x in read(root/'v2-runtime-sha256.json')}
for name in ('OpticalSystemDesign.Core','OpticalSystemDesign.InitialStructure.Engine','OpticalSystemDesign.InitialStructure.Contracts',
             'OpticalSystemDesign.InitialStructure.Persistence','OpticalSystemDesign.Application','OpticalSystemDesign.App',
             'OpticalSystemDesign.Modules','OpticalSystemDesign.CoatingDesign.Engine'):
    assert v2runtime[f'driver/{name}.dll']==v2runtime[f'tests/{name}.dll'],name
before=read(root/'v2-runtime-before.json')
assert {x['path']:x['sha256'] for x in before}=={k.removeprefix('driver/'):v for k,v in v2runtime.items() if k.startswith('driver/')}

costs={};identities=[];validation_count=0
for batch,folder in [('v1','formal'),('v2','formal-v2')]:
    comparison=read(root/folder/'comparison.json');assert comparison['status']=='verified' and comparison['freshProcessResume'] and len(comparison['rows'])==5
    total={'functions':0,'gradients':0,'rays':0,'activitySeconds':0,'recordedPersistenceSeconds':0}
    for seed in range(42,47):
        states={}
        for arm in ('continuous','resumed'):
            directory=root/folder/arm/f'seed{seed}'
            state=read(directory/'run.preparation.json')['checkpoint'];states[arm]=state;w=state['consumed']
            assert state['status']=='completed' and state['stage']=='Finished' and state['lostReservations']==0
            assert (w['functionEvaluations'],w['gradientEvaluations'],w['tracedRays'],w['differenceEvaluations'],w['validationEvaluations'])==(161,151,503394,56,10)
            assert sec(w['elapsed'])<=120 and len(state['spc']['pool'])==8 and len(state['result']['seeds'])==2
            assert state['options']['maximumFunctionEvaluations']==600 and state['options']['spc']['maximumGradientEvaluations']==600
            assert state['options']['maximumTracedRays']==2_000_000 and sec(state['options']['timeLimit'])==120
            segments=[read(p) for p in (directory/'segments').glob('from-*.json')]
            assert len(segments)==(1 if arm=='continuous' else 7)
            assert sum(x['actualNewRays'] for x in segments)==503394
            for x in segments:assert x['consumed']['tracedRays']-x['previous']['tracedRays']==x['actualNewRays']
            audit=read(directory/'independent-audit.json');assert not audit['searchBudgetInput']
            assert len(audit['candidates'])==8 and len(audit['nativeReadbacks'])==2
            for x in audit['candidates']:assert x['assessment']['completed'] and x['assessment']['meetsTargets']
            for x in audit['nativeReadbacks']:assert x['readback']['completed'] and x['readback']['meetsTargets']
            if batch=='v2':validation_count+=10
            total['functions']+=w['functionEvaluations'];total['gradients']+=w['gradientEvaluations'];total['rays']+=w['tracedRays']
            total['activitySeconds']+=sec(w['elapsed']);total['recordedPersistenceSeconds']+=sec(state['persistenceElapsed'])
        a,b=states['continuous'],states['resumed']
        assert a['plan']==b['plan'] and a['randomState']==b['randomState'] and strip(a['spc'])==strip(b['spc']) and strip(a['consumed'])==strip(b['consumed'])
        assert a['result']['seeds']==b['result']['seeds']
        if batch=='v2':identities.append(comparison['rows'][seed-42]['endpoints'])
    costs[batch]=total
assert len({tuple(x) for x in identities})==1
guards=read(root/'original-tasks-v2/summary.json');assert len(guards['guards'])==15 and guards['originalF8ExactRechecks']==5
for item in guards['guards']:assert item['consumed']['functionEvaluations']==item['consumed']['tracedRays']==0
for seed in range(42,47):assert read(root/f'original-tasks-v2/F8-old-qualified-{seed}.json')['exact']
reopen=read(root/'formal-v2-new-process-reopen.json');assert reopen['studies']==10 and reopen['rays']==0
product=read(root/'product-session-v2/completed.json');repeated=read(root/'product-session-v2/reopened.json')
assert product['mainStatus']=='completed' and product['exactlyOneHandoff'] and product['noGateChanges'] and product['selected']==2
assert product['loadRays']==product['reportRays']==0 and repeated['loadRays']==repeated['repeatedHandoffRays']==repeated['reportRays']==0
assert len(list((root/'product-session-v2').glob('*.autonomous.json')))==1
for x in product['audits']:assert x['independent']['meetsTargets'] and x['nativeReadback']['meetsTargets']
previous={}
for folder in ('2026-11-01/initial-performance','2026-10-31/preparation-checkpoint','2026-11-01/initial-product-m36','2026-11-01/spc-branches'):
    seal=repo/'validation/rechecks'/folder/'final-verification.json';data=read(seal)
    for item in data['artifacts']:assert sha(seal.parent/item['path'])==item['sha256'],folder+'/'+item['path']
    previous[folder]={'sealSha256':sha(seal),'verifiedArtifacts':len(data['artifacts'])}
owned=['src/OpticalSystemDesign.Core/Optimization/StationaryPointSearch.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/SpcPreparationModels.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/SeedPreparationCheckpointModels.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/TopologySeedSearchModels.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/SpcBranchSearch.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/SpcBranchSearch.Preparation.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/SeedPreparationSearch.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/ParaxialSeedSearch.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/SeedPreparationCheckpointStore.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/AutonomousDesignStudy.cs',
 'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousDesktopSession.cs',
 'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.cs',
 'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Seeds.cs',
 'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Workflow.cs',
 'src/OpticalSystemDesign.App/Services/DesignReviewLabels.cs']
with zipfile.ZipFile(root/'v2-source.zip') as z:
    for name in owned:assert z.read(name)==(repo/name).read_bytes(),'Current relevant source drift: '+name
extra=['docs/INITIAL_STRUCTURE_SPC_AUTOMATIC_RESUME_2026-11-01.md','docs/INITIAL_STRUCTURE_SPC_BRANCHES_2026-11-01.md',
 'docs/CURRENT_STATUS.md','docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md','docs/README.md','docs/USER_GUIDE.md','labs/InitialStructure/README.md',
 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/SpcPreparationTests.cs',
 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousSpcDesktopTests.cs',
 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/DelanoMaterializationTests.cs']
paths={repo/x for x in owned+extra}
paths.update(p for p in (repo/'tools/diagnostics/SpcAutomatic20261101').rglob('*') if p.is_file() and not {'bin','obj'}&set(p.relative_to(repo).parts))
delivery=[]
with zipfile.ZipFile(root/'delivery-source.zip','x',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(paths):
        name=p.relative_to(repo).as_posix();data=p.read_bytes();z.writestr(name,data);delivery.append({'path':name,'sha256':hashlib.sha256(data).hexdigest()})
write(root/'delivery-source-sha256.json',delivery)
artifacts=[{'path':p.relative_to(root).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(root.rglob('*')) if p.is_file() and p.name!='final-verification.json']
write(root/'final-verification.json',{'status':'implementation-verified-default-output-blocked','date':'2026-11-01',
 'scope':'Explicit one-level air-separated automatic SPC preparation, exact normal-stop continuation, trilingual controls/report, once-only main handoff; original gates.',
 'tests':tests,'finalPassedTests':109,'finalPdbDocumentChecks':1981,'finalSourceResourceFiles':2340,'runtimeScopes':archive,
 'finalDriverAndTestsSameEightProductAssemblies':True,'finalDriverRuntimeBeforeAfterUnchanged':True,
 'fiveSeedsSixFreshProcessPausePointsExact':True,'formalCosts':costs,'finalCandidateIndependentAndNativeChecks':validation_count,
 'originalGuards':15,'originalF8ExactRechecks':5,'distinctSelectedDesigns':2,'independentFiveDesignSuccessClaim':False,
 'normalResumeReplayedRays':0,'newProcessReportRays':0,'lostReservationChargedOnceAndNotResumable':True,
 'product':{'preparation':product['preparation'],'main':product['main'],'exactlyOneHandoff':True,'repeatAndReportRays':0,'handoffIndependentAndNativeChecks':4},
 'gatesAperturesAndManufacturingRelaxed':False,'f4ConflictRetained':True,
 'defaultOutput':{'compileStagePassed':True,'fullBuildPassed':False,'copyBlockedByProcess':21596,'userApplicationClosed':False,'currentInstanceUpdated':False},
 'independentFullAppAndDriverBuild':{'warnings':0,'errors':0},'documentation':{'documents':docs['Documents'],'brokenLinks':0,'fences':0,'guideDuplicates':0},
 'historicalEvidenceIntegrity':previous,'nativeGui':False,'gpuCertification':False,'crossPlatformCertification':False,'wholeRepositoryTests':False,'deepSeekUsed':False,
 'commitPushInstallPublish':False,'remainingRequiredCheck':['Default output full build/copy after user application releases the files.'],
 'limitations':['One original-permitted spherical post-stop site per parent, air-separated one-level growth; no autonomous cemented/recursive scheduling or automatic aperture.',
 'Selection uses original-qualified element-count/worst-RMS tradeoffs; not an assertion of a complete multimetric manufacturing Pareto frontier.',
 'Exact normal resume is bound to its recorded numeric environment. Forced process loss conservatively settles pending quota once and stops.',
 'Initial protocol batch is retained separately; final batch corrects checkpoint frequency metadata and binds numeric environment.',
 'Archived runtime packages retain Windows x64 dependencies; unexecuted platform binaries/native debug symbols were cleaned only from task-generated scratch.'],
 'artifacts':artifacts})
print(f'Sealed {len(artifacts)} files; final109 tests, five-seed six-point exact continuation; default-output full build remains blocked.')
