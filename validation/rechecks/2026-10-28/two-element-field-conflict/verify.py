"""Offline evidence checks only. Optical values and derivatives come from shared C# Core."""
import copy
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def normalize(value):
    if isinstance(value, dict):
        return {k[:1].lower() + k[1:]: normalize(v) for k, v in value.items()}
    if isinstance(value, list):
        return [normalize(v) for v in value]
    return value


def metrics(a):
    fields = a["Fields"]
    return {"Qualified": a["MeetsTargets"], "RmsUm": max(f["RmsRadiusMillimeters"] for f in fields) * 1000,
            "MaximumUm": max(f["MaximumRadiusMillimeters"] for f in fields) * 1000,
            "DistortionPercent": max(abs(f["DistortionFraction"]) for f in fields) * 100,
            "FocalMillimeters": a["EffectiveFocalLengthMillimeters"], "FNumber": a["FNumber"],
            "LostIntegrationRays": sum(f["AttemptedRays"] - f["ValidRays"] for f in fields),
            "LostCheckRays": sum(f["CheckAttemptedRays"] - f["CheckValidRays"] for f in fields)}


summary = read(HERE / "paired-runs/summary.json")
protocol_file = HERE / "paired-runs/frozen-protocol.json"
protocol = read(protocol_file)
assert hashlib.sha256(protocol_file.read_bytes()).hexdigest() == summary["ProtocolSha256"]
assert hashlib.sha256((REPO / protocol["Checkpoint"]).read_bytes()).hexdigest() == protocol["Sha256"]
state = read(REPO / protocol["Checkpoint"])["checkpoint"]
candidate = next(c for c in state["candidates"] if len(c["topology"]["elements"]) == 2)
variables = [e["optimization"]["variables"] for e in state["events"] if e.get("optimization")][-1]
assert len(variables) == 12 and protocol["Versions"] == [4, 5]
assert protocol["Budget"] == {"MaximumFunctionEvaluations": 600, "MaximumGradientEvaluations": 100, "MaximumTracedRays": 2000000,
                              "MaximumStageTracedRays": 400000, "FinalValidationRayReserve": 800000, "TimeLimitSeconds": 60}
assert summary["CpuProcessors"] == 2 and summary["Priority"] == "BelowNormal" and len(summary["Binaries"]) == 4
assert [r["Version"] for r in summary["Runs"]] == [4, 5]
inputs, outputs = [], []
for row in summary["Runs"]:
    directory = HERE / "paired-runs" / f"v{row['Version']}"
    inputs.append(read(directory / "input.json"))
    input = normalize(inputs[-1])
    assert input["source"] == candidate["topology"] and input["variables"] == variables
    assert input["targets"] == state["plan"]["targets"] and input["constraints"] == state["plan"]["constraints"]
    assert row["ActualRays"] == sum(row[key] for key in ["InitialRays", "LocalRays", "FullRays", "FinalRays"])
    assert row["LocalRays"] <= 400000 and row["FullRays"] == 400000 and row["InitialRays"] <= 800000 and row["FinalRays"] <= 800000
    assert row["ActualRays"] <= 2000000 and row["FunctionEvaluations"] <= 600 and row["GradientEvaluations"] <= 100 and row["ActualSeconds"] <= 60
    fcount = gcount = accepted = 0
    for phase in ["local", "full"]:
        opt = read(directory / (phase + ".json"))
        assert normalize(opt["Variables"]) == variables
        fcount += opt["Work"]["FunctionEvaluations"]; gcount += opt["Work"]["GradientEvaluations"]; accepted += opt["AcceptedUpdates"]
        coordinates = opt["Continuation"]["Adam"]["Coordinates"]
        assert all(v["lowerBound"] / v["scale"] <= q <= v["upperBound"] / v["scale"] for q, v in zip(coordinates, variables))
        for name in ["Reference", "Stop", "Image", "Fields", "Wavelengths", "GlassCatalogs", "Conjugate"]:
            assert normalize({name: opt["Topology"][name]})[name[:1].lower() + name[1:]] == candidate["topology"][name[:1].lower() + name[1:]]
        initial_elements = {e["id"]: e for e in candidate["topology"]["elements"]}
        for e in normalize(opt["Topology"])["elements"]:
            assert e["material"] == initial_elements[e["id"]]["material"]
            for side in ["front", "back"]:
                for key in ["clearSemiDiameterMillimeters", "mechanicalSemiDiameterMillimeters"]:
                    assert e[side][key] == initial_elements[e["id"]][side][key]
                assert not e[side]["profile"]["terms"] and e[side]["profile"]["maximumEvenPower"] == 0
    assert fcount + 2 == row["FunctionEvaluations"] and gcount == row["GradientEvaluations"] and accepted == row["AcceptedUpdates"]
    for a in [row["Initial"], row["Final"]]:
        assert a["Completed"] and not a["MeetsTargets"] and len(a["Fields"]) == 35
        assert normalize(a["Targets"]) == input["targets"]
        assert all(f["ValidRays"] == f["AttemptedRays"] == 960 and f["CheckValidRays"] == f["CheckAttemptedRays"] == 99 for f in a["Fields"])
        assert all("rms" in v["Code"] or "maximum" in v["Code"] for v in a["Violations"])
    assert (directory / "unqualified.staropt").is_file()
    outputs.append({"Version": row["Version"], "Metrics": metrics(row["Final"]), "Rays": row["ActualRays"],
                    "Functions": row["FunctionEvaluations"], "Gradients": row["GradientEvaluations"], "AcceptedUpdates": row["AcceptedUpdates"]})
a, b = copy.deepcopy(inputs[0]), copy.deepcopy(inputs[1])
for x in [a, b]:
    for phase in ["Local", "Full"]:
        x[phase].pop("TransmissionPolicyVersion")
