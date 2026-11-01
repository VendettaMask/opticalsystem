"""Freeze final candidate protocol; bookkeeping only, never imports optical endpoints."""
from pathlib import Path
import hashlib
import json
import shutil
import sys
import zipfile

repo, frozen = (Path(p).resolve() for p in sys.argv[1:])
base = repo / "validation/rechecks/2026-10-31/f8-completion"
evidence = base / "final-cold-v4-confirmed"
if evidence.exists():
    raise SystemExit("Existing evidence is never overwritten.")
evidence.mkdir()
for name in ("source.zip", "source-sha256.json"):
    shutil.copy2(base / "source-v4" / name, evidence / name)
shutil.copytree(frozen / "tools/diagnostics/F8ColdStart/bin/Release/net10.0", evidence / "runtime")
manifest = [{"path": p.relative_to(evidence).as_posix(), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
            for p in sorted((evidence / "runtime").rglob("*")) if p.is_file()]
(evidence / "runtime-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
with zipfile.ZipFile(base / "paired-v3/source.zip") as old, zipfile.ZipFile(evidence / "source.zip") as new:
    changes = [name for name in sorted(set(old.namelist()) | set(new.namelist()))
               if name not in old.namelist() or name not in new.namelist() or old.read(name) != new.read(name)]
    if any("TopologyGateCompensation" in name
           or name.endswith("FixedTopologyOptimizer.cs") for name in changes):
        raise SystemExit("Control numerical source equivalence must be reviewed before proceeding.")
(evidence / "source-changes-from-v3.json").write_text(json.dumps(changes, indent=2), encoding="utf-8")
protocol = {
    "version": "f8-completion-final-v4", "seeds": list(range(42, 47)), "arms": ["control", "candidate"],
    "armPolicies": {"control": {"version": 1, "exploreBackFocus": True}, "candidate": {"version": 4, "exploreBackFocus": True}},
    "budget": {"functions": 600, "gradients": 600, "rays": 10000000, "seconds": 600},
    "cold": "Five NEW candidate processes/RNGs using one frozen v4 runtime; source shapes erased, fresh layouts; no endpoint/checkpoint/optimizer-state imports, including the development seed46 pass.",
    "input": "One original F8 seed44 input specification; only the random seed changes.",
    "comparison": "Reuse the complete original five-seed control cohort from paired-v3 (Cold1/Gate2, back-focus exploration enabled), each originally cold and within the same caps. They are historical controls, NOT new v4-runtime runs. Preserve original protocol/runtime/source provenance and byte-for-byte control files. Gate solver source is identical; v4 changes the explicitly versioned candidate phase schedule and the old control branch is unchanged. Other simultaneous Core changes concern GRIN/MEMS/import/serialization and are enumerated in source-changes-from-v3.json; do not claim complete Core source or runtime identity. Independently replay every assessed original control/candidate optic under the final frozen runtime and require exact assessment identity before reporting comparison.",
    "execution": "Two bounded normal-priority candidate workers, deterministic seed order42-46, fresh process per seed, nested computation parallelism suppressed. Historical controls used the earlier frozen v3 runtime; no same-runtime or speedup claim.",
    "gates": "Original lens count, materials, aperture/stop/image, fields/spectrum, shape permissions, axial/coefficient bounds and complete formal optical/manufacturing validation remain unchanged.",
    "development": "All earlier failed batches and warm repairs are separate; no successful endpoints are spliced into this final candidate cohort.",
    "excluded": "F4 remains its original specification conflict. No DeepSeek, native desktop interaction, commits or publishing."
}
(evidence / "protocol.json").write_text(json.dumps(protocol, indent=2), encoding="utf-8")
print(f"Frozen final v4 candidates; source changes: {len(changes)}. Historical control provenance will be attached after its complete cohort finishes.")
