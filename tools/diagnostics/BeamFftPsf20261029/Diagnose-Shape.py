"""Offline Fourier diagnosis of exported Core samples; not a product computation path."""
from pathlib import Path
import hashlib
import json
import zipfile
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/beam-fft-shape-20261029"
rows = []


def interpolate(a, chief=True, rms=False):
    valid = a > 0
    yy, xx = np.indices(a.shape)
    selected = valid & ((xx + yy) % 2 == 0)
    for axis in (0, 1):
        for shift in (-1, 1):
            selected &= np.roll(valid, shift, axis)
    if not chief:
        selected[a.shape[0] // 2, a.shape[1] // 2] = False
    v = a * a if rms else a
    mean = sum(np.roll(v, s, ax) for ax in (0, 1) for s in (-1, 1)) / 4
    result = a.copy()
    result[selected] = np.sqrt(mean[selected]) if rms else mean[selected]
    return result


for path in sorted((RUN / "before").glob("*.json")):
    row = json.loads(path.read_text(encoding="utf-8-sig"))
    n, g = row["n"], row["g"]
    a, phase, raw_phase = (np.zeros((n, n)) for _ in range(3))
    for p in row["points"]:
        y, x = p["row"], p["column"]
        a[y, x] = np.sqrt(p["intensity"])
        phase[y, x], raw_phase[y, x] = p["phase"], p["rawPhase"]
    expected = np.array(row["native"]["z"])
    actual = np.array(row["actual"]["z"])
    variants = {}
    for name, amplitude, ph in [
        ("baseline", a, phase), ("zero-phase", a, phase * 0),
        ("raw-phase", a, raw_phase),
        ("mean-amplitude", interpolate(a), phase),
        ("mean-amplitude-keep-chief", interpolate(a, chief=False), phase),
        ("rms-amplitude", interpolate(a, rms=True), phase),
    ]:
        pupil = np.zeros((g, g), dtype=complex)
        offset = (g - n) // 2
        pupil[offset:offset+n, offset:offset+n] = amplitude * np.exp(-2j * np.pi * ph)
        values = abs(np.fft.fftshift(np.fft.fft2(pupil)))**2 / amplitude.sum()**2
        # The existing presentation uses center floor((g-1)/2), rather than g/2.
        values = np.roll(values, (-1, -1), axis=(0, 1))
        error = abs(values - expected)
        variants[name] = {"maximumAbsolute": float(error.max()),
                          "violations": int((error > 1e-5 + .001 * abs(expected)).sum()),
                          "peak": float(values.max()),
                          "windowSumRatio": float(values.sum() / expected.sum())}
        if name == "baseline":
            variants[name]["reconstructionMaximumAbsolute"] = float(abs(values - actual).max())
    rows.append({"id": row["id"], "n": n, "g": g, "acceptedNodes": int((a > 0).sum()),
                 "sourceSha256": hashlib.sha256(path.read_bytes()).hexdigest(), "variants": variants})

archive = ROOT / "validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip"
with zipfile.ZipFile(archive) as z:
    prefix = "artifacts/validation/afocal-fft-energy-20261027/native-energy-grid-v2/beam-expander-"
    real = json.loads(z.read(prefix + "Real/data.json"))["dataGrids"][0]
    imag = json.loads(z.read(prefix + "Imaginary/data.json"))["dataGrids"][0]
    field = np.conj(np.array(real["values"]) + 1j * np.array(imag["values"]))
    pupil = np.fft.fftshift(np.fft.ifft2(np.roll(field, (-31, -31), axis=(0, 1))))
    native = abs(pupil) / abs(pupil).max()
    y, x = np.indices((64, 64))
    # The archived request has the independent DEE pupil interval 1/(2 sqrt(32)).
    r2 = ((x - 32)**2 + (y - 32)**2) / 128
    source = np.exp(-2 * r2) * (r2 <= 1)
    candidates = {}
    for kind, values in [("exact-gaussian", source), ("mean-amplitude", interpolate(source)),
                         ("rms-amplitude", interpolate(source, rms=True))]:
        values = values / values.max()
        candidates[kind] = float(abs(values - native).max())
    auxiliary = {"request": prefix, "n": 32, "g": 64,
                 "nativeNodes": int((native > 1e-8).sum()),
                 "sourceNodes": int((source > 0).sum()), "amplitudeMaximumErrors": candidates}

result = {"sameRequestDecomposition": rows, "separateFrozenComplexControl": auxiliary,
          "scope": "Prescribed amplitude stencil, no fitted scale, phase, orientation or pupil size; auxiliary grid is a separate request."}
(RUN / "decomposition.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
