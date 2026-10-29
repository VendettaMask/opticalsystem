"""Independent offline replay of traced pupil data; no runtime optical dependency."""
import os
os.environ.setdefault("OPENBLAS_NUM_THREADS", "2")
from pathlib import Path
import json
import zipfile
import numpy as np
from scipy.interpolate import CubicSpline

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/huygens-focus-20261029"
summary = []
for path in sorted((RUN / "pupil-diagnostics").glob("*beam*.json")):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    pupil = data["pupil"]
    samples = pupil["samples"]
    diameter = pupil["afocalPupilDiameterMillimeters"]
    origin = pupil["afocalReferencePlaneOrigin"]
    positions = np.array([[s["pupilX"] - origin["x"], s["pupilY"] - origin["y"], s["pupilZ"] - origin["z"]] for s in samples])
    normalized = np.array([[s["normalizedPupilX"], s["normalizedPupilY"]] for s in samples]) * diameter / 2
    amplitude = np.sqrt([s["launchIntensity"] for s in samples])
    opd = np.array([s["opdWaves"] for s in samples])
    wavelength = pupil["sourceWavelengthNanometers"] * 1e-6
    n = data["rows"][0]["n"]
    pitch = data["rows"][0]["sampleSpacingMicrometers"]
    x = (np.arange(n) - n // 2) * pitch
    tx, ty = np.meshgrid(x, x)
    half_extent = .61 * wavelength * 1000 * (data["request"]["pupilSampling"] - 2) / diameter
    footprint = (np.sinc(tx / half_extent) * np.sinc(ty / half_extent)) ** 2
    footprint[(abs(tx) >= half_extent) | (abs(ty) >= half_extent)] = 0
    knots = np.array([-.01, -.005, 0, .005, .01])
    target = data["request"]["maximumFrequency"] * (n - 1) / n
    frequency = np.arange(n // 2) / (n * pitch)
    branch = "fixed-grid-controls" if data["id"].startswith("fixed-") else "expanded"
    member = f"artifacts/validation/afocal-recompare-20261026/{branch}/{data['id']}/native/data.json"
    with zipfile.ZipFile(ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip") as zip:
        native_group = json.loads(zip.read(member))["dataSeries"][0]
    native_x = np.array(native_group["x"])
    native = np.array(native_group["y"]).T
    modes = [("original", positions[:, :2], (positions ** 2).sum(axis=1)),
             ("paraxial-phase-only", positions[:, :2], (normalized ** 2).sum(axis=1)),
             ("incorrect-paraxial-propagation", normalized, (normalized ** 2).sum(axis=1))]
    rows = []
    for name, coordinates, radius_squared in modes:
        coefficients = amplitude[:, None] * np.exp(-2j * np.pi *
            (opd[:, None] - knots[None, :] * radius_squared[:, None] / (2000 * wavelength)))
        field = np.empty((n * n, 5), dtype=complex)
        for start in range(0, n * n, 128):
            end = min(n * n, start + 128)
            kernel = np.exp(-2j * np.pi / wavelength / 1000 *
                (tx.ravel()[start:end, None] * coordinates[:, 0] + ty.ravel()[start:end, None] * coordinates[:, 1]))
            field[start:end] = kernel @ coefficients
        psfs = abs(field.reshape(n, n, 5)) ** 2 * footprint[:, :, None] / amplitude.sum() ** 2 * 100
        raw = []
        for axis in [1, 0]:
            transformed = abs(np.fft.fft2(psfs, axes=(0, 1)))
            curves = transformed[:n // 2, 0] if axis == 1 else transformed[0, :n // 2]
            curves = curves / transformed[0, 0]
            raw.append(CubicSpline(frequency, curves, axis=0, bc_type="natural")(target))
        display = np.array([CubicSpline(knots, values, bc_type="natural")(native_x) for values in raw])
        errors = abs(display - native)
        rows.append({"mode": name, "rawTangential": raw[0].tolist(), "rawSagittal": raw[1].tolist(),
            "maximumByCurve": errors.max(axis=1).tolist(),
            "violationsAtNativeDisplayPoints": int((errors > 1e-5 + .002 * abs(native)).sum())})
        if name == "original":
            stored = np.array([r["values"] for r in data["rows"]]).transpose(1, 2, 0)
            assert abs(psfs - stored).max() < 1e-10
            rows[-1]["psfReplayMaximumAbsolute"] = float(abs(psfs - stored).max())
    # Independently reconstruct the native display spline from its five knots.
    expected_knots = native[:, ::25]
    assert abs(np.array([CubicSpline(knots, values, bc_type="natural")(native_x) for values in expected_knots]) - native).max() < 1e-13
    record = {"id": data["id"], "n": n, "pitchMilliradians": pitch, "modes": rows,
        "nativeNaturalSplineReconstructionMaximum": float(abs(np.array([CubicSpline(knots, values, bc_type="natural")(native_x) for values in expected_knots]) - native).max())}
    summary.append(record)
    print(json.dumps(record), flush=True)
(RUN / "decomposition.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
