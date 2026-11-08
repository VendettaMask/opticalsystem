import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


parser = argparse.ArgumentParser()
parser.add_argument("frozen_root", type=Path)
parser.add_argument("formal_root", type=Path)
parser.add_argument("independent_root", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()


def digest(payload):
    return hashlib.sha256(payload).hexdigest()


def read(path):
    payload = path.read_bytes()
    return json.loads(payload), digest(payload)


original, original_hash = read(args.frozen_root / "analyses/2161/request.json")
refined, request_hash = read(args.formal_root / "2161-axis-final-request.json")
result_dir = args.formal_root / "2161-axis-final"
result, result_hash = read(result_dir / "data.json")
provenance, provenance_hash = read(result_dir / "provenance.json")
baseline, _ = read(args.frozen_root / "rows.json")
assert baseline[2160]["index"] == 2161
tolerance = baseline[2160]["settings"]["quantities"]["Irradiance"]
assert (tolerance["absolute"], tolerance["relative"], tolerance["nrmse"], tolerance["minimumCoverage"]) == (
    1e-5, .002, .003, .95)

request = refined["request"]
options = request["workbenchSettings"].copy()
assert options.pop("FourierSamplingMode") == "Adaptive"
assert options.pop("FourierMaximumSampling") == "6144"
assert options.pop("FourierRelativeAccuracy") == "0.001"
assert dict(request, workbenchSettings=options) == original["request"]
assert request["configuration"] == 1 and request["pupilSampling"] == 32
assert options["Row"] == "中心" and options["GraphScaleMicrometers"] == "0" and options["Type"] == "X-线性"
snapshot_hash = digest(Path(refined["snapshotPath"]).read_bytes())
assert snapshot_hash == digest(Path(original["snapshotPath"]).read_bytes()) == provenance["snapshotSha256"]
assert provenance["executorId"] == "Application.WorkbenchRuntime.BuildAnalysisData/v1"
assert result["name"] == "FFT PSF Cross Section"
certificate = result["values"]["FourierSamplingValidation"]
assert certificate["protocol"] == "FourierPhysicalDomainConvergence/v3" and certificate["mode"] == "Adaptive"
assert certificate["requestedPupilSampling"] == 32 and certificate["maximumPupilSampling"] == 6144
assert certificate["relativeRmsAccuracy"] == .001 and certificate["absoluteAccuracy"] == 1e-7
assert (certificate["evaluatedNominalPupilCells"] + certificate["evaluatedStreamedPupilNodes"]
        <= certificate["maximumNominalPupilCells"] == 100_000_000)
receipt = {
    "frozenIndex": 2161, "configuration": 1, "outcome": result["outcome"], "reason": result["outcomeReason"],
    "originalNativeProfileUsable": False, "originalTolerance": tolerance,
    "frozenRequestSha256": original_hash, "formalRequestSha256": request_hash, "formalDataSha256": result_hash,
    "formalProvenanceSha256": provenance_hash, "provenance": provenance, "certificate": certificate,
}

if result["outcome"] == "Success":
    assert certificate["validated"]
    streamed = [s for s in certificate["steps"] if s.get("usesStreamedPhysicalPupil")]
    accepted = streamed[-2:]
    assert len(accepted) == 2
    assert accepted[0]["pupilSampling"] < accepted[1]["pupilSampling"] == certificate["returnedPupilSampling"]
    assert all(s["phaseResolved"] and 0 < s["maximumPupilPhaseStepWaves"] <= .5 for s in accepted)
    assert (accepted[-1]["relativeRmsChange"] <= certificate["relativeRmsAccuracy"]
            or accepted[-1]["maximumAbsoluteChange"] <= certificate["absoluteAccuracy"])

    values = result["values"]
    assert values["AxisFourierReadout"] and values["GraphScaleMicrometers"] == 0
    assert values["Row"] == "中心" and values["ProfileType"] == "X-线性" and values["ProfileIdealPeak"] == 1
    assert not values["Normalized"] and not values["UsePolarization"] and not values["UseRayAiming"]
    assert values["WavelengthNumber"] == values["FieldNumber"] == 1
    series, = result["plotSeries"]
    assert (series["xQuantity"], series["xUnit"], series["yQuantity"], series["yUnit"]) == (
        "ImageHeight", "Micrometer", "Irradiance", "Dimensionless")
    x = np.array([p["x"] for p in series["points"]])
    y = np.array([p["y"] for p in series["points"]])
    assert x.size == y.size == 201 and np.all(np.diff(x) > 0) and np.all(y >= 0) and np.isfinite(y).all()

    independent, independent_receipt_hash = read(args.independent_root / "2161-independent-receipt.json")
    assert independent["snapshotSha256"] == snapshot_hash
    assert independent["formal"]["dataSha256"] == result_hash
    assert independent["betweenIndependentQuadratures"]["peakNormalizedRms"] < 1e-8
    path = args.independent_root / "2161-independent.npz"
    assert digest(path.read_bytes()) == independent["independentPayloadSha256"]
    arrays = np.load(path)
    assert np.array_equal(x, arrays["axis"])
    half = independent["automaticHalfRangeMicrometers"]
    axis_error = float(np.max(abs(x - (-half + 2*half*np.arange(201)/200))))
    assert axis_error <= 1e-10
    ref = arrays["second"]
    residual = abs(y-ref)
    cross = {
        "coordinates": int(x.size), "coverage": 1,
        "maximumAbsolute": float(residual.max()),
        "peakNormalizedRms": float(np.sqrt(np.mean(residual**2)) / ref.max()),
        "pointwisePass": bool((residual <= tolerance["absolute"] + tolerance["relative"]*abs(ref)).all()),
    }
    cross["nrmsePass"] = cross["peakNormalizedRms"] <= tolerance["nrmse"]
    receipt["status"] = ("FormalAdaptiveAndIndependentScalarFftPass"
                         if cross["pointwisePass"] or cross["nrmsePass"] else "FormalIndependentCrossDifference")
    receipt["physicalAxis"] = {"quantity": "ImageHeight", "unit": "Micrometer", "points": int(x.size),
                               "range": [float(x[0]), float(x[-1])], "independentAxisMaximumError": axis_error}
    receipt["versusIndependentScalarFft"] = cross
    receipt["independentFormalAxisReceiptSha256"] = independent_receipt_hash
    receipt["independentFormalAxisPayloadSha256"] = independent["independentPayloadSha256"]
    receipt["diagnostics"] = values["StreamedFourierPupil"]
else:
    assert not certificate["validated"]
    receipt["status"] = "FormalAdaptiveUnavailable"

args.output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
