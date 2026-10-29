"""Read-only audit of the frozen bounded-candidate comparison. No optical computations."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
evidence = root / "validation/rechecks/2026-10-29/bounded-initial-structure"
protocol = json.loads((evidence / "protocol.json").read_text(encoding="utf-8"))
source = root / protocol["SourcePlan"]
assert hashlib.sha256(source.read_bytes()).hexdigest() == protocol["SourceSha256"]
original = json.loads(source.read_text())["plan"]
rows = json.loads((evidence / "runs/summary.json").read_text())
assert [r["Mode"] for r in rows] == protocol["Order"]
assert len(rows) == 3
budget = protocol["TotalBudget"]
checks = []

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
    for seed in plan["seeds"]:
        for key in ["schemaVersion", "algorithm", "conjugate", "reference", "stop", "image", "fields", "wavelengths", "glassCatalogs",
                    "temperatureCelsius", "pressureAtmospheres", "rayAimingEnabled", "matchRefractiveIndexData"]:
            assert seed[key] == original["seeds"][0][key], key
        assert apertures(seed) == apertures(original["seeds"][0])
    for quantity in ["FunctionEvaluations", "GradientEvaluations", "TracedRays"]:
        key = quantity[0].lower() + quantity[1:]
        assert work[key] == sum(e["work"][key] for e in state["events"])
    prep_work = row["Preparation"]["Work"] if row["Preparation"] else {"FunctionEvaluations": 0, "GradientEvaluations": 0, "TracedRays": 0}
    for combined, quantity, cap in [("CombinedFunctions", "FunctionEvaluations", "maximumFunctionEvaluations"),
                                    ("CombinedGradients", "GradientEvaluations", "maximumGradientEvaluations"),
                                    ("CombinedRays", "TracedRays", "maximumTracedRays")]:
        assert row[combined] == prep_work[quantity] + work[quantity[0].lower() + quantity[1:]]
        assert row[combined] <= budget[cap]
        assert plan["options"][cap] == budget[cap] - prep_work[quantity]
    assert row["WithinWallLimit"]
    assert all(c["assessment"]["meetsTargets"] and not c["assessment"]["violations"] for c in state["acceptedArchive"])
    assert state["trialCursor"] <= original["options"]["maximumTrials"]
    if row["Preparation"]:
        prep = json.loads((directory / "preparation.seed-search.json").read_text())["result"]
        assert prep["work"]["functionEvaluations"] == sum(a["functionEvaluations"] for a in prep["attempts"])
        assert all(a["functionEvaluations"] <= 30 for a in prep["attempts"])
        assert prep["work"]["functionEvaluations"] <= 120
        assert prep["work"]["tracedRays"] == 0
    if row["Mode"] == "bounded-candidates":
        assert state["stopReason"]
        assert plan["options"]["boundedSearch"]["maximumAttemptsPerElementCount"] == 3
        assert plan["seeds"][0] == original["seeds"][0]
        by_id = {c["id"]: c for c in state["candidates"]}
        trials = {}
        for event in state["events"]:
            if event["parentId"] is not None:
                trials.setdefault(event["trialId"], event["parentId"])
            if event["outcome"] in ["ScreenPassed", "ScreenRejected"]:
                assert event["work"]["tracedRays"] == event["work"]["gradientEvaluations"] == 0
                assert 1 <= event["work"]["functionEvaluations"] <= 8
            if event["operation"].startswith("population-") and event["phase"] in ["local", "full"]:
                assert any(e["trialId"] == event["trialId"] and e["outcome"] == "ScreenPassed"
                           and e["sequence"] < event["sequence"] for e in state["events"])
            if event["operation"] == "delete" and event["work"]["functionEvaluations"] > 0:
                assert by_id[event["parentId"]]["assessment"]["meetsTargets"]
        layers = [len(by_id[parent]["topology"]["elements"]) for parent in trials.values()]
        assert all(layers.count(layer) <= 3 for layer in set(layers))
    checks.append({"mode": row["Mode"], "caps_and_original_gates_preserved": True,
                   "wall_seconds": row["ComputeAndSaveWallSeconds"], "stop": state.get("stopReason")})

tests = ET.parse(evidence / "regressions/scoped-final.trx").findall(".//{*}UnitTestResult")
assert len(tests) == 28 and all(t.attrib["outcome"] == "Passed" for t in tests)
extra = ET.parse(evidence / "regressions/stopping-branches.trx").findall(".//{*}UnitTestResult")
assert len(extra) == 2 and all(t.attrib["outcome"] == "Passed" for t in extra)
assert len({t.attrib["testName"] for t in tests + extra}) == 30
audits = [json.loads(p.read_text()) for p in (evidence / "audits").glob("*.json")]
assert all(a["AdditionalTracedRays"] == 0 for a in audits)
result = {"source_unchanged": True, "same_caps_and_original_acceptance": True,
          "frozen_final_comparison_runs": 3, "scoped_tests_passed": len(tests + extra), "arms": checks,
          "post_run_first_order_requests": sum(a["AdditionalFirstOrderMetricRequests"] for a in audits),
          "post_run_diagnostic_rays": 0,
          "file_sha256": {str(p.relative_to(evidence)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
                          for p in evidence.rglob("*") if p.is_file() and p.name not in ["verification.json", "verification.log"]}}
(evidence / "verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k != "file_sha256"}, indent=2))
