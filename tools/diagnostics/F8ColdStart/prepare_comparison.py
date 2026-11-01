"""Declare the experiment before either arm starts; source/archive housekeeping only."""
from pathlib import Path
import json
import shutil
import sys

snapshot, evidence = (Path(arg).resolve() for arg in sys.argv[1:3])
candidate_version = int(sys.argv[3]) if len(sys.argv) == 4 else 2
if evidence.exists():
    raise SystemExit("Comparison evidence already exists.")
evidence.mkdir(parents=True)
for name in ("source.zip", "source-sha256.json"):
    shutil.copy2(snapshot / name, evidence / name)
protocol = {
    "version": f"f8-completion-paired-v{candidate_version}", "seeds": list(range(42,47)), "arms": ["control", "candidate"],
    "armPolicies": {"control": {"version":1,"exploreBackFocus":True}, "candidate":{"version":candidate_version,"exploreBackFocus":True}},
    "budget": {"functions":600,"gradients":600,"rays":10000000,"seconds":600},
    "cold": "Fresh process and RNG per arm/seed; all source shapes erased; newly sampled layouts; no repaired endpoint/checkpoint/optimizer-state import.",
    "input": "One original F8 seed44 task specification; only the RNG seed changes.",
    "comparison": "Both arms explore the same legal back-focus space. Control retains the previous v1 cold schedule and gate compensation v2; candidate uses the declared versioned schedule, consistent signed-clearance/hard correction and Core image-tangent step stabilization; v3 cold additionally uses inward manufacturing/image targets in gate v4.",
    "execution": "Two bounded normal-priority worker slots with fresh processes and nested computation parallelism suppressed; deterministic candidate-first then control work queue; same frozen binary runtime; no speedup claim." if candidate_version >= 3 else "Two bounded normal-priority workers by arm, each sequential across seeds and suppressing nested computation parallelism; same frozen binary runtime; no speedup claim.",
    "gates": "Original count, materials/permissions, physical apertures, fixed reference/stop/image, fields/spectrum, curvature/axial/coefficient bounds and original complete TopologyValidator unchanged.",
    "development": "Old-endpoint repair probes and the earlier no-image-curvature seed42 cold probe are development-only and excluded from the comparison.",
    "excluded": "F4 retains the original specification conflict. No DeepSeek, native desktop control, commits or publishing."
}
if candidate_version >= 4:
    protocol["comparison"] += " Candidate v4 warms A4 with sparse 2x8 training then uses the remaining full budget for original-spec dense A6 compensation; every stage is independently checked by the original final validator."
(evidence / "protocol-design.json").write_text(json.dumps(protocol,indent=2),encoding="utf-8")
print("Paired protocol declared before execution.")
