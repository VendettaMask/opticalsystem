"""Offline evidence checks only; all optical evaluation is performed by shared C# Core."""
import copy
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def seconds(value):
    hours, minutes, secs = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(secs)


def metrics(assessment):
    fields = assessment["Fields"]
    return {
        "Qualified": assessment["MeetsTargets"],
        "FocalMillimeters": assessment["EffectiveFocalLengthMillimeters"],
        "FNumber": assessment["FNumber"],
        "RmsMicrometers": max(f["RmsRadiusMillimeters"] for f in fields) * 1000,
        "MaximumMicrometers": max(f["MaximumRadiusMillimeters"] for f in fields) * 1000,
        "DistortionPercent": max(abs(f["DistortionFraction"]) for f in fields) * 100,
        "LostIntegrationRays": sum(f["AttemptedRays"] - f["ValidRays"] for f in fields),
        "LostCheckRays": sum(f["CheckAttemptedRays"] - f["CheckValidRays"] for f in fields),
    }


base = read(HERE / "prepared/baseline.topology-plan.json")["plan"]
preparation = read(HERE / "prepared/preparation.json")
assert digest(preparation["SourceNative"]) == preparation["NativeSha256"]
assert preparation["Assessment"]["MeetsTargets"]
assert preparation["AdditionalPreparationRays"] == 446064
seed = base["seeds"][0]
source_elements = {e["id"]: e for e in seed["elements"]}
all_rows, all_states, report_rows = [], [], []
successful = []
for batch, count in [("paired-runs", 16), ("pupil-runs", 4), ("conic-runs", 4), ("allocation-runs", 4)]:
    summary = read(HERE / batch / "summary.json")
    protocol = read(HERE / batch / "frozen-protocol.json")
    assert summary["ProtocolSha256"] == digest(HERE / batch / "frozen-protocol.json")
    assert digest(REPO / protocol["SourcePlan"]) == protocol["SourcePlanSha256"]
    assert digest(REPO / protocol["SourceNative"]) == protocol["SourceNativeSha256"]
    assert len(summary["Runs"]) == count and summary["CpuProcessors"] == 2 and summary["Priority"] == "BelowNormal"
    assert len(summary["Binaries"]) == 4 and all(len(v) == 64 for v in summary["Binaries"].values())
    for row in summary["Runs"]:
        assert row["Status"] in ["Completed", "BudgetExhausted"]
        directory = HERE / batch / row["Id"]
        state = read(directory / "run.autonomous.json")["checkpoint"]
        plan, consumed = state["plan"], state["consumed"]
        options = plan["options"]
        assert plan["seeds"] == base["seeds"]  # search seeds do not perturb the prescription
        assert plan["targets"] == base["targets"] and plan["constraints"] == base["constraints"]
        assert (options["maximumTrials"], options["maximumFunctionEvaluations"], options["maximumGradientEvaluations"],
                options["maximumTracedRays"], options["maximumStageTracedRays"], options["finalValidationRayReserve"]) == (6, 600, 100, 2000000, 400000, 800000)
        assert seconds(options["timeLimit"]) == 60
        assert consumed["functionEvaluations"] <= 600 and consumed["gradientEvaluations"] <= 100 and consumed["tracedRays"] <= 2000000
        for key, value in consumed.items():
            if key != "elapsed":
                assert value == sum(e["work"][key] for e in state["events"]), (row["Id"], key)
        mode = row["Mode"]
        assert bool(options.get("protectPostRecoveryRayBudget")) == (mode != "control")
        assert bool(options.get("useAxialVariablesInRecovery")) == (mode in ["axial", "pupil", "conic", "allocated"])
        assert bool(options.get("compensateConic")) == (mode in ["conic", "allocated"])
        assert bool(options["recovery"].get("useAutomaticMatrixDerivatives")) == (mode in ["automatic", "axial", "pupil", "conic", "allocated"])
        assert bool(options["recovery"].get("restorePupilMagnification")) == (mode in ["pupil", "conic", "allocated"])
        normalized = copy.deepcopy(plan)
        normal = normalized["options"]
        for key in ["protectPostRecoveryRayBudget", "useAxialVariablesInRecovery", "compensateConic"]:
            normal.pop(key, None)
        for key in ["useAutomaticMatrixDerivatives", "restorePupilMagnification"]:
            normal["recovery"].pop(key, None)
        assert normal["local"] == base["options"]["local"]
        expected_full = copy.deepcopy(base["options"]["full"])
        if mode == "allocated":
            expected_full.update(maximumUpdates=99, maximumFunctionEvaluations=100, maximumGradientEvaluations=100)
        assert normal["full"] == expected_full
        normal["full"] = base["options"]["full"]
        normal["seed"] = 42
        normal["expansion"]["enableGradualDeletion"] = False
        assert normalized == base, row["Id"]
        assert any(c["id"] == "seed-0" and c["assessment"]["meetsTargets"] for c in state["acceptedArchive"])
        first_variables = {}
        for event in state["events"]:
            if event["phase"] in ["local", "full"]:
                assert event["work"]["tracedRays"] <= 400000
            opt = event.get("optimization")
            if opt:
                variables = opt["variables"]
                first_variables.setdefault(event["trialId"], variables)
                assert variables == first_variables[event["trialId"]], "stage reanchoring"
                for v in variables:
                    if v["kind"] == "conic":
                        assert mode in ["conic", "allocated"] and (v["lowerBound"], v["upperBound"]) == (-2, 2)
                    elif v["kind"] == "curvature":
                        assert abs(v["lowerBound"] + options["curvatureBoundPerMillimeter"]) < 1e-12
                        assert v["upperBound"] == options["curvatureBoundPerMillimeter"]
                    else:
                        assert v["kind"] == "axialPosition" and v["surfaceId"] not in ["reference", "stop", "image"]
                        original = source_elements[v["surfaceId"].split(":")[0]]
                        position = original["frontZMillimeters"] + (original["thicknessMillimeters"] if v["surfaceId"].endswith(":back") else 0)
                        assert v["lowerBound"] >= position - .25 - 1e-12 and v["upperBound"] <= position + .25 + 1e-12
            if event["outcome"] == "StageValidationDeferred":
                assert event["work"]["tracedRays"] == 0
        for candidate in state["candidates"]:
            topology = candidate["topology"]
            for key in ["reference", "stop", "image", "fields", "wavelengths", "glassCatalogs", "conjugate", "temperatureCelsius", "pressureAtmospheres"]:
                assert topology[key] == seed[key]
            for element in topology["elements"]:
                original = source_elements[element["id"]]
                if not element["transition"]:
                    assert element["material"] == original["material"]
                for side in ["front", "back"]:
                    for key in ["clearSemiDiameterMillimeters", "mechanicalSemiDiameterMillimeters"]:
                        assert element[side][key] == original[side][key]
                    profile = element[side]["profile"]
                    assert not profile["terms"] and profile["maximumEvenPower"] == 0
                    assert abs(profile["conic"]) <= 2
                    if mode not in ["conic", "allocated"]:
                        assert profile["conic"] == original[side]["profile"]["conic"]
            if candidate["assessment"]["meetsTargets"]:
                assert candidate["assessment"]["completed"] and not candidate["assessment"]["violations"]
            if len(topology["elements"]) == 3 and candidate["assessment"]["meetsTargets"]:
                assert mode == "allocated" and row["Group"] == "C"
                assert (directory / (candidate["id"] + ".staropt")).is_file()
                successful.append(row["Id"])
        assert row["ReducedSuccess"] == (row["Id"] in successful)
        assert read(directory / "task.topology-plan.json")["plan"] == plan
        reduced = [c for c in row["Archive"] + row["FailedCandidates"] if c["Elements"] == 3 and c["Assessment"]["Completed"]]
        best = sorted(reduced, key=lambda c: (not c["Assessment"]["MeetsTargets"], max(f["RmsRadiusMillimeters"] for f in c["Assessment"]["Fields"])))
        report_rows.append({"Batch": batch, "Id": row["Id"], "Status": row["Status"], "Work": row["Consumed"],
                            "ReducedSuccess": row["ReducedSuccess"], "ThreeElement": metrics(best[0]["Assessment"]) if best else None})
        all_rows.append(row)
        all_states.append(state)
