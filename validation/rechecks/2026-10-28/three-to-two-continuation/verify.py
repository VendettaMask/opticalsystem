"""Check frozen C# evidence; no optical computation or training in Python."""
import copy
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def seconds(value):
    h, m, s = value.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def metrics(a):
    f = a["Fields"]
    return {"Qualified": a["MeetsTargets"], "FocalMillimeters": a["EffectiveFocalLengthMillimeters"], "FNumber": a["FNumber"],
            "RmsUm": max(x["RmsRadiusMillimeters"] for x in f) * 1000,
            "MaximumUm": max(x["MaximumRadiusMillimeters"] for x in f) * 1000,
            "DistortionPercent": max(abs(x["DistortionFraction"]) for x in f) * 100,
            "LostIntegrationRays": sum(x["AttemptedRays"] - x["ValidRays"] for x in f),
            "LostCheckRays": sum(x["CheckAttemptedRays"] - x["CheckValidRays"] for x in f)}


cells, summaries, native_cost = [], {}, 0
for batch, count, ordinal in [("paired-runs", 8, 0), ("selected-runs", 4, 1)]:
    root = HERE / batch
    summary = read(root / "summary.json")
    protocol = read(root / "frozen-protocol.json")
    assert summary["CpuProcessors"] == 2 and summary["Priority"] == "BelowNormal"
    assert len(summary["Runs"]) == count and len(summary["Binaries"]) == 4
    assert summary["ProtocolSha256"] == sha(root / "frozen-protocol.json")
    assert protocol.get("InitialDeletionOrdinal", 0) == ordinal
    assert sha(REPO / protocol["SourcePlan"]) == protocol["SourcePlanSha256"]
    assert sha(REPO / protocol["SourceNative"]) == protocol["SourceNativeSha256"]
    source = read(root / "source.topology-plan.json")["plan"]
    source_options = source["options"]
    prep = read(root / "preparation.json")
    assert prep["InitialAssessment"]["MeetsTargets"]
    summaries[batch] = {"Runs": count, "PreparationRays": prep["AdditionalPreparationRays"], "Source": metrics(prep["InitialAssessment"])}
    for row in summary["Runs"]:
        assert row["Status"] == "BudgetExhausted" and not row["ReducedSuccess"]
        directory = root / row["Id"]
        state = read(directory / "run.autonomous.json")["checkpoint"]
        plan, work = state["plan"], state["consumed"]
        options = plan["options"]
        assert plan["seeds"] == source["seeds"] and plan["targets"] == source["targets"] and plan["constraints"] == source["constraints"]
        assert (options["maximumTrials"], options["maximumFunctionEvaluations"], options["maximumGradientEvaluations"], options["maximumTracedRays"],
                options["maximumStageTracedRays"], options["finalValidationRayReserve"]) == (6, 600, 100, 2000000, 400000, 800000)
        assert (options["local"]["maximumUpdates"], options["full"]["maximumUpdates"]) == (4, 99)
        assert (options["recovery"]["maximumFunctionEvaluations"], options["recovery"]["maximumAcceptedSteps"]) == (150, 30)
        assert (options["axialCompensation"]["maximumDisplacementMillimeters"], options["expansion"]["maximumAbsoluteConic"]) == (.25, 2)
        normalized = copy.deepcopy(plan)
        normalized["options"]["seed"] = source_options["seed"]
        normalized["options"]["expansion"]["enableGradualDeletion"] = source_options["expansion"]["enableGradualDeletion"]
        normalized["options"]["recovery"].pop("useGeometryConstraintTangents", None)
        assert normalized == source
        assert bool(options["recovery"].get("useGeometryConstraintTangents")) == (row["Mode"] == "geometry")
        assert work["functionEvaluations"] <= 600 and work["gradientEvaluations"] <= 100 and work["tracedRays"] <= 2000000
        assert seconds(work["elapsed"]) <= seconds(options["timeLimit"]) == 60
        for key, value in work.items():
            if key != "elapsed":
                assert value == sum(e["work"][key] for e in state["events"])
        assert state["trialCursor"] == (2 if ordinal == 0 else 3) and len(state["candidates"]) == 2
        assert state["pending"]["phase"] == "recovery" and state.get("reservation") is None
        recovery = [e for e in state["events"] if e.get("recovery")]
        assert len(recovery) == 1 and recovery[0]["outcome"] == "Recovered"
        removed = source["seeds"][0]["elements"][ordinal]["id"]
        assert recovery[0]["change"]["removedElementIds"] == [removed]
        assert not any("gradual" in e["operation"].lower() for e in state["events"])
        if ordinal:
            explicit = read(directory / "explicit-start.json")
            assert explicit["InitialDeletionOrdinal"] == 1 and explicit["SourceElement"] == removed
            assert explicit["InitializationWork"]["ValidationEvaluations"] == 1
            assert explicit["InitializationWork"]["TracedRays"] == prep["AdditionalPreparationRays"]
        variables = None
        accepted = 0
        for event in state["events"]:
            if event["phase"] in ["local", "full"]:
                assert event["work"]["tracedRays"] <= 400000
            if event.get("optimization"):
                o = event["optimization"]
                accepted += o["acceptedUpdates"]
                if variables is None:
                    variables = o["variables"]
                assert variables == o["variables"]  # no stage reanchoring
                assert len(variables) == 12  # four curvatures, four positions, four conics
        assert accepted == (0 if ordinal == 0 else 4)
        for candidate in state["candidates"]:
            for key in ["stop", "image", "reference", "fields", "wavelengths", "glassCatalogs", "conjugate"]:
                assert candidate["topology"][key] == source["seeds"][0][key]
            by_id = {e["id"]: e for e in source["seeds"][0]["elements"]}
            for e in candidate["topology"]["elements"]:
                initial = by_id[e["id"]]
                assert e["material"] == initial["material"]
                for side in ["front", "back"]:
                    for key in ["clearSemiDiameterMillimeters", "mechanicalSemiDiameterMillimeters"]:
                        assert e[side][key] == initial[side][key]
                    assert not e[side]["profile"]["terms"] and abs(e[side]["profile"]["conic"]) <= 2
        two = next(c for c in row["Candidates"] if c["Elements"] == 2)
        a = two["Assessment"]
        assert a["Completed"] and not a["MeetsTargets"] and len(a["Fields"]) == 35
        assert all(f["AttemptedRays"] == f["ValidRays"] == 960 and f["CheckAttemptedRays"] == f["CheckValidRays"] == 99 for f in a["Fields"])
        assert all("rms" in v["Code"] or "maximum" in v["Code"] or "distortion" in v["Code"] for v in a["Violations"])
        assert (directory / (two["Id"] + "-unqualified.staropt")).is_file()
        assert len(state["acceptedArchive"]) == 1 and len(state["acceptedArchive"][0]["topology"]["elements"]) == 3
        cells.append({"Batch": batch, "Id": row["Id"], "Removed": removed, "AcceptedCompensationUpdates": accepted,
                      "Work": row["Consumed"], "Physical": metrics(a)})
