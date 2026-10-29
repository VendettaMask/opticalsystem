"""Read-only audit of the frozen local experiment; no product optical computation."""
import hashlib
import json
from pathlib import Path
import statistics
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
evidence = root / "validation/rechecks/2026-10-29/happylens"
final = json.loads((evidence / "comparison-final/summary.json").read_text())
assert len(final) == 18
for record in final:
    state = json.loads((evidence / "comparison-final" / (record["id"] + ("-population" if record["Population"] else "-local") + ".json")).read_text())
    work, options = state["Consumed"], state["Plan"]["Options"]
    for quantity, limit in [("FunctionEvaluations", "MaximumFunctionEvaluations"),
                            ("GradientEvaluations", "MaximumGradientEvaluations"), ("TracedRays", "MaximumTracedRays")]:
        assert work[quantity] <= options[limit]
        assert work[quantity] == sum(event["Work"][quantity] for event in state["Events"])
    assert options["MaximumFunctionEvaluations"] == 300 and options["MaximumTracedRays"] == 2_000_000
    assert state["Plan"]["Targets"]["MaximumRmsRadiusMillimeters"] == .05
    assert state["Plan"]["Targets"]["MaximumSpotRadiusMillimeters"] == .15
    assert state["Plan"]["Targets"]["MaximumDistortionFraction"] == .03
    assert state["TrialCursor"] == 4
    assert (options["Local"]["MaximumUpdates"] + options["Full"]["MaximumUpdates"] if record["Population"]
            else options["Full"]["MaximumUpdates"]) == 4
    preparation = json.loads((evidence / "comparison-final" / (record["id"] + ".seed-search.json")).read_text())["result"]
    assert preparation["work"]["functionEvaluations"] <= 400
    assert preparation["work"]["functionEvaluations"] == sum(a["functionEvaluations"] for a in preparation["attempts"])
    assert all(a["functionEvaluations"] <= 100 for a in preparation["attempts"])
    assert preparation["work"]["tracedRays"] == 0
    assert all(not c["Assessment"]["Violations"] for c in state["AcceptedArchive"])

tests = {}
for file in ["happylens-focused-final.trx", "happylens-scheduling-ui.trx", "happylens-core-final.trx"]:
    tree = ET.parse(evidence / file)
    rows = tree.findall(".//{*}UnitTestResult")
    assert rows and all(row.attrib["outcome"] == "Passed" for row in rows)
    for row in rows:
        tests[row.attrib["testName"]] = file

averages = {mode: statistics.mean(record["Best"]["RmsMicrometers"] for record in final
    if record["id"].startswith(mode) and not record["Population"]) for mode in ["Template", "RandomPhysical", "Delano"]}
audits = []
for group in ["comparison", "comparison-equal-updates", "comparison-final"]:
    records = json.loads((evidence / group / "summary.json").read_text())
    audits.append({"group": group, "runs": len(records), "traced_rays": sum(r["SearchWork"]["TracedRays"] for r in records),
        "functions": sum(r["SearchWork"]["FunctionEvaluations"] for r in records)})
files = {}
for path in evidence.rglob("*"):
    if path.is_file() and path.name != "verification.json":
        files[str(path.relative_to(evidence)).replace("\\", "/")] = hashlib.sha256(path.read_bytes()).hexdigest()
binary_files = [
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll",
    "tools/diagnostics/HappyLens20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/HappyLens20261029/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll",
]
binary_hashes = {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in binary_files}
assert binary_hashes[binary_files[0]] == binary_hashes[binary_files[2]]
assert binary_hashes[binary_files[1]] == binary_hashes[binary_files[3]]
result = {"upstream_commit": "8456f7d9e1fb3264f86b05a4ca43c29173e059ea", "distinct_passing_tests": len(tests),
    "tests": tests, "final_runs": len(final), "original_gates_and_ledgers": "passed",
    "mean_best_worst_field_rms_micrometers_local_only": averages,
    "delano_vs_template_mean_improvement_fraction": 1 - averages["Delano"] / averages["Template"],
    "all_search_batches_separate": audits, "file_sha256": files, "current_binary_sha256": binary_hashes,
    "additional_replay_work": {p.name: json.loads(p.read_text())["Consumed"] for p in evidence.glob("post-format-replay*.json")},
    "limits": ["small three-singlet F/12.5 half-field 5-degree task only", "no qualified two-element claim",
        "no universal population benefit", "no full upstream experiment", "no Linux/macOS or native desktop acceptance"]}
(evidence / "verification.json").write_text(json.dumps(result, indent=2))
print(json.dumps({key: value for key, value in result.items() if key not in ["tests", "file_sha256"]}, indent=2))