assert len(all_rows) == 28 and len(successful) == 2

audits = read(HERE / "native-audit/native-audits.json")
resolved = {}
for audit in audits:
    if "Reuses" in audit:
        resolved[audit["Id"]] = resolved[audit["Reuses"]]
        assert audit["AdditionalAuditRays"] == 0
        continue
    assert audit["Available"] and audit["MatchesRecordedPhysicalEndpoint"]
    assessment = audit["Assessment"]
    assert digest(audit["NativeFile"])
    assert assessment["Targets"] == preparation["Assessment"]["Targets"]
    assert assessment["ProtocolFingerprint"] == preparation["Assessment"]["ProtocolFingerprint"]
    assert len(assessment["Fields"]) == 35
    assert all(f["AttemptedRays"] == 960 and f["CheckAttemptedRays"] == 99 for f in assessment["Fields"])
    resolved[audit["Id"]] = audit
qualified = resolved[successful[0]]["Assessment"]
assert qualified["MeetsTargets"] and not qualified["Violations"]
assert all(f["ValidRays"] == 960 and f["CheckValidRays"] == 99 for f in qualified["Fields"])
assert all(resolved[x]["Assessment"] == qualified for x in successful)

evidence = HERE / "final-test-evidence"
matrix_errors = []
for index in range(1, 5):
    gradients = read(evidence / f"matrix-element-{index:03}.json")
    assert len(gradients) == 12
    for g in gradients:
        assert len(g["Automatic"]) == len(g["FiniteDifferences"]) == 5
        matrix_errors.extend(abs(a-b) for a, b in zip(g["Automatic"], g["FiniteDifferences"]))
