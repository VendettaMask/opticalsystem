"""Collect measured results and provenance without changing frozen references."""
import csv
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
repo = root.parents[3]
upstream = Path(sys.argv[1]).resolve()
fixture_dir = repo / "labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/Fixtures"
load = lambda path: json.loads(path.read_text(encoding="utf-8"))
policy = load(root / "regenerated-policy.json")
handoff = load(root / "regenerated-handoff.json")
fixed = load(root / "fixed-ray-comparison.json")
training = load(root / "upstream-short-training/run-summary.json")
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx = ET.parse(root / "reference-rerun-host.trx")
counters = trx.find(".//t:Counters", ns).attrib
phase_rows = {}
for phase, relative in [("curriculum", "loss_history.tsv"), ("refinement", "refine/loss_history.tsv")]:
    with (root / "upstream-short-training" / relative).open() as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))
    phase_rows[phase] = {"rows": len(rows), "first": rows[0], "last": rows[-1]}
checks = {
    "regeneratedPolicyExactlyEqualsFrozenJson": policy == load(fixture_dir / "autolens-e58-policy.json"),
    "regeneratedHandoffExactlyEqualsFrozenJson": handoff == load(fixture_dir / "autolens-e58-handoff.json"),
    "sameInputValidityAgrees": all(not r["validityMismatches"] for r in fixed["results"]),
    "sameInputPositionWithin1eMinus7Mm": all(r["maximumPositionAbsoluteErrorMm"] < 1e-7 for r in fixed["results"]),
    "sameInputImageLossWithin1eMinus8": all(c["absoluteError"] < 1e-8 for r in fixed["results"] for c in r["losses"]),
    "sameInputSelectedGradientWithin1eMinus6": all(c["maximumGradientAbsoluteError"] < 1e-6 for r in fixed["results"] for c in r["losses"]),
    "referenceTests15Passed": counters["passed"] == "15" and counters["failed"] == "0" and counters["notExecuted"] == "0",
    "shortTraining9Plus9Updates": all(phase["rows"] == 9 for phase in phase_rows.values()),
}
source_changes = subprocess.check_output(["git", "diff", "--name-only", "HEAD"], cwd=upstream, text=True).splitlines()
files = [path for path in root.rglob("*") if path.is_file()
         and not {"bin", "obj", "__pycache__"}.intersection(path.relative_to(root).parts)
         and path.name != "comparison-summary.json"]
summary = {
    "date": "2026-10-26", "timezone": "Asia/Shanghai",
    "scope": "Original Python execution, frozen optimization rules, same-input initial-lens CPU/CUDA trace and image-loss gradients; short original mobile training. No full default output equivalence claim.",
    "upstreamRepository": "https://github.com/AI4Optics/AutoLens",
    "upstreamCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=upstream, text=True).strip(),
    "upstreamSourceDirectory": str(upstream), "upstreamTrackedChanges": source_changes,
    "repositoryHead": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
    "productNumericalCodeChangesThisTask": [], "checks": checks, "allChecksPassed": all(checks.values()) and not source_changes,
    "tests": {key: int(value) for key, value in counters.items()},
    "fixedRays": fixed, "shortTraining": training, "trainingLossHistory": phase_rows,
    "knownNonEquivalence": [
        "Default Python run uses float32; shared product Core uses double. The direct fixed-ray comparison explicitly uses float64 on both sides.",
        "Training sample stream is not PyTorch CUDA Philox, including diagnostic random-number consumption; same seed does not imply the same training trajectory.",
        "The separate CAMERA_CONFIG topology is not fully reproduced by the mobile policy.",
        "Product hard physical acceptance gates remain separate from upstream training losses.",
        "This short original run ends at about 18.2014 mm and F/6.552; it does not meet the 5 mm F/1.8 target and is not a quality-success result.",
    ],
    "previousFullDefaultRun": {"source": "docs/validation/AUTOLENS_UPSTREAM_ALIGNMENT_2026-10-24.json",
        "historicalOnly": True, "exitCode": 0, "durationSeconds": 5742.922, "updates": [2001, 3001],
        "oldAbsoluteSourceDirectoryPresentOnThisComputer": Path("D:/Projects/autolens-upstream-20261024/source").exists()},
    "intermediateFailuresPreserved": ["Sandbox testhost connection abort (reference-rerun.trx)",
        "Initial development probe missing namespace and invalid parameter-origin declarations; corrected probe succeeds, original logs retained"],
    "evidenceSha256": {str(path.relative_to(root)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
                       for path in sorted(files)},
}
(root / "comparison-summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"checks": checks, "allChecksPassed": summary["allChecksPassed"]}, ensure_ascii=False))
if not summary["allChecksPassed"]:
    raise SystemExit(1)
