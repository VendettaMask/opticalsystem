"""Declare a new complete cohort before execution; archive housekeeping only."""
from pathlib import Path
import hashlib
import json
import shutil
import sys
import zipfile

repo, frozen = (Path(p).resolve() for p in sys.argv[1:])
base = repo / "validation/rechecks/2026-10-31/f8-completion"
evidence = base / "final-cold-v6"
if evidence.exists():
    raise SystemExit("Existing evidence is never overwritten.")
evidence.mkdir()
for name in ("source.zip", "source-sha256.json"):
    shutil.copy2(base / "source-v6" / name, evidence / name)
shutil.copytree(frozen / "tools/diagnostics/F8ColdStart/bin/Release/net10.0", evidence / "runtime")
manifest = [{"path": p.relative_to(evidence).as_posix(), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
            for p in sorted((evidence / "runtime").rglob("*")) if p.is_file()]
(evidence / "runtime-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
with zipfile.ZipFile(base / "paired-v3/source.zip") as old, zipfile.ZipFile(evidence / "source.zip") as new:
    changes = [name for name in sorted(set(old.namelist()) | set(new.namelist()))
               if name not in old.namelist() or name not in new.namelist() or old.read(name) != new.read(name)]
(evidence / "source-changes-from-v3.json").write_text(json.dumps(changes, indent=2), encoding="utf-8")
protocol = json.loads((base / "final-cold-v4-confirmed/protocol.json").read_text(encoding="utf-8"))
protocol["version"] = "f8-completion-final-v6"
protocol["armPolicies"]["candidate"]["version"] = 6
protocol["cold"] = "Five NEW candidate processes/RNGs using one frozen v6 runtime; source shapes erased, fresh layouts; no endpoint/checkpoint/optimizer-state imports, including all v4 successes."
protocol["comparison"] = "Historical five-seed controls are copied byte-for-byte from paired-v3 (Cold1/Gate2, same exploration/input/caps). They are NOT fresh v6 runs and do NOT have the same runtime. Full old provenance and source-change list retained. Candidate Cold6 uses Gate4 inward targets and early sparse A4, additionally caps each stage at the existing400-update solver limit. Concurrent unrelated Core changes are disclosed. Every original assessment must replay exactly under the final frozen runtime before this comparison is accepted."
protocol["execution"] = "Two bounded normal-priority candidate workers, deterministic seed order42-46, fresh process per seed, nested parallelism suppressed. Earlier controls retain their original runtime; no same-runtime or speedup claim."
(evidence / "protocol.json").write_text(json.dumps(protocol, indent=2), encoding="utf-8")
print(f"Declared new final Cold6/Gate4 cohort, {len(changes)} changed source paths disclosed.")
