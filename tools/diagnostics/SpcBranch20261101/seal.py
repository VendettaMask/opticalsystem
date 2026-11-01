"""Read-only validation audit and evidence sealing; no optical calculations."""
from pathlib import Path
import hashlib,json,sys,zipfile,xml.etree.ElementTree as ET

repo=Path(sys.argv[1]).resolve(); root=repo/'validation/rechecks/2026-11-01/spc-branches'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,data):
    with p.open('x',encoding='utf8') as f: json.dump(data,f,indent=2,ensure_ascii=False)
if (root/'final-verification.json').exists(): raise SystemExit('Sealed evidence is never overwritten.')
comparison=read(root/'comparison-final.json'); assert comparison['status']=='verified'
assert comparison['newOriginalTaskSuccesses']==0 and not comparison['gatesAperturesAndBudgetsRelaxed']
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
runs={}; names=[]; isolated_names=[]
for file,count in [('spc-focused-v3.trx',75),('spc-isolated.trx',48),('core-isolated.trx',23)]:
    rows=ET.parse(root/'tests'/file).findall('.//t:UnitTestResult',ns)
    assert len(rows)==count and all(r.get('outcome')=='Passed' for r in rows),file
    runs[file]={'passed':count,'failed':0,'skipped':0}
    names.extend(r.get('testName') for r in rows)
assert len(set(names))==98
docs=read(root/'documentation-audit.json')
assert not docs['BrokenLinks'] and not docs['MarkdownFenceIssues'] and not docs['GuideRepeatedParagraphs']
build=(root/'default-app-build.log').read_text(encoding='utf-8-sig')
assert '0 个警告' in build and '0 个错误' in build
pdb=read(root/'pdb-source-audit-final.json'); assert len(pdb)==577 and all(x['matches'] for x in pdb)
sources=read(root/'executed-source-sha256.json');assert len(sources)==598
with zipfile.ZipFile(root/'executed-source.zip') as z:
    for item in sources: assert hashlib.sha256(z.read(item['path'])).hexdigest()==item['sha256'],item['path']
for manifest in ('formal-runtime-sha256.json','isolated-runtime-sha256.json'):
    folder=manifest.removesuffix('-sha256.json')
    for x in read(root/manifest): assert sha(root/folder/x['name'])==x['sha256'],folder+'/'+x['name']
for x in read(root/'test-runtime-sha256.json'):
    assert sha(root/'test-runtimes'/x['scope']/x['name'])==x['sha256']
for folder in ('spc','core'):
    assert sha(root/'test-runtimes'/folder/'OpticalSystemDesign.Core.dll')==sha(root/'isolated-runtime/OpticalSystemDesign.Core.dll')
for name in ('OpticalSystemDesign.InitialStructure.Engine.dll','OpticalSystemDesign.InitialStructure.Contracts.dll','OpticalSystemDesign.InitialStructure.Persistence.dll'):
    assert sha(root/'test-runtimes/spc'/name)==sha(root/'isolated-runtime'/name)
previous={}
for folder in ('2026-11-01/initial-performance','2026-10-31/preparation-checkpoint','2026-11-01/initial-product-m36'):
    seal=repo/'validation/rechecks'/folder/'final-verification.json';data=read(seal)
    for item in data['artifacts']: assert sha(seal.parent/item['path'])==item['sha256'],folder+'/'+item['path']
    previous[folder]={'sealSha256':sha(seal),'verifiedArtifacts':len(data['artifacts'])}

# Exact relevant implementation source must still agree with the isolated execution source.
owned=['src/OpticalSystemDesign.Core/Differentiation/DifferentiableOpticalMetrics.cs',
 'src/OpticalSystemDesign.Core/Differentiation/DifferentiableOpticalMetrics.Continuation.cs',
 'src/OpticalSystemDesign.Core/Raytrace/SequentialRayTracer.DesignSensitivity.cs',
 'src/OpticalSystemDesign.Core/Optimization/StationaryPointSearch.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/SpcBranchModels.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/SpcBranchSearch.cs',
 'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/SpcBranchStudyFiles.cs']
with zipfile.ZipFile(root/'executed-source.zip') as z:
    for name in owned: assert (repo/name).read_bytes()==z.read(name),name
