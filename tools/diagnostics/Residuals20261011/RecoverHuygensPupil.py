"""Offline spectral identification; never called by product code.

The input is an independently captured wide native planar complex field. This
diagnostic compares its Fourier peaks to captured ray directions; no recovered
value is permitted to supply a product PSF, acceptance criterion or correction.
"""
import json
import pathlib
import sys
import numpy as np

root = pathlib.Path(sys.argv[1]).resolve()
out = pathlib.Path(sys.argv[2]).resolve()
out.mkdir(parents=True, exist_ok=False)
base = root / "artifacts/validation/residuals-20261011/followup"
def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))
real = read(base / "wide/real/Planar/data.json")["dataGrids"][0]
imag = read(base / "wide/imaginary/Planar/data.json")["dataGrids"][0]
field = np.array(real["values"]) + 1j * np.array(imag["values"])
freq = np.fft.fftshift(np.fft.fftfreq(field.shape[0], d=real["dx"]))
spectrum = np.fft.fftshift(np.fft.fft2(field)) / field.size
power = np.abs(spectrum)
local_maximum = np.maximum.reduce([np.roll(np.roll(power, r, axis=0), c, axis=1) for r in [-1,0,1] for c in [-1,0,1]])
mask = (power == local_maximum) & (power > power.max() * .08)
rows, columns = np.nonzero(mask)
peaks = sorted(zip(columns, rows), key=lambda p: -power[p[1], p[0]])
results = []
for name in ["endpoints", "zemax"]:
    rays = read(base / f"native/pupil-{name}/pupil-grid.json")
    native = np.array([(v["l"] / .4861327, v["m"] / .4861327) for v in rays["rows"]])
    peak_coords = np.array([(freq[c], freq[r]) for c, r in peaks])
    nearest = np.sqrt(((peak_coords[:, None, :] - native[None, :, :]) ** 2).sum(axis=2)).min(axis=1)
    print(name, "peaks", len(peaks), "mean nearest frequency error", nearest.mean(), "max", nearest.max())
    results.append(dict(nodes=name, count=len(native), meanNearestFrequencyError=float(nearest.mean()), maximumNearestFrequencyError=float(nearest.max())))
summary = dict(shape=field.shape, pitch=real["dx"], frequencyPitch=float(freq[1]-freq[0]), comparisons=results,
               limitation="The native wide output is truncated to a central support; peaks do not certify pupil nodes.",
               peaks=[dict(fx=float(freq[c]), fy=float(freq[r]), amplitude=float(power[r,c]), phase=float(np.angle(spectrum[r,c]))) for c,r in peaks])
(out / "spectrum.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
