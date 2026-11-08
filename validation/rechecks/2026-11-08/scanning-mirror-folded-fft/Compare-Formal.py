import argparse
import hashlib
import io
import json
import zipfile
from pathlib import Path

import numpy as np


parser = argparse.ArgumentParser()
parser.add_argument("frozen_jobs", type=Path)
parser.add_argument("formal_jobs", type=Path)
parser.add_argument("numeric_archive", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    payload = path.read_bytes()
    return json.loads(payload), digest(payload)


def difference(actual, expected):
    residual = abs(actual - expected)
    return {
        "coordinates": int(expected.size),
        "coverage": 1,
        "maximumAbsolute": float(residual.max()),
        "peakNormalizedRms": float(np.sqrt(np.mean(residual**2)) / expected.max()),
        "relativeL2": float(np.linalg.norm(residual) / np.linalg.norm(expected)),
    }


def compare(archive, row, configuration):
    job, frozen_job_hash = read(args.frozen_jobs / str(row) / "request.json")
    request_file = args.formal_jobs / f"{row}-4096-refined-request.json"
    refined, refined_hash = read(request_file)
    request = refined["request"]
    options = request["workbenchSettings"].copy()
    assert options.pop("FourierSamplingMode") == "Adaptive"
    assert options.pop("FourierMaximumSampling") == "4096"
    assert options.pop("FourierRelativeAccuracy") == "0.001"
    assert dict(request, workbenchSettings=options) == job["request"]
    assert request["canonicalAnalysisKey"] == "PSF" and request["configuration"] == configuration
    assert request["pupilSampling"] == 64 and request["imageSampling"] == 128
    assert request["imageDeltaMicrometers"] == .25 and request["primaryWavelengthMicrometers"] == .55
    snapshot_hash = digest(Path(refined["snapshotPath"]).read_bytes())
    assert snapshot_hash == digest(Path(job["snapshotPath"]).read_bytes())

    folder = args.formal_jobs / f"{row}-4096-refined"
    provenance, provenance_hash = read(folder / "provenance.json")
    assert provenance["snapshotSha256"] == snapshot_hash
    assert provenance["executorId"] == "Application.WorkbenchRuntime.BuildAnalysisData/v1"
    result, result_hash = read(folder / "data.json")
    assert result["name"] == "PSF" and result["outcome"] == "Success"
    values = result["values"]
    assert values["PhysicalFourierWindow"] and values["DisplaySize"] == values["RequestedDisplaySize"] == 128
    assert values["ImageDeltaMicrometers"] == values["RequestedImageDelta"] == .25
    assert values["FieldNumber"] == values["WavelengthNumber"] == 1
    assert values["FieldHx"] == values["FieldHy"] == values["RotationDegrees"] == 0
    assert values["SurfaceNumber"] == -1 and values["UnaberratedPeakRatio"] == 1
    assert not values["Normalized"] and not values["UsePolarization"] and values["UseRayAiming"] == [False]

    certificate = values["FourierSamplingValidation"]
    assert certificate["protocol"] == "FourierPhysicalDomainConvergence/v3"
    assert certificate["mode"] == "Adaptive" and certificate["validated"]
    assert certificate["requestedPupilSampling"] == 64
    assert certificate["returnedPupilSampling"] == certificate["maximumPupilSampling"] == 4096
    relative = certificate["relativeRmsAccuracy"]
    absolute = certificate["absoluteAccuracy"]
    assert relative == .001 and absolute == 1e-7
    assert (certificate["evaluatedNominalPupilCells"] + certificate["evaluatedStreamedPupilNodes"]
            <= certificate["maximumNominalPupilCells"] == 100_000_000)

    steps = certificate["steps"]
    streamed = [step for step in steps if step.get("usesStreamedPhysicalPupil")]
    assert [s["pupilSampling"] for s in streamed] == [2048, 4096, 2048, 4096]
    assert all(s["phaseResolved"] and 0 < s["maximumPupilPhaseStepWaves"] <= .5 for s in streamed)
    assert [bool(s.get("usesPhysicalPsfWindow")) for s in streamed] == [False, False, True, True]
    assert streamed[1]["relativeRmsChange"] > relative and streamed[1]["maximumAbsoluteChange"] > absolute
    assert streamed[3]["relativeRmsChange"] <= relative

    reference = np.load(io.BytesIO(archive.read(f"{row}-independent.npz")))
    axis, truth = reference["axis"], reference["second"]
    assert len(result["plotSeries"]) == 1
    plot = result["plotSeries"][0]
    assert plot["xQuantity"] == plot["yQuantity"] == "ImageHeight"
    assert plot["xUnit"] == plot["yUnit"] == "Micrometer"
    assert plot["valueQuantity"] == "Irradiance" and plot["valueUnit"] == "Dimensionless"
    points = plot["points"]
    assert len(points) == 128*128
    assert np.array_equal([point["x"] for point in points], np.tile(axis, 128))
    assert np.array_equal([point["y"] for point in points], np.repeat(axis, 128))
    actual = np.asarray([point["value"] for point in points]).reshape((128, 128))
    assert np.isfinite(actual).all() and np.all(actual >= 0)
    assert values["StrehlRatio"] == actual[63, 63] and values["DisplayedWindowPeakRatio"] == actual.max()

    cross = difference(actual, truth)
    cross["pointwisePass"] = bool((abs(actual - truth) <= 1e-5 + .001*abs(truth)).all())
    cross["nrmsePass"] = cross["peakNormalizedRms"] <= .01
    assert cross["pointwisePass"] and cross["nrmsePass"]
    direct = np.asarray(json.loads(archive.read(f"{row}-4096.json"))["image"])
    same_tier = difference(actual, direct)
    assert same_tier["maximumAbsolute"] <= 1e-15
    return {
        "frozenIndex": row,
        "configuration": configuration,
        "status": "FormalAdaptiveAndIndependentScalarFftPass",
        "frozenRequestSha256": frozen_job_hash,
        "formalRequestSha256": refined_hash,
        "formalDataSha256": result_hash,
        "formalProvenanceSha256": provenance_hash,
        "provenance": provenance,
        "certificate": certificate,
        "diagnostics": values["StreamedFourierPupil"],
        "physicalDomain": {"quantity": "ImageHeight", "unit": "Micrometer",
                           "xRange": [float(axis[0]), float(axis[-1])],
                           "yRange": [float(axis[0]), float(axis[-1])], "pitch": .25, "grid": [128, 128]},
        "originalTolerance": {"absolute": 1e-5, "relative": .001, "nrmse": .01, "minimumCoverage": .95},
        "versusIndependentScalarFft": cross,
        "versusDirectCompact4096": same_tier,
        "rawArchiveMembers": [f"{row}-formal-{suffix}.json" for suffix in ("request", "data", "provenance")],
        "originalNativeFftUsable": False,
    }


with zipfile.ZipFile(args.numeric_archive) as archive:
    receipts = [compare(archive, 2897, 2), compare(archive, 2919, 3)]
args.output.write_text(json.dumps(receipts, indent=2) + "\n", encoding="utf-8")
