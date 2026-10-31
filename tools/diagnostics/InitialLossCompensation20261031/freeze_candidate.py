"""Freeze source/runtime identities only. Existing evidence is never overwritten."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

frozen, evidence = (Path(p).resolve() for p in sys.argv[1:])
files = [p for p in frozen.rglob("*") if p.is_file()
         and not set(p.relative_to(frozen).parts) & {"bin", "obj", ".git"}]
archive_path = evidence / "candidate-source.zip"
manifest = []
with zipfile.ZipFile(archive_path, "x", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(files):
        relative = path.relative_to(frozen).as_posix()
        data = path.read_bytes()
        archive.writestr(relative, data)
        manifest.append(dict(path=relative, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
runtime = []
for folder in ("baseline-runtime", "candidate-v1-runtime"):
    for path in sorted((evidence / folder).glob("*.dll")):
        runtime.append(dict(path=path.relative_to(evidence).as_posix(), sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
core = [r["sha256"] for r in runtime if r["path"].endswith("/OpticalSystemDesign.Core.dll")]
assert len(core) == 2 and core[0] == core[1]
with (evidence / "candidate-source-sha256.json").open("x", encoding="utf-8") as stream:
    json.dump(dict(source=manifest, runtime=runtime), stream, indent=2)
print(json.dumps(dict(sourceFiles=len(manifest), frozenCoreSha256=core[0])))