assert a == b
assert outputs[0]["AcceptedUpdates"] == 0 and outputs[1]["AcceptedUpdates"] == 14
old, new = outputs[0]["Metrics"], outputs[1]["Metrics"]
assert new["RmsUm"] < old["RmsUm"] and new["MaximumUm"] < old["MaximumUm"]
diagnosis = read(HERE / "diagnosis/diagnosis.json")
assert len(diagnosis["Rows"]) == 8
assert all(not r["Accepted"] and r["AfterOther"] > r["BeforeOther"] for r in diagnosis["Rows"] if r["Version"] == 4)
assert all(r["Accepted"] and r["AfterOther"] < r["BeforeOther"] for r in diagnosis["Rows"] if r["Version"] == 5)
clipped = diagnosis["ClippedFallbackVariables"]
assert len(clipped) == 1 and clipped[0]["Kind"] == "axialPosition" and clipped[0]["SurfaceId"] == "element-001:front"
assert clipped[0]["RawProposal"] < clipped[0]["LowerCoordinate"] == 0
audits = read(HERE / "native-audit/native-audits.json")
assert len(audits) == 2 and all(a["Matches"] and not a["Assessment"]["MeetsTargets"] for a in audits)
for a in audits:
    assert metrics(a["Assessment"]) == next(o["Metrics"] for o in outputs if o["Version"] == a["Version"]) or all(
        abs(metrics(a["Assessment"])[key] - next(o["Metrics"][key] for o in outputs if o["Version"] == a["Version"])) < 1e-9
        for key in ["RmsUm", "MaximumUm", "DistortionPercent", "FocalMillimeters", "FNumber"])
gradients = read(HERE / "final-test-evidence/merit-gradients.json")
assert len(gradients["Errors"]) == 12 and max(gradients["Errors"]) < 2e-5
test_count = 0
test_names = set()
for file, count in [("focused.trx", 66), ("anchor-mismatch.trx", 1)]:
    test_xml = ET.parse(HERE / "tests" / file).getroot()
    tests = test_xml.find(".//{*}Counters").attrib
    assert tests["passed"] == tests["total"] == str(count) and tests["failed"] == "0"
    test_names.update(x.attrib["testName"] for x in test_xml.findall(".//{*}UnitTestResult"))
    test_count += count
assert len(test_names) == test_count == 67
# Compare linear objective slopes using the already recorded Core derivatives,
# not another optical evaluator. The two active image rows are RMS then maximum.
before = diagnosis["Before"]
active_rows = [t for t in before["ConstraintTangents"] if t["Row"] > 1 and t["Value"] > .01]
assert len(active_rows) == 2
assert abs(active_rows[0]["Value"] - (old["RmsUm"] / 50 - 1)) < 1e-6
assert abs(active_rows[1]["Value"] - (old["MaximumUm"] / 150 - 1)) < 1e-6
q = [e["optimization"] for e in state["events"] if e.get("optimization")][-1]["continuation"]["adam"]["coordinates"]
clipped_q = diagnosis["Rows"][0]["Coordinates"]
raw_q = clipped_q.copy()
clipped_index = next(i for i, v in enumerate(variables) if v["surfaceId"] == clipped[0]["SurfaceId"] and v["kind"] == clipped[0]["Kind"])
raw_q[clipped_index] = clipped[0]["RawProposal"]
merit_gradient = [2 * (active_rows[0]["Value"] + .02) * a + 2 * active_rows[1]["Value"] * b
                  for a, b in zip(active_rows[0]["Derivatives"], active_rows[1]["Derivatives"])]
raw_gradient = [sum(2 * before["Residuals"][t["Row"]] * before["Jacobian"][t["Row"]][i] for t in before["ConstraintTangents"]) for i in range(12)]
def slope(gradient, proposal):
    return sum(g * (x - initial) for g, x, initial in zip(gradient, proposal, q))
slopes = {"RawConstraintObjectiveBeforeClip": slope(raw_gradient, raw_q), "GuardedMeritBeforeClip": slope(merit_gradient, raw_q),
          "GuardedMeritAfterClip": slope(merit_gradient, clipped_q)}
assert slopes["RawConstraintObjectiveBeforeClip"] < 0 < slopes["GuardedMeritBeforeClip"] and slopes["GuardedMeritAfterClip"] > 0
result = {"Verified": True, "Runs": outputs, "RmsImprovementPercent": (1 - new["RmsUm"] / old["RmsUm"]) * 100,
          "MaximumImprovementPercent": (1 - new["MaximumUm"] / old["MaximumUm"]) * 100,
          "ActualSearchRaysAcrossTwoSeparateRuns": sum(o["Rays"] for o in outputs),
          "AdditionalV4FilterDiagnosticRays": read(HERE / "v4-filter/filter-audit.json")["ActualDiagnosticRays"],
          "AdditionalDirectionDiagnosticRays": diagnosis["ActualDiagnosticRays"],
          "AdditionalNativeAuditRays": sum(a["AdditionalAuditRays"] for a in audits),
          "MeritDerivativeMaximumAbsoluteError": max(gradients["Errors"]), "MeritDerivativeDiagnosticRaysInTests": gradients["Rays"],
          "FocusedTestsPassed": test_count, "FallbackDirectionalSlopes": slopes,
          "Limits": ["Fresh compensation of one fixed failed two-element endpoint, not a new successful three-to-two run.",
                     "All original twelve absolute bounds were reused without axial recentering.",
                     "v5 preserves v4 nonlinear acceptance and independent physical gates; defaults and old versions are not upgraded.",
                     "Both endpoints still fail original spot gates; no physical impossibility claim.",
                     "Shared-host wall time is not an isolated speed benchmark; diagnostics and audits are separate budgets."]}
(HERE / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
