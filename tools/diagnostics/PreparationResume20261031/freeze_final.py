"""Snapshot experiment sources/runtime and declare all paired cold runs. No optics in Python."""
from pathlib import Path
import hashlib
import json
import shutil
import sys
import zipfile

repo=Path(sys.argv[1]).resolve()
version=sys.argv[2] if len(sys.argv)>2 else 'v2'
if version not in ('v2','v3','v4'):raise SystemExit('Declare a new study version explicitly.')
root=repo/f'validation/rechecks/2026-10-31/preparation-checkpoint/final-paired-{version}'
if root.exists():raise SystemExit('Never overwrite a frozen study.')
root.mkdir(parents=True)
shutil.copytree(repo/'tools/diagnostics/PreparationResume20261031/bin/Release/net10.0',root/'runtime')
files=[p for folder in ('labs/InitialStructure/src','tools/diagnostics/PreparationResume20261031') for p in (repo/folder).rglob('*')
       if p.is_file() and not {'bin','obj','.git','__pycache__'} & set(p.relative_to(repo).parts)]
files += [(repo/'src/OpticalSystemDesign.App/Laboratories/InitialStructure'/name) for name in
          ('AutonomousDesktopSession.cs','AutonomousTopologyWindow.Seeds.cs','AutonomousTopologyWindow.Workflow.cs')]
files += [repo/'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs']
manifest=[]
with zipfile.ZipFile(root/'source.zip','x',zipfile.ZIP_DEFLATED) as z:
 for p in sorted(set(files)):
  relative=p.relative_to(repo).as_posix();z.writestr(relative,p.read_bytes());manifest.append({'path':relative,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
(root/'source-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
runtime=[{'path':p.relative_to(root).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted((root/'runtime').rglob('*')) if p.is_file()]
(root/'runtime-sha256.json').write_text(json.dumps(runtime,indent=2),encoding='utf8')
protocol={'version':f'preparation-paired-{version}','seeds':list(range(42,47)),'budget':{'functions':600,'gradients':600,'rays':10000000,'activeSeconds':600},
 'arms':['continuous','paused-and-new-process-resume'],'stopPoints':{'42':'construction','43':'sphere','44':'profile','45':'sphere','46':'dense'},
 'input':'Original competition-8-F8-full25 seed44 specification, only RNG seed changes. Fresh layouts, no endpoint imports.',
 'execution':'One bounded background worker; longest seed46 pair first, then42-45, sequential arms to avoid contention near unchanged wall-clock phase caps; one fresh process for each start and another fresh process for each resume. One frozen common runtime; nested computation parallelism suppressed.',
 'comparison':'Exact RNG, pool, phase endpoints/diagnostics, final original QA and all discrete counters. Segment actual ray measurements must settle exactly with no repeated trace. Active elapsed is measured, monotone, excludes offline/persistence time, and is not required to be bit-identical.',
 'originalGates':'All original aperture/stop/image, material/shape/axial permissions and image/manufacturing gates unchanged. Checkpoint cadence does not change the solver.',
 'boundaries':'Cold updates suspend after a complete accepted/rejected update; other preparation modes use complete starts/branches/candidate-stage/index-transfer-validation operations. Unexpected lost reservations are conservatively charged once and not reported as exact continuation.',
 'f4':'Original stop6.25mm > fixedface5mm conflict retained, no optimization or relaxed gates.',
 'sourceScope':'Module/adapter sources captured; shared Core runtime binary is archived and is the actual numerical authority for this frozen paired study. Concurrent unrelated Core working-tree edits are not substituted into these runs.'}
(root/'protocol.json').write_text(json.dumps(protocol,indent=2),encoding='utf8')
print(f'Frozen {len(manifest)} source files, {len(runtime)} runtime files and five paired seed protocols.')