for audit_folder, expected in [("deletion-audit", 8), ("selected-native-audit", 4)]:
    audits = read(HERE / audit_folder / "native-audits.json")
    assert len(audits) == expected
    checked = set()
    for a in audits:
        if "Reuses" in a:
            assert a["Reuses"] in checked and a["AdditionalAuditRays"] == 0
        else:
            assert a["Matches"] and not a["Assessment"]["MeetsTargets"]
            checked.add(a["Id"])
        native_cost += a["AdditionalAuditRays"]
diagnosis = read(HERE / "deletion-audit/deletion-diagnosis.json")
assert len(diagnosis) == 6
assert all(r["Recovery"]["Termination"] == ("rankDeficient" if r["Removed"] == "element-003" else "recovered") for r in diagnosis)
filter_audit = read(HERE / "filter-audit/filter-audit.json")
assert len(filter_audit["Rows"]) == 8 and all(r["OverallDescent"] and not r["AcceptedV4"] and r["Blockers"] for r in filter_audit["Rows"])
assert filter_audit["ActualDiagnosticRays"] == 144056
test = ET.parse(HERE / "tests/recovery.trx").getroot().find(".//{*}Counters").attrib
assert test["total"] == test["passed"] == "7" and test["failed"] == "0"
report = {"Verified": True, "QualifiedTwoElementRuns": 0, "QualifiedSourceRetained": True, "Cells": cells,
          "Batches": summaries, "SearchRaysAcrossSeparateRuns": sum(c["Work"]["TracedRays"] for c in cells),
          "AdditionalDeletionDiagnosticRays": sum(r["AdditionalPhysicalRays"] for r in diagnosis),
          "AdditionalDeletionRecoveryFunctions": sum(r["AdditionalRecoveryFunctions"] for r in diagnosis),
          "AdditionalFilterDiagnosticRays": filter_audit["ActualDiagnosticRays"], "AdditionalNativeAuditRays": native_cost,
          "FocusedTestsPassed": int(test["passed"]),
          "Limits": ["One source prescription; two search seeds do not create independent designs.",
                     "D configurations did not dispatch a gradual stage; direct recovery succeeded first.",
                     "Second deletion was explicitly prioritized after diagnosis; default automatic coverage is not improved.",
                     "All evaluations and independent audits are charged separately; no pooled successful budget.",
                     "No physical infeasibility or global-minimum element count is proven."]}
(HERE / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k not in ["Cells", "Limits"]}, ensure_ascii=False, indent=2))
