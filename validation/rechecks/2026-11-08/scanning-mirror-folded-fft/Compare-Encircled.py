import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


parser = argparse.ArgumentParser()
parser.add_argument("frozen_root", type=Path)
parser.add_argument("formal_root", type=Path)
parser.add_argument("independent_root", type=Path)
parser.add_argument("original_native", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--index", type=int, choices=(2882, 2904, 2926), default=2882)
args = parser.parse_args()


def digest(payload):
    return hashlib.sha256(payload).hexdigest()


def read(path):
    payload = path.read_bytes()
    return json.loads(payload), digest(payload)


row = args.index
configuration = (row - 2882)//22 + 1
request0, frozen_hash = read(args.frozen_root / f"analyses/{row}/request.json")
baseline, _ = read(args.frozen_root / "rows.json")
assert baseline[row - 1]["index"] == row
tolerance = baseline[row - 1]["settings"]["quantities"]["EnergyFraction"]
assert (tolerance["absolute"], tolerance["relative"], tolerance["nrmse"], tolerance["minimumCoverage"]) == (
    1e-5, .002, .003, .95)
snapshot_hash = digest(Path(request0["snapshotPath"]).read_bytes())
native, native_hash = read(args.original_native)
assert len(native["messages"]) == 1 and "取样太低" in native["messages"][0]["Text"]
assert not native["dataSeries"] and not native["dataGrids"]
independent, independent_hash = read(args.independent_root / f"{row}-independent-receipt.json")
reference = independent["encircledEnergy"]
assert reference["idealCentroidMicrometers"] == [0, 0]
assert reference["actualCentroidMicrometers"][0] == 0
if row == 2882:
    assert reference["actualCentroidMicrometers"][1] == 0
    assert reference["idealVersusAiry"]["maximumAbsolute"] < 1e-12
else:
    assert reference["centroidAgreementMaximumMicrometers"] < 1e-6
    assert reference["idealVersusEllipticalJinc"]["maximumAbsolute"] < 1e-12
assert reference["actualBetweenQuadratures"]["peakNormalizedRms"] < 1e-8
independent_payload = args.independent_root / f"{row}-independent.npz"
data = np.load(independent_payload)
radii = data["radius"]


def compare(candidate, truth):
    delta = abs(candidate-truth)
    nrmse = float(np.sqrt(np.mean(delta**2)) / max(truth.max(), tolerance["absolute"]))
    pointwise = bool((delta <= tolerance["absolute"] + tolerance["relative"]*abs(truth)).all())
    rms_pass = nrmse <= tolerance["nrmse"]
    branch = ("Both" if rms_pass else "Pointwise") if pointwise else ("NormalizedRms" if rms_pass else "Neither")
    return {"coordinates": int(radii.size), "coverage": 1, "maximumAbsolute": float(delta.max()),
            "peakNormalizedRms": nrmse, "pointwisePass": pointwise, "nrmsePass": rms_pass,
            "acceptanceBranch": branch, "pass": pointwise or rms_pass}


entries = []
curves = []
sampling_tiers = (32768, 65536, 131072) if row == 2882 else (65536, 131072)
for n in sampling_tiers:
    name = f"{row}-energy-{n}"
    job, job_hash = read(args.formal_root / f"{name}-request.json")
    request = job["request"]
    workbench = request["workbenchSettings"].copy()
    for key in ("PupilSampling", "Sampling"):
        assert workbench[key] == str(n)
        workbench[key] = request0["request"]["workbenchSettings"][key]
    assert request["pupilSampling"] == n
    assert dict(request, pupilSampling=32, workbenchSettings=workbench) == request0["request"]
    assert request["configuration"] == configuration and request["field"] == request["wavelength"] == 1
    assert request["zemaxSettings"]["UseHuygensPSF"] is False and request["reference"] == "Centroid"
    assert not request["polarization"] and not request["useRayAiming"]
    assert json.loads(request["apodization"])["numbers"]["type"] == 0
    assert digest(Path(job["snapshotPath"]).read_bytes()) == snapshot_hash
    result, result_hash = read(args.formal_root / f"{name}/data.json")
    provenance, provenance_hash = read(args.formal_root / f"{name}/provenance.json")
    assert provenance["snapshotSha256"] == snapshot_hash
    assert provenance["executorId"] == "Application.WorkbenchRuntime.BuildAnalysisData/v1"
    assert result["outcome"] == "Success" and result["name"] == "Diffraction Encircled Energy"
    values = result["values"]
    assert values["Method"] == "FFT cumulative distance-bin integration"
    assert values["PupilSampling"] == n and values["Reference"] == "centroid"
    assert values["MaximumDistanceMicrometers"] == 5 and values["DiffractionLimit"]
    plots = result["plotSeries"]
    assert len(plots) == 2 and [p["name"] for p in plots] == ["衍射极限", "Field 1"]
    extracted = {}
    for label, plot in zip(("ideal", "actual"), plots):
        assert (plot["xQuantity"], plot["xUnit"], plot["yQuantity"], plot["yUnit"]) == (
            "Radius", "Micrometer", "EnergyFraction", "Dimensionless")
        assert len(plot["points"]) == 401
        x = np.array([p["x"] for p in plot["points"]])
        y = np.array([p["y"] for p in plot["points"]])
        assert np.max(abs(x-radii)) <= 1e-12 and np.isfinite(y).all() and np.all(np.diff(y) >= 0)
        assert y[0] == 0 and y[-1] < 1
        extracted[label] = y
    curves.append(extracted)
    entries.append({"nominalPupilSampling": n, "requestSha256": job_hash, "dataSha256": result_hash,
                    "provenanceSha256": provenance_hash, "provenance": provenance,
                    "fourierSize": values["BoundedFourierSampling"],
                    "distanceBinWidthMicrometers": values["DistanceBinWidth"],
                    "versusIndependent": {label: compare(curve, data[label]) for label, curve in extracted.items()}})

pairs = [{label: compare(a[label], b[label]) for label in a} for a, b in zip(curves, curves[1:])]
assert all(item["pass"] for item in pairs[-1].values())
assert all(item["pass"] and item["pointwisePass"] for item in entries[-1]["versusIndependent"].values())
receipt = {
    "frozenIndex": row, "configuration": configuration, "status": "FormalIndependentScalarFftEnergyPass",
    "originalNativeEnergyUsable": False, "originalNativeDiagnosticSha256": native_hash,
    "frozenRequestSha256": frozen_hash, "sourceSnapshotSha256": snapshot_hash, "originalTolerance": tolerance,
    "independentReceiptSha256": independent_hash, "independentPayloadSha256": digest(independent_payload.read_bytes()),
    "physicalReference": reference, "formal": entries,
    "betweenFormalTiers": [{"lower": a, "higher": b, "curves": c}
                           for a, b, c in zip(sampling_tiers, sampling_tiers[1:], pairs)],
}
args.output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
