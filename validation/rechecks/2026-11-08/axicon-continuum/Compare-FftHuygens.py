import argparse
import gzip
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
from scipy.special import j0, roots_legendre


parser = argparse.ArgumentParser()
parser.add_argument("rays", type=Path)
parser.add_argument("fft_receipt", type=Path)
parser.add_argument("corpus", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


fft = json.loads(args.fft_receipt.read_bytes())
ray_hash = digest(args.rays)
assert ray_hash == fft["nativeRaySha256"] == "f10932ba1ddb438722f7af7add2402b86c7d7c7c27a8e3113449bc20261cdb41"
with gzip.open(args.rays, "rb") as file:
    count = struct.unpack("<i", file.read(4))[0]
    nodes = np.frombuffer(file.read(), dtype="<f8").reshape((count, 6))
assert count == 229364
stages = (nodes[:8191*12], nodes[8191*12:])

wave_um = fft["wavelengthMicrometers"]
wave_mm = wave_um*.001
radius = fft["nativeReferenceSphereRecoveredMm"]
marginal_sine = 1 / (2*fft["workingFNumberFromMarginalRay"])
axis = np.array(fft["imageAxesMicrometers"])
assert np.array_equal(axis, np.arange(-63,65)*.25)
grid_y, grid_x = np.meshgrid(np.arange(-63,65), np.arange(-63,65), indexing="ij")
r2, pixel_index = np.unique(grid_x**2+grid_y**2, return_inverse=True)
assert np.array_equal(r2, fft["squaredPixelRadii"])
radius_image = .25*np.sqrt(r2)
gl_r, gl_w = roots_legendre(128)
disk_radius = 2.5*(gl_r+1)
radii = np.concatenate((radius_image, disk_radius))


def shell_correction(samples):
    impact2 = (samples[:, 4]*samples[:, 5])**2
    return impact2/(radius+np.sqrt(radius**2-impact2))


def calculate(samples):
    weight = samples[:, 1]/samples[:, 1].sum()
    plane_opl = samples[:, 2]-samples[0, 2]
    phase = (2*np.pi/wave_mm)*(plane_opl[:, None] + np.stack(
        (shell_correction(samples), np.zeros_like(plane_opl)), axis=1))
    pupil = weight[:, None]*np.exp(1j*phase)
    angular_coordinates = (marginal_sine*samples[:, 0], samples[:, 3])
    fields = np.empty((2, radii.size, 2), dtype=complex)
    for model, coordinate in enumerate(angular_coordinates):
        for i in range(0, radii.size, 32):
            block = slice(i, min(i+32, radii.size))
            kernel = j0((2*np.pi/wave_um)*radii[block, None]*coordinate)
            fields[model, block] = kernel @ pupil.real + 1j*(kernel @ pupil.imag)
    # First subscript selects the transverse Fourier map. The last subscript
    # selects the exit sphere (0) or plane eikonal (1).
    return np.abs(fields)**2


first, second = [calculate(stage) for stage in stages]


def measure(candidate, truth):
    diff = abs(candidate-truth)
    return {
        "coordinates": int(diff.size),
        "maximumAbsolute": float(diff.max()),
        "peakNormalizedRms": float(np.sqrt(np.mean(diff**2))/truth.max()),
        "relativeL2": float(np.linalg.norm(diff)/np.linalg.norm(truth)),
    }


def grid(cases, mapping, phase):
    return cases[mapping, :r2.size, phase][pixel_index].reshape((128, 128))


fft_psf = grid(second, 0, 0)
huygens_psf = grid(second, 1, 1)
assert measure(fft_psf, np.array(fft["intensityQuadrature2"])[pixel_index].reshape((128,128)))["relativeL2"] < 1e-6
fft_convergence = measure(grid(first, 0, 0), fft_psf)
huygens_convergence = measure(grid(first, 1, 1), huygens_psf)
assert fft_convergence["relativeL2"] < 1e-6 and huygens_convergence["relativeL2"] < 1e-6
central = slice(47, 79)
assert np.array_equal(axis[central], np.arange(-16,16)*.25)
prior_path = args.fft_receipt.parent / "native-results.json"
prior = json.loads(prior_path.read_bytes())[-1]
assert prior["nodeCount"] == len(stages[1])
prior_line = np.array(next(row for row in prior["results"] if row["focus"] == 0)["line"])
prior_line_check = measure(huygens_psf[63,central], prior_line)
assert prior_line_check["relativeL2"] < 1e-8

difference_waves = shell_correction(stages[1])/wave_mm
w = stages[1][:, 1]/stages[1][:, 1].sum()
piston = np.dot(w,difference_waves)
angular_error = stages[1][:, 3]-marginal_sine*stages[1][:, 0]
phase_error = {
    "commonPistonWaves": float(piston),
    "peakToValleyWaves": float(np.ptp(difference_waves)),
    "pistonRemovedWeightedRmsWaves": float(np.sqrt(np.dot(w,(difference_waves-piston)**2))),
    "minimumAndMaximumWaves": [float(difference_waves.min()), float(difference_waves.max())],
    "scope": "Reference-transport phase difference. Rotational symmetry makes best-fit tilt zero.",
}
coordinate_error = {
    "measuredSineRange": [float(stages[1][:, 3].min()), float(stages[1][:, 3].max())],
    "sineErrorWeightedRms": float(np.sqrt(np.dot(w,angular_error**2))),
    "sineErrorMaximum": float(abs(angular_error).max()),
    "atHalfPupilMeasuredSine": float(stages[1][np.argmin(abs(stages[1][:,0]-.5)), 3]),
    "atHalfPupilLinearSine": float(.5*marginal_sine),
    "maximumKernelPhaseErrorAt16MicrometersWaves": float(16/wave_um*abs(angular_error).max()),
}

native = []
for row in (945, 947, 949, 952):
    case, = (args.corpus / "analyses").glob(f"{row:04d}-*")
    folder = case/"native"
    settings = json.loads((folder/"captured-settings.json").read_bytes())
    payload = folder/"data.json"
    if payload.exists():
        data = json.loads(payload.read_bytes())
        message = data["messages"]
        assert any("取样太低" in entry["Text"] for entry in message)
        series, grids = len(data["dataSeries"]), len(data["dataGrids"])
    else:
        payload = folder/"error.json"
        failure = json.loads(payload.read_bytes())
        message = failure["errorCode"]
        assert row == 949 and message == "NativeInvalidResult"
        series = grids = 0
    native.append({
        "frozenIndex": row, "capturedProperties": settings["properties"], "capturedSelectors": settings["selectors"],
        "configuration": settings["request"]["configuration"], "settingsSha256": digest(folder/"captured-settings.json"),
        "diagnosticPayloadSha256": digest(payload), "diagnostics": message,
        "returnedSeries": series, "returnedGrids": grids, "usableNumericReference": False,
    })


def relative_ideal_disk_signal(mapping, phase):
    # This is an irradiance integral in one common ideal normalization.
    # It is not a full-plane-normalized encircled-energy fraction.
    return float(5*np.pi*np.dot(gl_w, disk_radius*second[mapping, r2.size:, phase]))


payload = args.output / "fft-huygens-operator-grids.npz"
np.savez_compressed(payload, imageAxisMicrometers=axis, fft=fft_psf, huygens=huygens_psf,
                    spherePhaseWithNativeSine=grid(second, 1, 0),
                    planePhaseWithLinearSine=grid(second, 0, 1))
receipt = {
    "classification": "OriginalApplicabilityDiagnosticOnly",
    "nativeRaySha256": ray_hash, "fftModelSha256": digest(args.fft_receipt),
    "vendorDefinitions": {
        "fftPsf": "https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html",
        "fftMtf": "https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_MTF.html",
        "fftCrossSection": "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_Cross_Section.html",
        "fftEncircledEnergy": "https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html",
    },
    "rayImageDisplacementMillimeters": [float(nodes[:, 5].min()), float(nodes[:, 5].max())],
    "rayImageDisplacementWavelengths": [float(nodes[:, 5].min()/wave_mm), float(nodes[:, 5].max()/wave_mm)],
    "referencePhaseTransportDifference": phase_error,
    "transverseFourierMapDifference": coordinate_error,
    "fftTwoQuadratures": fft_convergence, "huygensTwoQuadratures": huygens_convergence,
    "huygensPreviousValidatedLineSha256": digest(prior_path),
    "huygensVersusPreviousValidatedLine": prior_line_check,
    "fullWindowFftVersusHuygens": measure(fft_psf, huygens_psf),
    "shared32WindowFftVersusHuygens": measure(fft_psf[central,central], huygens_psf[central,central]),
    "sharedCentralXLineFftVersusHuygens": measure(fft_psf[63,central], huygens_psf[63,central]),
    "centerAndSharedWindowPeak": {
        "fft": [float(fft_psf[63,63]), float(fft_psf[central,central].max())],
        "huygens": [float(huygens_psf[63,63]), float(huygens_psf[central,central].max())],
    },
    "spherePhaseWithNativeSineVersusFft": measure(grid(second, 1, 0), fft_psf),
    "planePhaseWithLinearSineVersusFft": measure(grid(second, 0, 1), fft_psf),
    "commonIdealScaledIrradianceIntegralWithin5Micrometers": {
        "fft": relative_ideal_disk_signal(0, 0), "huygens": relative_ideal_disk_signal(1, 1),
        "fullPlaneEnergyFraction": False,
    },
    "originalNativeDiagnostics": native,
    "rawGridPayloadSha256": digest(payload),
}
(args.output / "fft-huygens-applicability.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
