"""Offline replay of saved OTFs; never used by the product or to create native references."""
from pathlib import Path
import hashlib
import json
import numpy as np
from scipy.interpolate import CubicSpline

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/fft-mtf-20261029"


def complex_values(values):
    return np.array([c["real"] + 1j * c["imaginary"] for c in values])


def sample(axis, values, target, method):
    axis = np.array(axis)
    if method == "linear":
        result = np.interp(target, axis, values.real) + 1j * np.interp(target, axis, values.imag)
    else:
        result = CubicSpline(axis, values, bc_type="natural" if method == "natural" else "not-a-knot")(target)
    return np.where(target > axis[-1], 0, result)


output = []
for path in sorted((RUN / "before").glob("*.json")):
    row = json.loads(path.read_text(encoding="utf-8-sig"))
    modes = {}
    reconstruction = 0.0
    for fi, field in enumerate(row["variants"][0]["rows"]):
        for direction, key in enumerate(["tangential", "sagittal"]):
            expected = row["expected"]["series"][2 * fi + direction]
            actual = row["actual"]["series"][2 * fi + direction]
            targets = np.array(actual["x"])
            mono = field["monochromatic"]
            weights = np.array([m["weight"] for m in mono], dtype=float)
            weights = weights / weights.sum()
            for mode in ["combined-natural", "combined-notaknot", "combined-linear",
                         "direct-natural", "direct-notaknot", "direct-linear", "magnitudes-natural"]:
                method = mode.rsplit("-", 1)[1]
                if mode.startswith("combined"):
                    combined = field["combined"]
                    value = sample(combined["frequency"], complex_values(combined[key]), targets, method)
                else:
                    terms = []
                    for m, weight in zip(mono, weights):
                        v = sample(m["frequency"], complex_values(m[key]), targets, method)
                        terms.append((abs(v) if mode.startswith("magnitudes") else v) * weight)
                    value = sum(terms)
                value = np.clip(abs(value), 0, 1)
                if mode == "combined-natural":
                    reconstruction = max(reconstruction, float(abs(value - np.array(actual["y"])).max()))
                union = np.unique(np.r_[expected["x"], targets])
                reference = np.interp(union, expected["x"], expected["y"])
                error = abs(np.interp(union, targets, value) - reference)
                result = modes.setdefault(mode, {"maximumAbsolute": 0, "violations": 0})
                result["maximumAbsolute"] = max(result["maximumAbsolute"], float(error.max()))
                result["violations"] += int((error > .001 + .001 * abs(reference)).sum())
    controls = [{"mode": v["mode"], "maximumAbsolute": max(c["maximum"]
                for f in v["rows"] for c in f["comparisons"])} for v in row["variants"]]
    output.append({"id": row["id"], "pupilSampling": row["n"], "fftGrid": row["g"],
                   "sourceSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                   "baselineReconstructionMaximumAbsolute": reconstruction,
                   "opticalAndAxisControls": controls, "combinationControls": modes})
record = {"scope": "Original monochromatic OTF samples and display coordinates, unchanged natural spline; controls do not fit parameters or replace native reference values.", "cases": output}
(RUN / "decomposition.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
print(json.dumps(record, indent=2))