assert max(matrix_errors) < 1e-5
probes = read(evidence / "recovery-probes.json")
assert probes["old"]["Work"]["FunctionEvaluations"] == 80 and probes["old"]["Work"]["DifferenceEvaluations"] == 72
assert probes["automatic"]["Work"]["FunctionEvaluations"] == 8 and probes["automatic"]["Work"]["DifferenceEvaluations"] == 0
conic_gradients = read(evidence / "conic-gradients.json")
assert len(conic_gradients) == 6 and max(x["MaximumError"] for x in conic_gradients) < 1e-5
filter_audit = read(HERE / "filter-audit/filter-audit.json")
assert filter_audit["ActualDiagnosticRays"] == 136604
assert not filter_audit["Rows"][0]["AcceptedV4"] and filter_audit["Rows"][2]["AcceptedV4"]

tests = {}
for file, expected in [("focused.trx", 92), ("core-metric.trx", 2)]:
    root = ET.parse(HERE / "tests" / file).getroot()
    counters = root.find(".//{*}Counters").attrib
    assert int(counters["total"]) == expected and int(counters["passed"]) == expected
    assert counters["failed"] == "0"
    tests[file] = expected

winner_state = next(s for s in all_states if s["runId"] == successful[0])
winner = next(c for c in winner_state["candidates"] if len(c["topology"]["elements"]) == 3)
prescription = [{"Element": e["id"], "Material": e["material"], "FrontZMillimeters": e["frontZMillimeters"],
                 "ThicknessMillimeters": e["thicknessMillimeters"], "FrontCurvature": e["front"]["profile"]["curvaturePerMillimeter"],
                 "BackCurvature": e["back"]["profile"]["curvaturePerMillimeter"], "FrontConic": e["front"]["profile"]["conic"],
                 "BackConic": e["back"]["profile"]["conic"]} for e in winner["topology"]["elements"]]
result = {
    "Verified": True, "Runs": len(all_rows), "ReducedQualifiedRuns": successful,
    "SourceNativeSha256": preparation["NativeSha256"], "SourceFourElement": metrics(preparation["Assessment"]),
    "QualifiedThreeElement": metrics(qualified), "QualifiedPrescription": prescription,
    "TargetFocalRelativeErrorPercent": (qualified["EffectiveFocalLengthMillimeters"] / qualified["Targets"]["EffectiveFocalLengthMillimeters"] - 1) * 100,
    "TargetFNumberRelativeErrorPercent": (qualified["FNumber"] / qualified["Targets"]["FNumber"] - 1) * 100,
    "SearchRaysAcross28SeparateRuns": sum(r["Consumed"]["TracedRays"] for r in all_rows),
    "AdditionalPreparationRays": preparation["AdditionalPreparationRays"],
    "AdditionalFilterDiagnosticRays": filter_audit["ActualDiagnosticRays"],
    "AdditionalNativeAuditRays": sum(a["AdditionalAuditRays"] for a in audits),
    "WallTimeOverruns": [r["Id"] for r in all_rows if seconds(r["Consumed"]["Elapsed"]) > 60],
    "MatrixDerivativeColumnsChecked": 48, "MatrixAndPupilDerivativeValuesChecked": len(matrix_errors),
    "MaximumMatrixAndPupilDerivativeAbsoluteError": max(matrix_errors),
    "MaximumConicSpotGradientAbsoluteError": max(x["MaximumError"] for x in conic_gradients),
    "FocusedTests": tests, "Cells": report_rows,
    "Limitations": ["One unchanged source prescription; two search seeds are not independent lens designs.",
                    "Six conics add compensation freedom; original curvature and axial bounds remain fixed.",
                    "Allocated Full ceiling 99 differs from old 8 updates, within original stage/global quotas.",
                    "First-order rank rejection is not a proof of physical infeasibility.",
                    "Timing on a shared computer is not an isolated speed benchmark.",
                    "Preparation, separate experiments, diagnostics and native audits are not one pooled successful-run budget."]}
(HERE / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k not in ["Cells", "QualifiedPrescription", "Limitations"]}, ensure_ascii=False, indent=2))
