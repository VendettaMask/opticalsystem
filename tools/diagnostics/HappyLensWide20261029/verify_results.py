"""Audit the one frozen wide-field pair without new optical work."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
evidence = root / "validation/rechecks/2026-10-29/happylens-wide21"
protocol = json.loads((evidence / "protocol.json").read_text(encoding="utf-8"))
source_file = root / protocol["SourcePlan"]
assert hashlib.sha256(source_file.read_bytes()).hexdigest() == protocol["SourceSha256"]
original = json.loads(source_file.read_text())["plan"]
common = protocol["TotalBudget"]
rows = json.loads((evidence / "runs/summary.json").read_text())
assert [row["Mode"] for row in rows] == protocol["Order"]

def apertures(topology):
    return [(e["material"], e["front"]["clearSemiDiameterMillimeters"], e["front"]["mechanicalSemiDiameterMillimeters"],
             e["back"]["clearSemiDiameterMillimeters"], e["back"]["mechanicalSemiDiameterMillimeters"]) for e in topology["elements"]]

results = []
for row in rows:
    folder = evidence / "runs" / row["Mode"]
    state = json.loads((folder / "run.autonomous.json").read_text())["checkpoint"]
    plan, work = state["plan"], state["consumed"]
    assert plan["targets"] == original["targets"] == protocol["Targets"]
    assert plan["constraints"] == original["constraints"] == protocol["Constraints"]
    for seed in plan["seeds"]:
        for key in ["schemaVersion", "algorithm", "conjugate", "reference", "stop", "image", "fields", "wavelengths", "glassCatalogs",
                    "temperatureCelsius", "pressureAtmospheres", "rayAimingEnabled", "matchRefractiveIndexData"]:
            assert seed[key] == original["seeds"][0][key], key
        assert apertures(seed) == apertures(original["seeds"][0])
    for key in ["local", "full", "recovery", "axialCompensation", "compensateConic", "curvatureBoundPerMillimeter", "expansion",
                "maximumTrials", "maximumStageTracedRays", "finalValidationRayReserve"]:
        assert plan["options"][key] == original["options"][key], key
    for quantity in ["FunctionEvaluations", "GradientEvaluations", "TracedRays"]:
        key = quantity[0].lower() + quantity[1:]
        assert work[key] == sum(event["work"][key] for event in state["events"])
    assert row["CombinedFunctions"] <= common["maximumFunctionEvaluations"]
    assert row["CombinedGradients"] <= common["maximumGradientEvaluations"]
    assert row["CombinedRays"] <= common["maximumTracedRays"]
    prep_work = row["Preparation"]["Work"] if row["Preparation"] else {"FunctionEvaluations": 0, "GradientEvaluations": 0, "TracedRays": 0}
    assert row["CombinedFunctions"] == prep_work["FunctionEvaluations"] + work["functionEvaluations"]
    assert row["CombinedGradients"] == prep_work["GradientEvaluations"] + work["gradientEvaluations"]
    assert row["CombinedRays"] == prep_work["TracedRays"] + work["tracedRays"]
    assert plan["options"]["maximumFunctionEvaluations"] == 600 - prep_work["FunctionEvaluations"]
    assert plan["options"]["timeLimit"] != "00:01:00"  # Outer preparation/setup time is deducted.
    if row["Preparation"]:
        preparation = json.loads((folder / "preparation.seed-search.json").read_text())["result"]
        assert preparation["work"]["functionEvaluations"] == sum(a["functionEvaluations"] for a in preparation["attempts"])
        assert all(a["functionEvaluations"] <= 30 for a in preparation["attempts"])
        assert preparation["work"]["functionEvaluations"] <= 120
        assert len(preparation["seeds"]) <= 1
        for seed in preparation["seeds"]:
            assert apertures(seed) == apertures(original["seeds"][0])
    assert all(c["assessment"]["meetsTargets"] and not c["assessment"]["violations"] for c in state["acceptedArchive"])
    results.append({"mode": row["Mode"], "within_numeric_caps": True, "within_wall_limit": row["WithinWallLimit"],
        "wall_seconds": row["ComputeAndSaveWallSeconds"], "functions": row["CombinedFunctions"],
        "gradients": row["CombinedGradients"], "rays": row["CombinedRays"],
        "new_seed_retained": row["Preparation"]["Retained"] if row["Preparation"] else None,
        "status": row["Status"]})

test_rows = ET.parse(evidence / "per-face-regression.trx").findall(".//{*}UnitTestResult")
assert len(test_rows) == 5 and all(row.attrib["outcome"] == "Passed" for row in test_rows)
diagnosis = json.loads((evidence / "preparation-first-order-audit.json").read_text())
assert diagnosis["AdditionalTracedRays"] == 0 and diagnosis["AdditionalFirstOrderMetricRequests"] == 12
fingerprints = {str(path.relative_to(evidence)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
    for path in evidence.rglob("*") if path.is_file() and path.name != "verification.json"}
result = {"source_unchanged": True, "gates_training_and_physical_envelope_unchanged": True,
    "search_runs": 2, "no_additional_optimization_runs": True, "distinct_focused_tests_passed": 5,
    "additional_diagnostic_first_order_requests": 12, "additional_diagnostic_rays": 0,
    "arms": results, "file_sha256": fingerprints}
(evidence / "verification.json").write_text(json.dumps(result, indent=2))
print(json.dumps({key: value for key, value in result.items() if key != "file_sha256"}, indent=2))
