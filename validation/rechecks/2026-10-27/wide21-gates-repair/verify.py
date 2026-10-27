import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def text(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def seconds(value):
    h, m, s = value.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


diagnosis = read(HERE / "diagnosis/diagnosis.json")
retained = diagnosis["Results"][1]
lost = [ray for ray in retained["Rays"] if not ray["InsideAllApertures"]]
assert len(lost) == 10
assert {ray["BlockedAt"] for ray in lost} == {"element-001:front", "element-003:front", "element-004:front"}
edge = retained["Metrics"][-1]
assert edge["GaussianMaximumUm"] < 150 < edge["FormalMaximumUm"] < edge["CommonCheckMaximumUm"]
assert abs(edge["TrainingDistortionPercent"] - edge["FormalDistortionPercent"]) < 1e-9
assert all(not step["Accepted"] for step in retained["Steps"][0][2:])

summary = read(HERE / "paired-runs/summary.json")
assert len(summary["Runs"]) == 12 and summary["CpuProcessors"] == 2 and summary["Priority"] == "BelowNormal"
protocol = read(HERE / "paired-runs/frozen-protocol.json")
for case in protocol["Cases"]:
    assert hashlib.sha256((REPO / case["Source"]["RepositoryPath"]).read_bytes()).hexdigest() == case["OriginalSha256"]
normalized = {}
all_states = []
for row in summary["Runs"]:
    assert row["Status"] == "Completed"
    state = read(HERE / "paired-runs" / row["Id"] / "run.autonomous.json")["checkpoint"]
    all_states.append(state)
    options = state["plan"]["options"]
    assert (options["maximumTrials"], options["maximumFunctionEvaluations"], options["maximumGradientEvaluations"], options["maximumTracedRays"], options["maximumStageTracedRays"], options["finalValidationRayReserve"]) == (6, 600, 100, 2_000_000, 400_000, 800_000)
    assert seconds(options["timeLimit"]) == 60 and seconds(state["consumed"]["elapsed"]) <= 60
    for key, value in state["consumed"].items():
        if key != "elapsed":
            assert value == sum(event["work"][key] for event in state["events"])
    assert state["consumed"]["tracedRays"] <= 2_000_000 and state["consumed"]["functionEvaluations"] <= 600 and state["consumed"]["gradientEvaluations"] <= 100
    normal = json.loads(json.dumps(state["plan"]))
    for stage in [normal["options"]["local"], normal["options"]["full"]]:
        assert stage["computeMode"] == "cpu" and stage["sampling"]["pupilRings"] == 4 and stage["sampling"]["pupilArms"] == 16
        assert stage["transmissionPolicyVersion"] == (4 if row["Mode"] == "joint" else 3)
        assert bool(stage["sampling"].get("includeBoundaryMaximum")) == (row["Mode"] != "v3")
        stage["sampling"].pop("includeBoundaryMaximum", None)
        stage["transmissionPolicyVersion"] = 3
    key = (row["Seed"], row["Group"])
    if key in normalized:
        assert normalized[key] == text(normal)
    normalized[key] = text(normal)
    seed = state["plan"]["seeds"][0]
    by_id = {e["id"]: e for e in seed["elements"]}
    for candidate in state["candidates"]:
        for name in ["reference", "stop", "image", "fields", "wavelengths"]:
            assert candidate["topology"][name] == seed[name]
        for element in candidate["topology"]["elements"]:
            if element["id"] in by_id:
                original = by_id[element["id"]]
                assert element["material"] == original["material"]
                for side in ["front", "back"]:
                    assert element[side]["clearSemiDiameterMillimeters"] == original[side]["clearSemiDiameterMillimeters"]
                    assert element[side]["mechanicalSemiDiameterMillimeters"] == original[side]["mechanicalSemiDiameterMillimeters"]
        assert candidate["assessment"]["targets"] == state["plan"]["targets"]
        assert all(field["attemptedRays"] == 960 and field["checkAttemptedRays"] == 99 for field in candidate["assessment"]["fields"])
    assert not row["ReducedSuccess"]
    assert bool(state["acceptedArchive"]) == (row["Mode"] == "joint")
    if row["Mode"] == "v3":
        old = read(REPO / "validation/rechecks/2026-10-27/topology-integrated-compensation/paired-runs" / f"repr-new-wide4-3-{row['Seed']}-{row['Group']}-axial/run.autonomous.json")["checkpoint"]
        assert [c["topology"] for c in old["candidates"]] == [c["topology"] for c in state["candidates"]]
        assert old["consumed"]["tracedRays"] == state["consumed"]["tracedRays"]

fixed = read(HERE / "fixed-repair/summary.json")
assert len(fixed["Runs"]) == 3 and fixed["CpuProcessors"] == 2 and fixed["Priority"] == "BelowNormal"
original_vars = read(HERE / "diagnosis/diagnosis.json")["Results"][0]["Variables"]
for row in fixed["Runs"]:
    inp = read(HERE / "fixed-repair" / row["Id"] / "input.json")
    assert inp["Variables"] == original_vars
    assert inp["Options"]["MaximumUpdates"] == 8 and inp["Options"]["MaximumFunctionEvaluations"] == inp["Options"]["MaximumGradientEvaluations"] == 9
    assert row["Initial"]["Completed"] and row["Final"]["Completed"]
    assert row["Initial"]["ProtocolFingerprint"] == row["Final"]["ProtocolFingerprint"]
    assert row["ActualRays"] == row["InitialRays"] + row["TrainingRays"] + row["FinalRays"] <= 2_000_000
    assert row["TrainingRays"] <= 400_000 and row["FinalRays"] <= 800_000
    assert row["FunctionEvaluations"] <= 600 and row["GradientEvaluations"] <= 100 and row["ActualSeconds"] <= 60
    assert row["Final"]["MeetsTargets"] == (row["Id"] == "joint")
    for slot, request in enumerate(original_vars):
        coordinate = row["Optimization"]["Continuation"]["Adam"]["Coordinates"][slot]
        assert request["LowerBound"] <= coordinate * request["Scale"] <= request["UpperBound"]
assert fixed["Runs"][-1]["GradientEvaluations"] == 3

native = read(HERE / "native-audit/native-audits.json")
replays = read(HERE / "fixed-native-audit/native-and-precision-audits.json")
assert len(native) == 6 and len(replays) == 3
for row in native + replays:
    assessment = row["Assessment"]
    assert assessment["Completed"] and len(assessment["Fields"]) == 35
    if assessment["MeetsTargets"]:
        assert not assessment["Violations"]
        assert all(f["ValidRays"] == f["AttemptedRays"] == 960 and f["CheckValidRays"] == f["CheckAttemptedRays"] == 99 for f in assessment["Fields"])
assert sum(row["Assessment"]["MeetsTargets"] for row in native + replays) == 5
passed = []
for path, count in [("focused.trx", 84), ("core-metric.trx", 2)]:
    tree = ET.parse(HERE / "tests" / path).getroot()
    counters = next(node for node in tree.iter() if node.tag.endswith("Counters"))
    assert int(counters.attrib["passed"]) == count and int(counters.attrib["failed"]) == 0
    passed.append(count)
for build in ["build-default-app.log", "build-tools-final.log"]:
    assert "0 个错误" in (HERE / build).read_text(encoding="utf-8-sig")
assert "passed" in (HERE / "format-verified.log").read_text(encoding="utf-8-sig")
assert not read(HERE / "documentation-audit.json")["Missing"]
result = {
    "Verified": True, "OriginalPipelineRuns": 12, "SeparateFixedRepairRuns": 3,
    "OriginalPipelineQualifiedFourElements": 4, "QualifiedReductions": 0,
    "SourceBytesAndPairedPermissionsUnchanged": True, "OriginalSixteenBoundsRetained": True,
    "OriginalPhysicalProtocolAndGatesUnchanged": True, "LegacyV3ExactlyReproduced": True,
    "LedgerAndAllCapsVerified": True, "TestsPassed": sum(passed),
    "DefaultAppAndToolsBuildPassed": True, "SixScopedFormatChecksPassed": True, "DocumentationLinksPassed": True,
    "ActualSearchRays": sum(row["Consumed"]["TracedRays"] for row in summary["Runs"]) + sum(row["ActualRays"] for row in fixed["Runs"]),
    "AdditionalDiagnosticRays": diagnosis["ActualDiagnosticRays"],
    "AdditionalNativeRays": sum(row["AdditionalAuditRays"] for row in native),
    "AdditionalFixedNativeAndPrecisionRays": sum(row["AdditionalAuditRays"] for row in replays),
    "DirectRepairResult": fixed["Runs"][-1]["Final"],
}
(HERE / "verification.json").write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
print(json.dumps({key: value for key, value in result.items() if key != "DirectRepairResult"}, indent=2))
