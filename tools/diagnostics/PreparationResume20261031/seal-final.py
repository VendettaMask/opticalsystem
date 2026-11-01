"""Archive final task sources and record the immutable runtimes used for validation."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

repo=Path(sys.argv[1]).resolve()
root=repo/'validation/rechecks/2026-10-31/preparation-checkpoint'
folders=('labs/InitialStructure/src','labs/InitialStructure/tests','src/OpticalSystemDesign.App/Laboratories/InitialStructure',
 'tools/diagnostics/PreparationResume20261031','tools/diagnostics/PreparationCheckpointAudit20261031','tools/diagnostics/F8RealPipeline')
files=[p for folder in folders for p in (repo/folder).rglob('*') if p.is_file() and not {'bin','obj','__pycache__'}&set(p.relative_to(repo).parts)]
files += [repo/p for p in ('AGENTS.md','src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs',
 'docs/INITIAL_STRUCTURE_PREPARATION_CHECKPOINT_2026-10-31.md','docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md',
 'docs/CURRENT_STATUS.md','docs/USER_GUIDE.md','docs/README.md','labs/InitialStructure/README.md',
 'docs/INITIAL_STRUCTURE_COMPETITION_2026-10-30.md','docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md',
 'validation/rechecks/2026-10-31/preparation-checkpoint/README.md')]
manifest=[]
with zipfile.ZipFile(root/'final-source.zip','x',zipfile.ZIP_DEFLATED) as archive:
 for p in sorted(set(files)):
  content=p.read_bytes();name=p.relative_to(repo).as_posix();archive.writestr(name,content)
  manifest.append(dict(path=name,sha256=hashlib.sha256(content).hexdigest()))
(root/'final-source-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
runtime={}
for folder in ('final-paired-v4/runtime','audit-frozen-runtime','audit-current-runtime','audit-product-runtime','current-product-runtime','regression-runtime'):
 runtime[folder]=[dict(path=p.relative_to(root).as_posix(),sha256=hashlib.sha256(p.read_bytes()).hexdigest())
  for p in sorted((root/folder).rglob('*')) if p.is_file()]
original=json.loads((root/'final-paired-v4/runtime-sha256.json').read_text())
for item in original:
 assert hashlib.sha256((root/'final-paired-v4'/item['path']).read_bytes()).hexdigest()==item['sha256'],'Frozen experiment runtime changed.'
drift=[]
for item in json.loads((root/'final-paired-v4/source-sha256.json').read_text()):
 p=repo/item['path']
 if not p.exists() or hashlib.sha256(p.read_bytes()).hexdigest()!=item['sha256']:drift.append(item['path'])
assert all(p.startswith('tools/diagnostics/') for p in drift),'Product preparation source changed after the frozen study.'
result=dict(sourceFiles=len(manifest),runtimeFiles=runtime,changedFrozenSourcePaths=drift,
 sourceScope='Preparation module, desktop adapters, tests, diagnostics and current task documentation; shared Core numerical authority is each actual archived binary, not a later concurrent working-tree revision.',
 numericalRuntimeScope='Frozen paired study; regression-copy Core; final product-copy Core. All three original QA audits separately recorded.',
 noNativeGui=True,noDeployment=True)
(root/'final-identities.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print(f'Archived {len(manifest)} source files; frozen runtime unchanged, production preparation sources unchanged.')
