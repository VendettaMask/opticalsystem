"""Compare fixed numeric points with the frozen native plot's raster curve.

This is bounded screenshot digitization, not a replacement for native numeric data.
The bounds are taken from this one 1056 x 792 capture's declared physical axes.
"""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

here = Path(__file__).resolve().parent
root = next(p for p in here.parents if (p / "OpticalSystemDesign.slnx").exists())
native = root / "validation/rechecks/2026-11-21/all-output-visual-comparison/representative-six/afocal-riflescope/zemax/color-focus-shift.JPG"
fixed = json.loads((here / "fixed-core.json").read_text(encoding="utf-8"))
raw = json.loads((here / "independent/results.json").read_text(encoding="utf-8"))
points = fixed["points"]
waves = np.array([p["Y"] for p in points])
shifts = np.array([p["X"] for p in points])
primary = fixed["Values"]["PrimaryParaxialVergenceDiopters"]
old = [1e6 / r["v"] - 1e6 / primary for r in raw]
independent = np.array([r["v"] - primary for r in raw])
assert len(points) == len(raw) == 101
assert np.max(np.abs(independent - shifts)) < 1e-12
(here / "before-reconstructed.json").write_text(json.dumps({
    "scope": "Old distance-difference formula reconstructed from frozen pre-fix raw paraxial traces",
    "unit": "micrometer", "maximumChange": max(old) - min(old),
    "points": [{"wavelength": r["w"], "shift": x} for r, x in zip(raw, old)]
}, indent=2), encoding="utf-8")

rgb = np.asarray(Image.open(native).convert("RGB"), dtype=np.int16)
assert rgb.shape == (792, 1056, 3)
left, right, top, bottom = 230, 1020, 47, 509
samples = []
for row in range(top + 1, bottom):
    scan = rgb[row, left:right + 1]
    blue = (scan[:, 2] > scan[:, 0] + 24) & (scan[:, 2] > scan[:, 1] + 24)
    columns = np.flatnonzero(blue)
    if len(columns) == 0:
        continue
    x = left + float(np.median(columns))
    wavelength = 0.656 - (row - top) / (bottom - top) * (0.656 - 0.486)
    native_d = -0.2 + (x - left) / (right - left) * 0.4
    fixed_d = float(np.interp(wavelength, waves, shifts))
    samples.append({"row": row, "wavelengthMicrometers": wavelength,
                    "nativeDigitizedDiopters": native_d, "fixedDiopters": fixed_d,
                    "absoluteErrorDiopters": abs(native_d - fixed_d)})
assert len(samples) > 400
errors = np.array([s["absoluteErrorDiopters"] for s in samples])
report = {
    "scope": "Frozen native screenshot digitization at pupil zone 0; not a native numeric export",
    "nativeImage": str(native.relative_to(root)),
    "nativeSha256": hashlib.sha256(native.read_bytes()).hexdigest(),
    "plotPixelBounds": [left, top, right, bottom],
    "plotBoundsDiopters": [-0.2, 0.2], "wavelengthBoundsMicrometers": [0.486, 0.656],
    "oneHorizontalPixelDiopters": 0.4 / (right - left),
    "rowsCompared": len(samples),
    "maximumErrorDiopters": float(errors.max()),
    "rmsErrorDiopters": float(np.sqrt(np.mean(errors**2))),
    "fixedMaximumVariationDiopters": float(shifts.max() - shifts.min()),
    "nativePrintedMaximumVariationDiopters": 0.1585,
    "oldReconstructedMaximumVariationMicrometers": max(old) - min(old),
    "samples": samples,
}
assert errors.max() < 0.0015  # Three horizontal pixels in this frozen capture.
(here / "native-curve-comparison.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps({key: value for key, value in report.items() if key != "samples"}, indent=2))
