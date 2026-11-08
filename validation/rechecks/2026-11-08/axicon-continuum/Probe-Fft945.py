import argparse
import gzip
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
from scipy.interpolate import CubicSpline
from scipy.optimize import least_squares
from scipy.special import j0


parser = argparse.ArgumentParser()
parser.add_argument("rays", type=Path)
parser.add_argument("wavefront", type=Path)
parser.add_argument("first_order_report", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


ray_hash = sha256(args.rays)
assert ray_hash == "f10932ba1ddb438722f7af7add2402b86c7d7c7c27a8e3113449bc20261cdb41"
with gzip.open(args.rays, "rb") as stream:
    count = struct.unpack("<i", stream.read(4))[0]
    samples = np.frombuffer(stream.read(), dtype="<f8").reshape((count, 6))
assert count == 8191 * 12 + 16384 * 8
quadratures = (samples[:8191 * 12], samples[8191 * 12:])
ordered = samples[np.argsort(samples[:, 0])]
radius = ordered[:, 0]
assert np.all(np.diff(radius) > 0)

wavelength = .55
wavelength_mm = wavelength * .001
plane = CubicSpline(radius, ordered[:, 2] - ordered[0, 2])
impact_squared = CubicSpline(radius, (ordered[:, 4] * ordered[:, 5]) ** 2)


def sphere_correction(impact2, sphere):
    return impact2 / (sphere + np.sqrt(sphere**2 - impact2))


def wavefront(r, sphere):
    # The signed r·direction term cancels when the plane eikonal is
    # propagated back to the real reference sphere about the chief.
    return -(plane(r) + sphere_correction(impact_squared(r), sphere)) / wavelength_mm


raw = json.loads(args.wavefront.read_text(encoding="utf-8-sig"))
values = np.asarray(raw["dataGrids"][0]["values"], dtype=float)
assert not raw["messages"] and values.shape == (64, 64)
assert np.nanargmin(values) == 32 * 64 + 32
y, x = np.indices(values.shape)
finite = np.isfinite(values)
valid = finite & ((x != 32) | (y != 32))
r2 = (x[valid] - 32) ** 2 + (y[valid] - 32) ** 2
distinct, membership = np.unique(r2, return_inverse=True)
launch = np.sqrt(r2) / 31
native_waves = values[valid]
assert np.count_nonzero(finite) == 3001 and launch.max() == 1

# Split by radius so symmetry-equivalent samples cannot leak into the holdout.
fit_mask = membership % 3 == 0
reported_sphere = 463.1391
reference = least_squares(
    lambda p: wavefront(launch[fit_mask], p[0]) + p[1] - native_waves[fit_mask],
    x0=[reported_sphere, 3490.04], xtol=1e-15, ftol=1e-15, gtol=1e-12,
)
sphere, piston = reference.x
residual = wavefront(launch[~fit_mask], sphere) + piston - native_waves[~fit_mask]
reference_delta = wavefront(launch, reported_sphere) - native_waves
reference_delta -= reference_delta.mean()

marginal_sine = CubicSpline(radius, ordered[:, 3])(1)
fnumber = 1 / (2 * marginal_sine)
pitch = .25
axis = np.arange(-63, 65) * pitch
y, x = np.meshgrid(np.arange(-63, 65), np.arange(-63, 65), indexing="ij")
pixel_r2, inverse = np.unique(x ** 2 + y ** 2, return_inverse=True)
kernel = np.pi / (wavelength * fnumber) * pitch * np.sqrt(pixel_r2)
arguments = np.linspace(0, kernel.max(), 8193)
azimuth = np.cos(np.pi * (np.arange(128) + .5) / 128)
bessel_check = np.max(np.abs(j0(arguments) - np.cos(arguments[:, None] * azimuth).mean(axis=1)))


def integrate(nodes, reference_sphere):
    impact2 = (nodes[:, 4] * nodes[:, 5]) ** 2
    path = nodes[:, 2] - nodes[0, 2] + sphere_correction(impact2, reference_sphere)
    coherent = nodes[:, 1] * np.exp(2j * np.pi * path / wavelength_mm) / nodes[:, 1].sum()
    amplitude = np.empty(kernel.size, dtype=np.complex128)
    for first in range(0, kernel.size, 32):
        block = slice(first, min(first + 32, kernel.size))
        bessel = j0(kernel[block, None] * nodes[:, 0])
        amplitude[block] = bessel @ coherent.real + 1j * (bessel @ coherent.imag)
    return np.abs(amplitude) ** 2


def comparison(candidate, truth):
    error = (candidate - truth)[inverse]
    peak = truth.max()
    return {
        "coordinates": int(inverse.size),
        "maximumAbsolute": float(np.max(np.abs(error))),
        "peakNormalizedRms": float(np.sqrt(np.mean(error**2)) / peak),
        "relativeL2": float(np.linalg.norm(error) / np.linalg.norm(truth[inverse])),
    }


first, second = (integrate(nodes, sphere) for nodes in quadratures)
core_sphere = integrate(quadratures[1], 463.1706981298117)
rounded_sphere = integrate(quadratures[1], reported_sphere)
dense_radius = np.linspace(radius[0], 1, 10001)
slope = np.gradient(wavefront(dense_radius, sphere), dense_radius)
stretch = wavelength * fnumber / (2 * pitch)
assert np.all(slope > 0)
receipt = {
    "frozenIndex": 945,
    "status": "ScalarFftContinuumDiagnosticOnly",
    "originalNativeFftResultUsable": False,
    "nativeRaySha256": ray_hash,
    "nativeWavefrontSha256": sha256(args.wavefront),
    "nativeFirstOrderReportSha256": sha256(args.first_order_report),
    "nativeReferenceSphereReportedMm": reported_sphere,
    "nativeReferenceSphereRecoveredMm": float(sphere),
    "referencePistonWaves": float(piston),
    "wavefrontFitNodeCount": int(fit_mask.sum()),
    "wavefrontHoldoutNodeCount": int((~fit_mask).sum()),
    "wavefrontFitDistinctRadii": int(np.unique(r2[fit_mask]).size),
    "wavefrontHoldoutDistinctRadii": int(distinct.size - np.unique(r2[fit_mask]).size),
    "wavefrontHoldoutRmsWaves": float(np.sqrt(np.mean(residual**2))),
    "wavefrontHoldoutMaxAbsWaves": float(np.max(np.abs(residual))),
    "wavefrontUsingRoundedNativeSphereMaxAbsWaves": float(np.max(np.abs(reference_delta))),
    "reportedWorkingFNumber": 2.01463,
    "workingFNumberFromMarginalRay": float(fnumber),
    "wavelengthMicrometers": wavelength,
    "observedPupilPhaseSlopeWavesPerUnitRadius": [float(slope.min()), float(slope.max())],
    "approximatePhaseGuardNominalSamplingAtFixedPitch": int(np.ceil(4 * slope.max() * stretch)),
    "unsampledCenterAmplitudeUpperBound": float(radius[0] ** 2),
    "imageAxesMicrometers": axis.tolist(),
    "besselCheckMaxAbs": float(bessel_check),
    "quadratureNodes": [int(len(nodes)) for nodes in quadratures],
    "betweenQuadratures": comparison(first, second),
    "coreSphereSensitivity": comparison(core_sphere, second),
    "roundedNativeSphereSensitivity": comparison(rounded_sphere, second),
    "centerAndPeakRelativeToIdeal": [float(second[0]), float(second.max())],
    "squaredPixelRadii": pixel_r2.tolist(),
    "intensityQuadrature1": first.tolist(),
    "intensityQuadrature2": second.tolist(),
}
args.output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
