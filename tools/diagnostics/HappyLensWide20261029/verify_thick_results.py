"""Read-only audit of thick Delano construction, original gates, work and scoped tests."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
evidence = root / "validation/rechecks/2026-10-29/delano-thick-materialization"
protocol = json.loads((evidence / "protocol.json").read_text(encoding="utf-8"))
source = root / protocol["SourcePlan"]
assert hashlib.sha256(source.read_bytes()).hexdigest() == protocol["SourceSha256"]
original = json.loads(source.read_text())["plan"]
rows = json.loads((evidence / "runs/summary.json").read_text())
assert [row["Mode"] for row in rows] == protocol["Order"] == ["original", "bounded-candidates", "thick-delano"]
budget = protocol["TotalBudget"]
checks = []
preparations = {}

def apertures(topology):
    return [(e["material"], e["front"]["clearSemiDiameterMillimeters"], e["front"]["mechanicalSemiDiameterMillimeters"],
             e["back"]["clearSemiDiameterMillimeters"], e["back"]["mechanicalSemiDiameterMillimeters"]) for e in topology["elements"]]

for row in rows:
    directory = evidence / "runs" / row["Mode"]
    state = json.loads((directory / "run.autonomous.json").read_text())["checkpoint"]
    plan, work = state["plan"], state["consumed"]
    assert plan["targets"] == original["targets"] == protocol["Targets"]
    assert plan["constraints"] == original["constraints"] == protocol["Constraints"]
    for key in ["local", "full", "recovery", "axialCompensation", "compensateConic", "curvatureBoundPerMillimeter", "expansion",
                "maximumTrials", "maximumStageTracedRays", "finalValidationRayReserve"]:
        assert plan["options"][key] == original["options"][key], key
    assert plan["seeds"][0] == original["seeds"][0]
    for seed in plan["seeds"]:
        for key in ["schemaVersion", "algorithm", "conjugate", "reference", "stop", "image", "fields", "wavelengths", "glassCatalogs",
                    "temperatureCelsius", "pressureAtmospheres", "rayAimingEnabled", "matchRefractiveIndexData"]:
            assert seed[key] == original["seeds"][0][key], key
        assert apertures(seed) == apertures(original["seeds"][0])
    prep_work = row["Preparation"]["Work"] if row["Preparation"] else {"FunctionEvaluations": 0, "GradientEvaluations": 0, "TracedRays": 0}
    for combined, quantity, cap in [("CombinedFunctions", "FunctionEvaluations", "maximumFunctionEvaluations"),
                                    ("CombinedGradients", "GradientEvaluations", "maximumGradientEvaluations"),
                                    ("CombinedRays", "TracedRays", "maximumTracedRays")]:
        key = quantity[0].lower() + quantity[1:]
        assert work[key] == sum(e["work"][key] for e in state["events"])
        assert row[combined] == prep_work[quantity] + work[key]
        assert row[combined] <= budget[cap]
        assert plan["options"][cap] == budget[cap] - prep_work[quantity]
    assert row["WithinWallLimit"]
    assert row["ComputeAndSaveWallSeconds"] <= row["WallLimitSeconds"] == 60
    assert all(c["assessment"]["meetsTargets"] and not c["assessment"]["violations"] for c in state["acceptedArchive"])
    assert state["trialCursor"] <= original["options"]["maximumTrials"]
    if row["Preparation"]:
        prep = json.loads((directory / "preparation.seed-search.json").read_text())["result"]
        preparations[row["Mode"]] = prep
        assert len(prep["attempts"]) == 4
        assert prep["work"]["functionEvaluations"] == sum(a["functionEvaluations"] for a in prep["attempts"])
        assert all(a["functionEvaluations"] <= 30 for a in prep["attempts"])
        assert prep["work"]["functionEvaluations"] <= 120 and prep["work"]["tracedRays"] == 0
        assert plan["options"]["boundedSearch"] == {k[0].lower() + k[1:]: v for k, v in protocol["BoundedPolicy"].items()}
        assert state["stopReason"] == "insufficientCompleteTrialBudget"
        assert budget["maximumTracedRays"] - row["CombinedRays"] < budget["finalValidationRayReserve"] + protocol["BoundedPolicy"]["MinimumCompensationRays"]
        by_id = {c["id"]: c for c in state["candidates"]}
        trials = {}
        for event in state["events"]:
            if event["parentId"]:
                trials.setdefault(event["trialId"], event["parentId"])
            if event["outcome"] in ["ScreenPassed", "ScreenRejected"]:
                assert event["work"]["tracedRays"] == event["work"]["gradientEvaluations"] == 0
                assert 1 <= event["work"]["functionEvaluations"] <= 8
            if event["operation"].startswith("population-") and event["phase"] in ["local", "full"]:
                assert any(e["trialId"] == event["trialId"] and e["outcome"] == "ScreenPassed" and e["sequence"] < event["sequence"] for e in state["events"])
            if event["operation"] == "delete" and event["work"]["functionEvaluations"]:
                assert by_id[event["parentId"]]["assessment"]["meetsTargets"]
        layers = [len(by_id[parent]["topology"]["elements"]) for parent in trials.values()]
        assert all(layers.count(layer) <= 3 for layer in set(layers))
    checks.append({"mode": row["Mode"], "original_gates_and_caps_preserved": True, "functions": row["CombinedFunctions"],
                   "gradients": row["CombinedGradients"], "rays": row["CombinedRays"], "wall_seconds": row["ComputeAndSaveWallSeconds"]})

previous, thick = preparations["bounded-candidates"], preparations["thick-delano"]
restored_options = copy.deepcopy(thick["options"])
assert restored_options["generation"].pop("materializationVersion") == 2
assert restored_options == previous["options"]
assert not previous["seeds"] and len(thick["seeds"]) == 1
assert all(a["outcome"] == "FirstOrderFeasibleOnly" for a in thick["attempts"])
assert thick["work"]["functionEvaluations"] == 52 and thick["work"]["matrixEvaluations"] == 136
new_seed = next(c for c in rows[-1]["InitialQuality"] if c["Id"] == "seed-1")["Assessment"]
assert new_seed["Completed"] and not new_seed["MeetsTargets"] and new_seed["Failure"] is None
assert new_seed["Fields"] == 35 and new_seed["MinimumIntegrationValid"] == 960 and new_seed["MinimumChecksValid"] == 99
assert all(v["Code"].endswith(":rms") or v["Code"].endswith(":maximum") for v in new_seed["Violations"])
assert abs(new_seed["EffectiveFocalLengthMillimeters"] / original["targets"]["effectiveFocalLengthMillimeters"] - 1) < 1e-10
assert abs(new_seed["FNumber"] / original["targets"]["fNumber"] - 1) < 1e-10

latest = {}
for name in ["core-scoped.trx", "regressions/module-scoped.trx", "regressions/materialization-final.trx", "regressions/principal-plane-diagnosis-final.trx"]:
    for test in ET.parse(evidence / name).findall(".//{*}UnitTestResult"):
        latest[test.attrib["testName"]] = test.attrib["outcome"]
assert len(latest) == 43 and all(outcome == "Passed" for outcome in latest.values())
diagnosis = json.loads((evidence / "regressions/legacy-materialization-diagnosis.json").read_text())
assert diagnosis["AdditionalFirstOrderRequests"] == 28 and diagnosis["AdditionalTracedRays"] == 0
for start in diagnosis["Starts"]:
    assert start["EffectiveDistances"][-1] > start["Trace"]["DistancesMillimeters"][-1]
    assert start["MarginalAtFixedStop"] > start["ActualStopRadius"]
before = json.loads((evidence / "regressions/thick-before-restoration.json").read_text())
assert before["TracedRays"] == 0
for start in before["Starts"]:
    assert abs(start["Focal"] / protocol["Targets"]["effectiveFocalLengthMillimeters"] - 1) < 1e-10
    assert abs(start["FNumber"] / protocol["Targets"]["fNumber"] - 1) < 1e-10 and abs(start["FocusA"]) < 1e-10
audits = [json.loads(p.read_text()) for p in (evidence / "audits").glob("*.json")]
assert sum(a["AdditionalFirstOrderMetricRequests"] for a in audits) == 24 and all(a["AdditionalTracedRays"] == 0 for a in audits)
result = {"source_unchanged": True, "same_caps_and_original_acceptance": True, "frozen_comparison_runs": 3,
          "first_order_and_manufacturing_passes_before_after": [0, 4], "scoped_distinct_tests_finally_passed": len(latest),
          "arms": checks, "post_run_first_order_requests": 24, "post_run_diagnostic_rays": 0,
          "file_sha256": {str(p.relative_to(evidence)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in evidence.rglob("*") if p.is_file() and p.name not in ["verification.json", "verification.log"]}}
(evidence / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k != "file_sha256"}, indent=2))
