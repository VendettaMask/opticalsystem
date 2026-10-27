"""Read-only evidence checks; no optical calculation or search replay."""
import hashlib
import json
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def stable(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def seconds(value):
    h, m, s = value.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


rows = []
time_overruns = []
for batch, count in [("paired-runs", 32), ("qualified-control", 8)]:
    summary = read(HERE / batch / "summary.json")
    assert len(summary["Runs"]) == count
    assert summary["CpuProcessors"] == 2 and summary["Priority"] == "BelowNormal"
    protocol_path = HERE / batch / "frozen-protocol.json"
    protocol = read(protocol_path)
    assert hashlib.sha256(protocol_path.read_bytes()).hexdigest() == summary["ProtocolSha256"]
    paired = {}
    for task in protocol["Cases"]:
        source = REPO / task["Source"]["RepositoryPath"]
        assert hashlib.sha256(source.read_bytes()).hexdigest() == task["OriginalSha256"]
    for row in summary["Runs"]:
        assert row["Status"] != "Failed", row
        state = read(HERE / batch / row["Id"] / "run.autonomous.json")["checkpoint"]
        plan = state["plan"]
        options = plan["options"]
        assert state["reservation"] is None
        assert options["maximumTracedRays"] == 2_000_000
        assert options["maximumFunctionEvaluations"] == 600
        assert options["maximumGradientEvaluations"] == 100
        assert options["maximumTrials"] == 6
        assert options["maximumStageTracedRays"] == 400_000
        assert options["finalValidationRayReserve"] == 800_000
        assert seconds(options["timeLimit"]) == 60
        assert row["Consumed"]["TracedRays"] <= 2_000_000
        assert row["Consumed"]["FunctionEvaluations"] <= 600
        assert row["Consumed"]["GradientEvaluations"] <= 100
        if seconds(row["Consumed"]["Elapsed"]) > 60:
            assert row["Status"] == "BudgetExhausted"
            time_overruns.append({"Batch": batch, "Id": row["Id"], "ActualSeconds": seconds(row["Consumed"]["Elapsed"]),
                                  "ConfiguredSeconds": 60, "RetainedAsTimeBudgetExhausted": True})
        for key, value in state["consumed"].items():
            if key != "elapsed":
                assert value == sum(event["work"][key] for event in state["events"]), (row["Id"], key)
        for stage in [options["local"], options["full"]]:
            assert stage["computeMode"] == "cpu" and not stage["performFinalAssessment"]
            assert stage["sampling"]["pupilRings"] == 4 and stage["sampling"]["pupilArms"] == 16
            assert stage["sampling"]["includeBoundaryClearance"]
            assert stage["transmissionFirst"] and stage["transmissionPolicyVersion"] == 3
        normal = json.loads(json.dumps(plan))
        normal["options"].pop("axialCompensation", None)
        key = (row["Task"], row["Seed"], row["Group"])
        if key in paired:
            assert stable(normal) == paired[key], "Unexpected paired change beyond axial permission"
        paired[key] = stable(normal)
        if row["Mode"] == "axial":
            assert options["axialCompensation"] == {"maximumDisplacementMillimeters": .25, "coordinateScaleMillimeters": .1, "elementIds": []}
        else:
            assert "axialCompensation" not in options
        seed = plan["seeds"][0]
        surfaces = {e[side + "SurfaceId"]: e[side] for e in seed["elements"] for side in ["front", "back"]}
        for candidate in state["candidates"]:
            topology = candidate["topology"]
            for field in ["reference", "stop", "image", "fields", "wavelengths"]:
                assert topology[field] == seed[field], (row["Id"], field)
            for element in topology["elements"]:
                for side in ["front", "back"]:
                    if element[side + "SurfaceId"] in surfaces:
                        original = surfaces[element[side + "SurfaceId"]]
                        assert element[side]["clearSemiDiameterMillimeters"] == original["clearSemiDiameterMillimeters"]
                        assert element[side]["mechanicalSemiDiameterMillimeters"] == original["mechanicalSemiDiameterMillimeters"]
            assert candidate["assessment"]["completed"]
            assert candidate["assessment"]["targets"] == plan["targets"]
            assert all(field["attemptedRays"] == 960 and field["checkAttemptedRays"] == 99 for field in candidate["assessment"]["fields"])
        if row["ReducedSuccess"]:
            exports = read(HERE / batch / row["Id"] / "export-checks.json")
            assert len(exports) == 1
            for exported in exports:
                recorded = next(candidate for candidate in state["acceptedArchive"] if candidate["id"] == exported["Id"])
                assessment = exported["Assessment"]
                assert assessment["MeetsTargets"] and assessment["Completed"] and not assessment["Violations"]
                assert assessment["ProtocolFingerprint"] == recorded["assessment"]["protocolFingerprint"]
                assert len(assessment["Fields"]) == len(recorded["assessment"]["fields"]) == 35
                assert all(field["ValidRays"] == field["AttemptedRays"] == 960 and field["CheckValidRays"] == field["CheckAttemptedRays"] == 99 for field in assessment["Fields"])
        trial_bounds = {}
        for event in state["events"]:
            if event.get("optimization"):
                axes = [request for request in event["optimization"]["variables"] if request["kind"] == "axialPosition"]
                if row["Mode"] == "curvature":
                    assert not axes
                bounds = {request["surfaceId"]: request for request in axes}
                if event["trialId"] in trial_bounds:
                    assert bounds == trial_bounds[event["trialId"]], "Axial window recentered between stages"
                trial_bounds[event["trialId"]] = bounds
                frozen = event.get("gradualDeletion")
                if frozen:
                    assert frozen["originalElement"]["frontSurfaceId"] not in bounds
                    assert frozen["originalElement"]["backSurfaceId"] not in bounds
                for request in axes:
                    assert 0 < request["upperBound"] - request["lowerBound"] <= .50000000001
                    assert request["scale"] == .1
                    assert request["surfaceId"] not in [seed["reference"]["id"], seed["stop"]["id"], seed["image"]["id"]]
        rows.append({"Batch": batch, **row})

old = read(HERE / "runs/repr-new-wide4-1-42-C-curvature/run.autonomous.json")["checkpoint"]
fresh = read(HERE / "paired-runs/repr-new-wide4-1-42-C-curvature/run.autonomous.json")["checkpoint"]
assert stable([c["topology"] for c in old["candidates"]]) == stable([c["topology"] for c in fresh["candidates"]])
assert old["consumed"]["tracedRays"] == fresh["consumed"]["tracedRays"]
control = read(HERE / "final-integration-tests/autonomous-control.json")
accepted = control["full"]["AcceptedArchive"]
assert len(accepted) == 1 and accepted[0]["Assessment"]["MeetsTargets"]
assert control["NativeAssessment"]["MeetsTargets"]
assert control["full"]["Consumed"]["TracedRays"] == control["resumed"]["Consumed"]["TracedRays"] == 1_077_444
assert stable(accepted[0]["Topology"]) == stable(control["resumed"]["AcceptedArchive"][0]["Topology"])
audit = read(HERE / "precision-audit/precision-audit.json")
assert audit["Complete"] and audit["ActualDiagnosticRays"] <= 2_000_000
trx = ET.parse(HERE / "tests/focused.trx").getroot()
counters = next(item for item in trx.iter() if item.tag.endswith("Counters"))
assert counters.attrib["failed"] == "0" and int(counters.attrib["passed"]) >= 80
for build in ["build-default-app.log", "build-runner-verified.log"]:
    assert "0 个错误" in (HERE / build).read_text(encoding="utf-8-sig")
for formatted in ["format-app.log", "format-engine.log", "format-tests-final.log", "format-tools-final.log"]:
    assert not (HERE / formatted).read_text(encoding="utf-8-sig").strip()
assert not read(HERE / "documentation-audit.json")["Missing"]
result = {
    "Verified": True, "NewPrescriptionRuns": 32, "QualifiedFourControlRuns": 8,
    "OriginalSourceBytesUnchanged": True, "PairedSettingsAndOriginalGatesUnchanged": True,
    "StageWindowsAndWorkLedgerVerified": True, "NativeAndExactResumeVerified": True,
    "FunctionGradientRayHardCapsRespected": True, "AllWallTimesWithinLimit": not time_overruns,
    "CooperativeTimeLimitOverruns": time_overruns,
    "CpuProcessors": 2, "Priority": "BelowNormal", "TestsPassed": int(counters.attrib["passed"]),
    "DefaultAppAndRunnerBuildSucceeded": True, "ScopedWhitespaceChecksPassed": True, "DocumentationLinksVerified": True,
    "ActualSearchRays": sum(row["Consumed"]["TracedRays"] for row in rows),
    "FirstSummaryMetadataInterruptedSearchRays": old["consumed"]["tracedRays"],
    "IntegrationControlSearchRays": control["full"]["Consumed"]["TracedRays"],
    "IntegrationNativeAuditRays": control["AdditionalAuditRays"],
    "QualifiedControlNativeAuditRays": sum(row["ExportAuditRays"] for row in rows if row["Batch"] == "qualified-control"),
    "PrecisionDiagnosticRays": audit["ActualDiagnosticRays"],
    "PrecisionAvailableUniqueTopologies": sum(row.get("Available") is True for row in audit["Rows"]),
    "PrecisionUnavailableUniqueTopologies": sum(row.get("Available") is False and "TopologyFingerprint" in row for row in audit["Rows"]),
    "NewReducedSuccesses": sum(row["ReducedSuccess"] for row in rows if row["Batch"] == "paired-runs"),
    "QualifiedControlReducedSuccesses": sum(row["ReducedSuccess"] for row in rows if row["Batch"] == "qualified-control"),
    "Statuses": dict(Counter(row["Status"] for row in rows)), "Runs": rows,
}
(HERE / "verification.json").write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k != "Runs"}, ensure_ascii=False, indent=2))
