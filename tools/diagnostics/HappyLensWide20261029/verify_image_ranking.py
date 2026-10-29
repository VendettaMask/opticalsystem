"""Read-only integrity and budget audit. Never supplies product optical results."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
evidence = root / "validation/rechecks/2026-10-29/seed-image-ranking"
protocol_path = evidence / "coarse-protocol.json"
protocol = json.loads(protocol_path.read_text(encoding="utf-8"))
source = root / protocol["SourcePlan"]
assert hashlib.sha256(source.read_bytes()).hexdigest() == protocol["SourceSha256"]
original = json.loads(source.read_text(encoding="utf-8"))["plan"]
runs = evidence / "comparison-coarse"
assert (runs / "frozen-protocol.json").read_bytes() == protocol_path.read_bytes()
rows = json.loads((runs / "summary.json").read_text(encoding="utf-8"))
assert [row["Mode"] for row in rows] == protocol["Order"]
assert json.loads((runs / "runtime.json").read_text())["PriorityClass"] == "Normal"
budget = protocol["TotalBudget"]
audits = []
for row in rows:
    mode = row["Mode"]
    directory = runs / mode
    state = json.loads((directory / "run.autonomous.json").read_text())["checkpoint"]
    plan, work = state["plan"], state["consumed"]
    prep = json.loads((directory / "preparation.seed-search.json").read_text())["result"]
    assert plan["targets"] == original["targets"] == protocol["Targets"]
    assert plan["constraints"] == original["constraints"] == protocol["Constraints"]
    for key in ["local", "full", "recovery", "axialCompensation", "compensateConic", "curvatureBoundPerMillimeter",
                "expansion", "maximumTrials", "maximumStageTracedRays", "finalValidationRayReserve"]:
        assert plan["options"][key] == original["options"][key], key
    assert plan["seeds"][0] == original["seeds"][0]
    for seed in plan["seeds"]:
        for key in ["reference", "stop", "image", "fields", "wavelengths", "glassCatalogs", "rayAimingEnabled",
                    "matchRefractiveIndexData", "temperatureCelsius", "pressureAtmospheres"]:
            assert seed[key] == original["seeds"][0][key], key
        for element, source_element in zip(seed["elements"], original["seeds"][0]["elements"], strict=True):
            assert element["material"] == source_element["material"]
            for face in ["front", "back"]:
                for aperture in ["clearSemiDiameterMillimeters", "mechanicalSemiDiameterMillimeters"]:
                    assert element[face][aperture] == source_element[face][aperture]
    screens = prep.get("imageScreens", [])
    assert len(prep["attempts"]) == 4
    assert prep["work"]["functionEvaluations"] == sum(a["functionEvaluations"] for a in prep["attempts"]) + len(screens)
    assert all(a["functionEvaluations"] <= 30 for a in prep["attempts"])
    assert prep["work"]["functionEvaluations"] <= 120
    assert prep["work"]["tracedRays"] == sum(screen["tracedRays"] for screen in screens) <= 10000
    for combined, key in [("CombinedFunctions", "functionEvaluations"), ("CombinedGradients", "gradientEvaluations"), ("CombinedRays", "tracedRays")]:
        assert work[key] == sum(event["work"][key] for event in state["events"])
        assert row[combined] == prep["work"][key] + work[key] <= budget["maximum" + key[0].upper() + key[1:]]
        assert plan["options"]["maximum" + key[0].upper() + key[1:]] == budget["maximum" + key[0].upper() + key[1:]] - prep["work"][key]
    assert row["WithinWallLimit"] and row["ComputeAndSaveWallSeconds"] <= 60
    assert row["PersistenceReserveSeconds"] == 10
    assert all(c["assessment"]["meetsTargets"] and c["assessment"]["completed"] and not c["assessment"]["violations"] for c in state["acceptedArchive"])
    if mode == "image-ranked":
        assert len(screens) == 4
        assert all(screen["outcome"] == "Screened" and screen["completedFields"] == screen["expectedFields"] == 3
                   and screen["boundaryChecksPassed"] and screen["minimumValidFraction"] == 1
                   and 0 < screen["tracedRays"] <= 2500 for screen in screens)
        assert len(prep["seeds"]) == 1
        assert prep["options"]["imageScreening"]["boundaryCheckArms"] == 8
        assert prep["options"]["imageScreening"]["pupilRings"] == 1
        assert prep["options"]["imageScreening"]["pupilArms"] == 4
    else:
        assert not screens and "imageScreening" not in prep["options"]
    audits.append({"mode": mode, "functions": row["CombinedFunctions"], "gradients": row["CombinedGradients"],
                   "rays": row["CombinedRays"], "wall_seconds": row["ComputeAndSaveWallSeconds"],
                   "original_physical_gates_and_caps_preserved": True})

latest_tests = {}
test_files = ["screening-focused.trx", "screening-ui.trx", "screening-final.trx", "screening-coarse-development.trx", "screening-verified.trx"]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
for name in test_files:
    for result in ET.parse(evidence / "tests" / name).findall(".//t:UnitTestResult", ns):
        latest_tests[result.attrib["testName"]] = result.attrib["outcome"]
assert len(latest_tests) == 18 and set(latest_tests.values()) == {"Passed"}, latest_tests
output = {"status": "passed", "protocol_sha256": hashlib.sha256(protocol_path.read_bytes()).hexdigest(),
          "scope": "one fixed wide-angle task, not a broad benchmark or a claim of final design improvement",
          "runs": audits, "distinct_latest_tests_passed": len(latest_tests)}
(evidence / "verification.json").write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
print(json.dumps(output, indent=2))
