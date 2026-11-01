"""Freeze a numerical runtime and source evidence before performance trials; no optics."""
from pathlib import Path
import hashlib,json,shutil,sys,zipfile
repo=Path(sys.argv[1]).resolve();arm=sys.argv[2]
if arm not in ('baseline','candidate'):raise SystemExit('Declare baseline or candidate.')
root=repo/'validation/rechecks/2026-11-01/initial-performance'/arm
if root.exists():raise SystemExit('Never overwrite a frozen arm.')
root.mkdir(parents=True)
runtime_source=Path(sys.argv[3]).resolve() if len(sys.argv)>3 else repo/'tools/diagnostics/InitialPerformance20261101/bin/Release/net10.0'
shutil.copytree(runtime_source,root/'runtime')
files=[p for folder in ('src/OpticalSystemDesign.Core','labs/InitialStructure/src','tools/diagnostics/InitialPerformance20261101') for p in (repo/folder).rglob('*') if p.is_file() and not {'bin','obj','__pycache__'}&set(p.relative_to(repo).parts)]
manifest=[]
with zipfile.ZipFile(root/'source.zip','x',zipfile.ZIP_DEFLATED) as archive:
 for p in sorted(files):
  data=p.read_bytes();name=p.relative_to(repo).as_posix();archive.writestr(name,data);manifest.append(dict(path=name,sha256=hashlib.sha256(data).hexdigest()))
(root/'source-sha256.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
runtime=[dict(path=p.relative_to(root).as_posix(),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted((root/'runtime').rglob('*')) if p.is_file()]
(root/'runtime-sha256.json').write_text(json.dumps(runtime,indent=2),encoding='utf8')
protocol=dict(arm=arm,seeds=[42,43,44,45,46],order=[46,42,43,44,45],workers=1,
 originalInput='validation/rechecks/2026-10-31/initial-loss-compensation/control/seed44/competition-8-F8-full25.input.json',
 caps=dict(functions=600,gradients=600,cpuRays=10000000,activeSeconds=600),originalPhaseCapsRetained=True,
 unchanged='Original aperture/stop/image, field/spectrum, material/shape/axial permissions, image and manufacturing gates.',
 timing='Opt-in inclusive stage timings; wall-clock and active elapsed measured, not bit-compared. Profiling/IO/offline not added as extra computation allowance.',
 imports=False,f4='Original fixed-aperture conflict retained; never enlarge aperture or relax acceptance.',
 sourceScope='Actual runtime archived with source snapshot. Later shared working-tree edits are not substituted into frozen runs.')
(root/'protocol.json').write_text(json.dumps(protocol,indent=2),encoding='utf8')
print(f'Frozen {arm}: {len(manifest)} source and {len(runtime)} runtime files.')
