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
original_rows = json.loads((args.frozen_root / "rows.json").read_bytes())
geometry = json.loads((args.independent_root / "reference-receipt.json").read_bytes())


def digest(payload):
    return hashlib.sha256(payload).hexdigest()


def read(path):
    payload = path.read_bytes()
    return json.loads(payload), digest(payload)


def verify(row, configuration):
    original, original_hash = read(args.frozen_root / f"analyses/{row}/request.json")
    formal_request, formal_request_hash = read(args.formal_root / f"{row}-axis-formal-request.json")
    request = formal_request["request"]
    options = request["workbenchSettings"].copy()
    assert options.pop("FourierSamplingMode") == "Adaptive"
    assert options.pop("FourierMaximumSampling") == "4096"
    assert options.pop("FourierRelativeAccuracy") == "0.001"
    assert dict(request, workbenchSettings=options) == original["request"]
    assert request["configuration"] == configuration and request["pupilSampling"] == 32
    assert options["GraphScaleMicrometers"] == "0" and options["Row"] == "中心"
    assert options["Type"] == "X-线性" and options["Sampling"] == "32"
    snapshot_hash = digest(Path(formal_request["snapshotPath"]).read_bytes())
    assert snapshot_hash == digest(Path(original["snapshotPath"]).read_bytes())
    assert original_rows[row - 1]["index"] == row
    tolerance = original_rows[row - 1]["settings"]["quantities"]["Irradiance"]
    assert (tolerance["absolute"], tolerance["relative"], tolerance["nrmse"], tolerance["minimumCoverage"]) == (
        1e-5, .002, .003, .95)

    folder = args.formal_root / f"{row}-axis-formal"
    result, result_hash = read(folder / "data.json")
    provenance, provenance_hash = read(folder / "provenance.json")
    assert provenance["snapshotSha256"] == snapshot_hash
    assert provenance["executorId"] == "Application.WorkbenchRuntime.BuildAnalysisData/v1"
    assert result["name"] == "FFT PSF Cross Section" and result["outcome"] == "Success"
    values = result["values"]
    assert values["AxisFourierReadout"] and values["GraphScaleMicrometers"] == 0
    assert values["Row"] == "中心" and values["ProfileType"] == "X-线性" and values["ProfileIdealPeak"] == 1
    assert not values["Normalized"] and not values["UsePolarization"] and not values["UseRayAiming"]
    assert values["WavelengthNumber"] == values["FieldNumber"] == 1
    certificate = values["FourierSamplingValidation"]
    assert certificate["protocol"] == "FourierPhysicalDomainConvergence/v3"
    assert certificate["mode"] == "Adaptive" and certificate["validated"]
    assert certificate["requestedPupilSampling"] == 32
    assert certificate["returnedPupilSampling"] == certificate["maximumPupilSampling"] == 4096
    assert certificate["relativeRmsAccuracy"] == .001 and certificate["absoluteAccuracy"] == 1e-7
    assert (certificate["evaluatedNominalPupilCells"] + certificate["evaluatedStreamedPupilNodes"]
            <= certificate["maximumNominalPupilCells"] == 100_000_000)
    tiers = [step for step in certificate["steps"] if step.get("usesStreamedPhysicalPupil")]
    assert [step["pupilSampling"] for step in tiers] == [3072, 4096]
    assert all(s["phaseResolved"] and 0 < s["maximumPupilPhaseStepWaves"] <= .5 for s in tiers)
    assert tiers[-1]["relativeRmsChange"] <= certificate["relativeRmsAccuracy"]

    curve, = result["plotSeries"]
    assert (curve["xQuantity"], curve["xUnit"], curve["yQuantity"], curve["yUnit"]) == (
        "ImageHeight", "Micrometer", "Irradiance", "Dimensionless")
    points = curve["points"]
    assert len(points) == 201
    x = np.asarray([p["x"] for p in points])
    y = np.asarray([p["y"] for p in points])
    assert np.all(np.diff(x) > 0) and np.isfinite(y).all() and np.all(y >= 0)
    independent = args.independent_root / f"{row}-independent.npz"
    quadrature = np.load(independent)
    assert np.array_equal(x, quadrature["axis"])
    case = next(item for item in geometry if item["centralXSection"]["frozenIndex"] == row)
    axis = case["centralXSection"]
    assert axis["actualAxisMaximumDifferenceMicrometers"] <= 1e-10
    assert axis["quadratureComparison"]["peakNormalizedRms"] < 1e-8
    ref = quadrature["second"]
    difference = abs(y - ref)
    cross = {
        "coordinates": len(x), "coverage": 1,
        "maximumAbsolute": float(difference.max()),
        "peakNormalizedRms": float(np.sqrt(np.mean(difference**2)) / ref.max()),
        "pointwisePass": bool((difference <= tolerance["absolute"] + tolerance["relative"]*abs(ref)).all()),
    }
    cross["nrmsePass"] = cross["peakNormalizedRms"] <= tolerance["nrmse"]
    assert cross["pointwisePass"] and cross["nrmsePass"]
    return {
        "frozenIndex": row,
        "configuration": configuration,
        "status": "FormalAdaptiveAndIndependentScalarFftPass",
        "frozenRequestSha256": original_hash,
        "formalRequestSha256": formal_request_hash,
        "formalDataSha256": result_hash,
        "formalProvenanceSha256": provenance_hash,
        "provenance": provenance,
        "certificate": certificate,
        "diagnostics": values["StreamedFourierPupil"],
        "physicalAxis": {"quantity": "ImageHeight", "unit": "Micrometer",
                         "range": [float(x[0]), float(x[-1])], "points": 201},
        "independentAxisAndQuadrature": axis,
        "independentPayloadSha256": digest(independent.read_bytes()),
        "originalTolerance": tolerance,
        "versusIndependentScalarFft": cross,
        "originalNativeFftUsable": False,
    }


receipts = [verify(2901, 2), verify(2923, 3)]
args.output.write_text(json.dumps(receipts, indent=2) + "\n", encoding="utf-8")
