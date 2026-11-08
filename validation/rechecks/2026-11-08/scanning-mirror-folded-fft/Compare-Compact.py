import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


parser = argparse.ArgumentParser()
parser.add_argument("reference", type=Path)
parser.add_argument("compact", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
reference = json.loads((args.reference / "reference-receipt.json").read_text())


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def metric(candidate, truth):
    difference = abs(candidate - truth)
    return {
        "coordinates": int(truth.size),
        "maximumAbsolute": float(difference.max()),
        "peakNormalizedRms": float(np.sqrt(np.mean(difference**2)) / truth.max()),
        "relativeL2": float(np.linalg.norm(difference) / np.linalg.norm(truth)),
    }


for report in reference:
    row = report["frozenIndex"]
    axes_and_image = args.reference / f"{row}-independent.npz"
    frozen = np.load(axes_and_image)
    target = frozen["second"]
    paths = [args.compact / f"{row}-{tier}.json" for tier in (2048, 4096)]
    receipts = [json.loads(path.read_text()) for path in paths]
    images = [np.asarray(receipt.pop("image")) for receipt in receipts]
    assert all(np.array_equal(receipt["axis"], frozen["axis"]) for receipt in receipts)
    assert all(image.shape == (128, 128) and np.isfinite(image).all() for image in images)
    assert all(receipt["phase"]["Status"] == "ResolvedAtNodes"
               and receipt["phase"]["MaximumStepWaves"] <= .5 for receipt in receipts)
    assert receipts[0]["coreSha256"] == receipts[1]["coreSha256"]
    assert receipts[0]["sourceSnapshotSha256"] == receipts[1]["sourceSnapshotSha256"]
    for key in ("TangentialWorkingFNumber", "SagittalWorkingFNumber"):
        assert receipts[0][key] == receipts[1][key]

    lower, higher = images
    convergence = metric(lower, higher)
    convergence["relativeCenterChange"] = float(abs(lower[63, 63] - higher[63, 63]) / higher[63, 63])
    convergence["relativePeakChange"] = float(abs(lower.max() - higher.max()) / higher.max())
    convergence["metricWithPublishedScalars"] = max(
        convergence["peakNormalizedRms"], convergence["relativeCenterChange"], convergence["relativePeakChange"])
    convergence["pass"] = convergence["metricWithPublishedScalars"] <= .001
    cross = metric(higher, target)
    cross["pointwisePass"] = bool((abs(higher - target) <= 1e-5 + .001*abs(target)).all())
    cross["nrmsePass"] = cross["peakNormalizedRms"] <= .01
    cross["coverage"] = 1
    report["compactDirectDiagnostic"] = {
        "tierPayloads": [{"file": path.name, "sha256": sha256(path), "metadata": receipt}
                         for path, receipt in zip(paths, receipts)],
        "betweenTiers": convergence,
        "highTierVersusIndependentScalarFft": cross,
    }
    report["independentPayload"] = {"file": axes_and_image.name, "sha256": sha256(axes_and_image)}
    formal = args.reference / f"{row}-product.json"
    if formal.exists():
        result = json.loads(formal.read_text())
        report["priorStretchedFormal"] = {
            "file": formal.name, "sha256": sha256(formal), "coreSha256": result["coreSha256"],
            "outcome": result["Outcome"], "reason": result["OutcomeReason"],
            "certificate": result["Values"]["FourierSamplingValidation"],
        }
    assert convergence["pass"] and cross["pointwisePass"] and cross["nrmsePass"]

args.output.write_text(json.dumps(reference, indent=2) + "\n", encoding="utf-8")
