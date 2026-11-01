"""Copy an already completed cold control cohort with explicit historical provenance."""
from pathlib import Path
import hashlib
import json
import shutil
import sys

base = Path(sys.argv[1]).resolve()
source, final = base / "paired-v3", base / "final-cold-v6"
if (final / "control").exists():
    raise SystemExit("Existing controls are never overwritten.")
for seed in range(42, 47):
    directory = source / "control" / f"seed{seed}"
    for name in ("result.json", "input.json", "summary.json"):
        if not (directory / name).exists():
            raise SystemExit(f"Control cohort incomplete: seed{seed}/{name}")
    saved = json.loads((directory / "result.json").read_text(encoding="utf-8"))
    if saved["result"]["policy"]["version"] != 1:
        raise SystemExit("Unexpected historical control version.")
shutil.copytree(source / "control", final / "control")
provenance = final / "historical-control-provenance"
provenance.mkdir()
for name in ("source.zip", "source-sha256.json", "runtime-sha256.json", "protocol.json", "run.ps1"):
    shutil.copy2(source / name, provenance / name)
manifest = []
for p in sorted((source / "control").rglob("*")):
    if not p.is_file():
        continue
    relative = p.relative_to(source)
    expected = hashlib.sha256(p.read_bytes()).hexdigest()
    if hashlib.sha256((final / relative).read_bytes()).hexdigest() != expected:
        raise SystemExit(f"Copy mismatch: {relative}")
    manifest.append({"path": relative.as_posix(), "sha256": expected})
(provenance / "control-files-sha256.json").write_text(json.dumps({"source": "../../paired-v3", "newSearch": False,
    "originalRuntime": "../../paired-v3/runtime", "files": manifest}, indent=2), encoding="utf-8")
print(f"Attached all five historical controls byte-for-byte ({len(manifest)} files).")