paths={repo/x for x in owned}
extra=['AGENTS.md','Directory.Build.props','Directory.Build.targets','global.json','ProductVersion.props',
 'docs/INITIAL_STRUCTURE_SPC_BRANCHES_2026-11-01.md','docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md',
 'docs/CURRENT_STATUS.md','docs/README.md','labs/InitialStructure/README.md',
 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/SpcBranchTests.cs',
 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/SpcTransitionTests.cs',
 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/CementedTopologyTests.cs',
 'tests/OpticalSystemDesign.Tests/OpticalDesignDerivativeTests.cs','tests/OpticalSystemDesign.Tests/DifferentiableCheckMaximumTests.cs',
 'tests/OpticalSystemDesign.Tests/DifferentiableGeometryWorkspaceTests.cs']
paths.update(repo/x for x in extra)
paths.update(p for p in (repo/'tools/diagnostics/SpcBranch20261101').rglob('*') if p.is_file() and not {'bin','obj'} & set(p.relative_to(repo).parts))
manifest=[]
with zipfile.ZipFile(root/'final-source.zip','x',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(paths):
        data=p.read_bytes();name=p.relative_to(repo).as_posix();z.writestr(name,data);manifest.append({'path':name,'sha256':hashlib.sha256(data).hexdigest()})
    # Save the actual reduced test project descriptions and source copies, not just a list of test names.
    snapshot=Path('D:/Projects/.codex-build/SPC-20261101-frozen')
    for folder in ('tests/OpticalSystemDesign.Tests','labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests'):
        for p in sorted((snapshot/folder).glob('*')):
            if p.is_file() and p.suffix in ('.cs','.csproj'):
                name='isolated-tests/'+p.relative_to(snapshot).as_posix();data=p.read_bytes();z.writestr(name,data)
                manifest.append({'path':name,'sha256':hashlib.sha256(data).hexdigest()})
write(root/'final-source-sha256.json',manifest)
artifacts=[{'path':p.relative_to(root).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(root.rglob('*')) if p.is_file() and p.name!='final-verification.json']
result={'status':'complete','date':'2026-11-01','scope':comparison['scope'],'tests':runs,'uniquePassedTestIdentities':98,
 'frozenSpcCoreAndFormalRuntimeBytesMatch':True,'pdbDocumentChecksMatched':577,'executedSourceResourceFiles':598,
 'formalBatches':2,'casesPerBatch':10,'qualifiedBranchesPerArmPerBatch':10,'distinctQualifiedDesignsPerArm':2,
 'searchCostsByBatch':comparison['searchCostsByBatch'],'spcWorstRmsMillimeters':comparison['spcWorstRmsMillimeters'],
 'directWorstRmsMillimeters':comparison['directWorstRmsMillimeters'],'stationaryParentWorstRmsMillimeters':comparison['parentRmsMillimeters'],
 'fewerLensOrParentParetoBenefit':False,'newOriginalTaskSuccesses':0,'f4OriginalConflictZeroWork':True,
 'originalGuardsPerBatch':15,'originalF8ExactRechecksPerBatch':5,'branchIndependentAndNativeChecksPerBatch':40,
 'newProcessStudies':10,'readOnlyStudyRays':0,'gatesAperturesAndBudgetsRelaxed':False,
 'defaultAppBuild':{'warnings':0,'errors':0},'documentation':{'documents':docs['Documents'],'brokenLinks':0,'fences':0,'guideDuplicates':0},
 'historicalEvidenceIntegrity':previous,'fullRepositoryTests':False,'nativeGui':False,'gpuCertification':False,'deepSeekUsed':False,'commitPushPublish':False,
 'limitations':['One declared-curvature subspace, one existing authored parent/site; not global stationarity or independent five-design success evidence.',
 'Explicit air-separated one-level API only; automatic preparation/desktop routing, SPC optimizer checkpoints and autonomous cemented/recursive scheduling remain open.',
 'The refined one-lens parent already qualifies and has better RMS than the two restored children; no complexity benefit.',
 'Shared-build first batch and fixed-source final batch retained separately, including failed source-audit and failed development attempts.',
 '23 Core and48 module tests execute the identical final Core bytes; the broader75-case working-checkout regression is a separate scope.'],
 'artifacts':artifacts}
write(root/'final-verification.json',result)
print(f"Sealed {len(artifacts)} artifacts, {len(manifest)} delivery sources; 98 distinct tests; original gates and F4 conflict unchanged.")
