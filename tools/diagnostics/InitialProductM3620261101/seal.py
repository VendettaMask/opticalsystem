"""Seal completed M3.6 validation and sources. Does not compute optics."""
from pathlib import Path
import hashlib,json,sys,zipfile,xml.etree.ElementTree as ET

repo=Path(sys.argv[1]).resolve();root=repo/'validation/rechecks/2026-11-01/initial-product-m36'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def tests(name,expected):
 ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 rows=ET.parse(root/'tests'/name).findall('.//t:UnitTestResult',ns)
 assert len(rows)==expected and all(x.get('outcome')=='Passed' for x in rows),name
 return [x.get('testName') for x in rows]
if (root/'final-verification.json').exists():raise SystemExit('Never overwrite sealed evidence.')
names=[];runs={}
for name,count in [('m36-final.trx',83),('m36-release-final.trx',14),('product-release-final.trx',1),('product-release-reopen.trx',1)]:
 rows=tests(name,count);names+=rows;runs[name]={'passed':count,'failed':0,'skipped':0}
runtime={}
for prefix in ('runtime','release-runtime','product-release-runtime'):
 a=read(root/f'tests/{prefix}-before.json');b=read(root/f'tests/{prefix}-after.json')
 before={Path(x['Path']).name:x['Hash'] for x in a};after={Path(x['Path']).name:x['Hash'] for x in b}
 assert before==after and len(before)==9,prefix
 scope={'runtime':'regression','release-runtime':'final-ui','product-release-runtime':'final-product'}[prefix]
 for name,value in before.items():assert sha(root/'runtimes'/scope/name).upper()==value,scope+'/'+name
 runtime[prefix]={'binaryCount':9,'unchanged':True}
product=read(root/'product-session-final/report-verification.json')
assert product['ReadOnlyReportRays']==0 and product['PlanAndAllBudgetCountersUnchanged'] and product['OriginalGatesUnchanged']
flow=read(root/'product-session-final/summary.json')
assert flow['preparationStoppedAndLoaded'] and flow['loadAndRepeatedHandoffRays']==0 and flow['physicalAndDiscreteWorkSettled']
assert flow['checkedAgain']['completed'] and flow['checkedAgain']['meetsTargets']
for seed in range(42,47):
 evidence=read(root/f'evidence-release-final/original-F8-{seed}.json')
 assert evidence['qualified'] and evidence['displayAndReportRays']==0
 assert evidence['independent']['meetsTargets'] and evidence['nativeReadback']['meetsTargets']
f4=read(root/'evidence-release-final/original-F4-conflict.json')
assert f4['stopReason']=='OriginalExternalStopApertureConflict' and f4['consumed']['functionEvaluations']==0 and f4['consumed']['tracedRays']==0
docs=read(root/'documentation-audit.json')
assert not docs['BrokenLinks'] and not docs['MarkdownFenceIssues'] and not docs['GuideRepeatedParagraphs']
assert '0 个警告' in (root/'tests/default-app-release-final.log').read_text(encoding='utf-8-sig')
assert '0 个错误' in (root/'tests/default-app-release-final.log').read_text(encoding='utf-8-sig')

previous={}
for folder in ('2026-11-01/initial-performance','2026-10-31/preparation-checkpoint'):
 seal=repo/'validation/rechecks'/folder/'final-verification.json';data=read(seal)
 for item in data['artifacts']:assert sha(seal.parent/item['path'])==item['sha256'],item['path']
 previous[folder]={'sealSha256':sha(seal),'verifiedArtifacts':len(data['artifacts'])}

folders=['src/OpticalSystemDesign.Core','labs/InitialStructure/src','labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests',
 'src/OpticalSystemDesign.App/Laboratories/InitialStructure','tools/diagnostics/InitialProductM3620261101','tools/diagnostics/F8RealPipeline']
paths={p for folder in folders for p in (repo/folder).rglob('*') if p.is_file() and not {'bin','obj','__pycache__'}&set(p.relative_to(repo).parts)}
extra=['AGENTS.md','global.json','Directory.Build.props','Directory.Packages.props','src/OpticalSystemDesign.App/Services/DesignReviewLabels.cs',
 'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs','src/OpticalSystemDesign.App/Services/UiLocalization.cs',
 'docs/INITIAL_STRUCTURE_PRODUCT_REVIEW_2026-11-01.md','docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md',
 'docs/CURRENT_STATUS.md','docs/README.md','labs/InitialStructure/README.md','validation/rechecks/2026-11-01/initial-product-m36/README.md']
paths.update(repo/x for x in extra if (repo/x).is_file());manifest=[]
with zipfile.ZipFile(root/'final-source.zip','x',zipfile.ZIP_DEFLATED) as archive:
 for p in sorted(paths):
  data=p.read_bytes();name=p.relative_to(repo).as_posix();archive.writestr(name,data);manifest.append({'path':name,'sha256':hashlib.sha256(data).hexdigest()})
(root/'final-source-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')

attempts=[]
for folder in ('product-session','product-session-v2','product-session-final'):
 s=read(root/f'{folder}/summary.json');attempts.append({'path':folder,'preparation':s['preparationCost'],'main':s['mainSearchCost'],
  'originalPhysicsAndNativeReadbackCompleted':s['checkedAgain']['meetsTargets'],'finalCombinedAuditPassed':folder=='product-session-final'})
artifacts=[{'path':p.relative_to(root).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(root.rglob('*')) if p.is_file() and p.name!='final-verification.json']
result={'status':'complete','date':'2026-11-01','milestone':'M3.6','scope':'Product record presentation, trilingual Markdown/JSON, portable read-only portfolio; background/headless only.',
 'testRuns':runs,'uniquePassedTestIdentities':len(set(names)),'runtimeScopes':runtime,'originalF8IndependentQAAndNativeReadbacks':10,
 'f4ZeroWorkSpecificationConflict':True,'gatesAperturesSamplingAndBudgetsChanged':False,'reportAndPortfolioRays':0,
 'productAttemptsAndCosts':attempts,'transitionEvidenceNeverQualifiedOrExported':True,'evidenceFileLimitBytes':64*1024*1024,
 'documentation':{'documents':docs['Documents'],'brokenLinks':0,'fences':0,'guideDuplicates':0},'historicalEvidenceIntegrity':previous,
 'sourceFiles':len(manifest),'fullRepositoryTests':False,'nativeGui':False,'commitPushPublish':False,
 'limitations':['83-case regression precedes final transition/serialization refinements; final14-case suite and final original product chain cover those changes.',
 'Different recorded runtime scopes are retained separately; repeated tests are not counted as independent designs.',
 'Portfolio snapshots do not resume searches or upgrade old numerical states; original checkpoints and kernels remain required.',
 'M3.3 automatic apertures, genuine SPC branch optimization and autonomous cemented scheduling remain open.'],
 'artifacts':artifacts}
(root/'final-verification.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print(f'Sealed {len(artifacts)} artifacts, {len(manifest)} sources, {len(set(names))} distinct passed test identities; original gates and F4 conflict retained.')
