import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

import numpy as np


parser = argparse.ArgumentParser()
parser.add_argument("repo", type=Path)
parser.add_argument("rays", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
evidence = args.repo/"validation/rechecks/2026-11-08"
corpus = args.repo/"artifacts/zemax-sequential-corpus/20261021/full-v1"


def read(path):
    raw = path.read_bytes()
    return json.loads(raw), hashlib.sha256(raw).hexdigest()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prefix(path, marker, arguments):
    original = sys.argv
    namespace = {"__file__": str(path)}
    try:
        sys.argv = [str(path), *map(str, arguments)]
        content = path.read_text(encoding="utf-8-sig")
        assert content.count(marker) == 1
        exec(compile(content.split(marker)[0], str(path), "exec"), namespace)
    finally:
        sys.argv = original
    return namespace


def statistics(values, weights):
    order = np.argsort(values)
    q = np.interp([.05, .5, .95], np.cumsum(weights[order]), values[order])
    return {"p05": float(q[0]), "median": float(q[1]), "p95": float(q[2]),
            "max": float(values.max()), "rms": float(np.sqrt(weights@(values*values)))}


def tangent_basis(chief_direction):
    ex = np.eye(3)[0]
    ex -= (ex@chief_direction)*chief_direction
    ex /= np.linalg.norm(ex)
    ey = np.cross(chief_direction, ex)
    return np.column_stack((ex, ey))


def kernel_errors(q, model, basis, w, wavelength):
    fitted = basis @ np.linalg.lstsq(basis*np.sqrt(w[:, None]), q*np.sqrt(w[:, None]), rcond=None)[0]
    difference = np.linalg.norm(q-fitted, axis=1)
    nominal = np.linalg.norm(q-model, axis=1) if model is not None else None
    output = {"bestAffineMapResidualSine": statistics(difference, w),
              "bestAffineMapWorstAzimuthKernelErrorAt16MicrometersWaves": statistics(difference*16/wavelength, w)}
    if nominal is not None:
        output["requestMapResidualSine"] = statistics(nominal, w)
    return output


central_r = 2.**(-np.arange(3, 13))


def control(label, trace, wavelength, radius, receipt_sha, plan, weights=None):
    # Equal-area deterministic probes avoid oversampling the rim. Each tracer
    # supplies its physical mask and optional pupil power separately.
    radial, angular = plan
    r = np.sqrt((np.arange(radial)+.5)/radial)
    azimuth = (np.arange(angular)+.5)*2*np.pi/angular
    px = (r[:, None]*np.cos(azimuth)).ravel()
    py = (r[:, None]*np.sin(azimuth)).ravel()
    chief, direction, _ = trace(np.zeros(1), np.zeros(1))
    chief, direction = chief[0], direction[0]
    image, rays, valid = trace(px, py)
    u, v = px[valid], py[valid]
    w = np.ones(len(u)) if weights is None else weights(u, v)
    w /= w.sum()
    image, rays = image[valid], rays[valid]
    delta = image-chief
    offset_plane = delta-(delta@direction/(rays@direction))[:, None]*rays
    basis = tangent_basis(direction)
    q = rays@basis
    pupil = np.column_stack((np.ones(len(u)), u, v))
    impact = np.linalg.norm(np.cross(delta, rays), axis=1)
    assert np.max(impact) < radius
    sag_waves = (impact*impact/(radius+np.sqrt(radius*radius-impact*impact)))/(wavelength*.001)
    fitted = pupil @ np.linalg.lstsq(pupil*np.sqrt(w[:, None]), sag_waves*np.sqrt(w), rcond=None)[0]
    _, plus, valid_plus = trace(central_r, 0*central_r)
    _, minus, valid_minus = trace(-central_r, 0*central_r)
    assert np.all(valid_plus & valid_minus)
    gap = np.linalg.norm((plus-minus)@basis, axis=1)
    return {"case": label, "independentReceiptSha256": receipt_sha, "wavelengthMicrometers": wavelength,
            "acceptedAreaProbes": int(len(u)),
            "transverseMissInChiefNormalPlaneWaves": statistics(np.linalg.norm(offset_plane, axis=1)/(wavelength*.001), w),
            "transverseMissOnRequestedImageWaves": statistics(np.linalg.norm(delta[:, :2], axis=1)/(wavelength*.001), w),
            "transportSagAfterPistonTiltWaves": statistics(abs(sag_waves-fitted), w),
            "opposingCentralRaySineGap": gap.tolist(), **kernel_errors(q, None, pupil, w, wavelength)}


native = {}
for index in (945, 947, 949, 952):
    case, = (corpus/"analyses").glob(f"{index:04d}-*")
    root = case/"native"
    captured, captured_sha = read(root/"captured-settings.json")
    record = {"capturedSettingsSha256": captured_sha, "nativeProperties": captured["properties"],
              "nativeSelectors": captured["selectors"]}
    if index == 949:
        error, error_sha = read(root/"error.json")
        assert error["errorCode"] == "NativeInvalidResult"
        assert "null data-series buffer" in error["error"] and not (root/"data.txt").exists()
        record.update({"apiSha256": error_sha, "apiOutcome": error["type"], "errorCode": error["errorCode"],
                       "apiFirstLine": error["error"].splitlines()[0], "textExists": False})
    else:
        data, data_sha = read(root/"data.json")
        warning = "取样太低, 数据不准确!"
        assert data["messages"] == [{"ErrorCode": "Success", "Text": warning}]
        text_file = root/"data.txt"
        raw = text_file.read_bytes()
        text = raw.decode("utf-16" if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else "utf-8-sig")
        assert warning in text
        grid_shapes = [[g["nx"], g["ny"], len(g["values"])] for g in data["dataGrids"]]
        series_shapes = [[len(g["x"]), len(g["y"]), len(g["y"][0])] for g in data["dataSeries"]]
        if index == 947:
            assert series_shapes == [[300, 300, 2]] and not grid_shapes and text.strip() != warning
        else:
            assert not series_shapes and text.strip() == warning
            assert grid_shapes == ([[0, 0, 0]] if index == 945 else [])
        record.update({"apiSha256": data_sha, "textSha256": digest(text_file), "warning": warning,
                       "apiGridDescriptorShapesNxNyValueRows": grid_shapes,
                       "apiSeriesShapesXRowsYRowsChannels": series_shapes, "textIsWarningOnly": text.strip() == warning})
    native[str(index)] = record


psf, psf_sha = read(evidence/"axicon-continuum/fft-945-radial-model.json")
applicability, app_sha = read(evidence/"axicon-continuum/fft-huygens-applicability.json")
assert digest(args.rays) == psf["nativeRaySha256"]
with gzip.open(args.rays, "rb") as f:
    count = struct.unpack("<i", f.read(4))[0]
    samples = np.frombuffer(f.read(), dtype="<f8").reshape((count, 6))[8191*12:]
assert samples.shape == (131072, 6)
ax_r, ax_q = samples[:, 0], samples[:, 3]
ax_w = samples[:, 1]/samples[:, 1].sum()
wave = psf["wavelengthMicrometers"]
f_number = psf["workingFNumberFromMarginalRay"]
sphere = psf["nativeReferenceSphereRecoveredMm"]
affine_scale = np.dot(ax_w*ax_r, ax_q)/np.dot(ax_w, ax_r**2)
ax_error = abs(ax_q-affine_scale*ax_r)
impact = (samples[:, 4]*samples[:, 5])**2
transport = impact/(sphere+np.sqrt(sphere*sphere-impact))/(wave*.001)
source = corpus/"sources/Miscellaneous/Axicon model.zmx"
identity, _ = read(evidence/"axicon-continuum/identity.json")
assert digest(source) == identity["sourceLensSha256"]
raw = source.read_bytes()
head, *surfaces = re.split(r"(?m)^SURF \d+\s*$", raw.decode("utf-16" if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else "utf-8-sig"))


def number(section, key):
    return float(re.search(rf"(?m)^\s*{key}\s+(\S+)", section).group(1))


curvature = number(surfaces[3], "CURV")
conic = number(surfaces[3], "CONI")
assert curvature > 0 and conic < -1 and "GLAS BK7" in surfaces[1]
tip_radius = 1/(curvature*np.sqrt(-1-conic))
index = np.sqrt(1 + np.sum(np.array([1.03961212, .231792344, 1.01046945])*wave**2 /
                           (wave**2-np.array([.00600069867, .0200179144, 103.560653]))))
reduced_slope = -(index-1)*number(surfaces[1], "CURV")
gain_at_second = 1+number(surfaces[1], "DISZ")*reduced_slope/index
reduced_slope -= (1-index)*number(surfaces[2], "CURV")*gain_at_second
gain_at_third = gain_at_second+number(surfaces[2], "DISZ")*reduced_slope
tip_pupil_radius = tip_radius/(gain_at_third*number(head, "ENPD")/2)
axicon = {"case": "Axicon", "sourcePsfReceiptSha256": psf_sha, "pairedOperatorReceiptSha256": app_sha,
          "nativeRaySha256": digest(args.rays), "wavelengthMicrometers": wave,
          "transverseMissInChiefNormalPlaneWaves": statistics(samples[:, 5]/(wave*.001), ax_w),
          "requestMapResidualSine": statistics(abs(ax_q-ax_r/(2*f_number)), ax_w),
          "bestAffineMapResidualSine": statistics(ax_error, ax_w),
          "bestAffineMapWorstAzimuthKernelErrorAt16MicrometersWaves": statistics(ax_error*16/wave, ax_w),
          "bestAffineMapSinePerUnitPupil": float(affine_scale),
          "transportSagAfterPistonTiltWaves": statistics(abs(transport-ax_w@transport), ax_w),
          "opposingCentralRaySineGap": (2*np.interp(central_r, ax_r, ax_q)).tolist(),
          "smallestSampledNormalizedRadius": float(ax_r.min()),
          "smallestSampledRadiusDirectionSine": float(ax_q[np.argmin(ax_r)]),
          "roundedConicTip": {"sourceLensSha256": digest(source), "surface": 3,
                              "curvaturePerMillimeter": curvature, "conic": conic,
                              "surfaceSlopeTransitionRadiusMillimeters": float(tip_radius),
                              "paraxialFootprintGainBeforeSurface": float(gain_at_third),
                              "paraxialEstimateOfNormalizedLaunchTransitionRadius": float(tip_pupil_radius),
                              "sampledPlateauIsMathematicalDiscontinuity": False}}


tw_dir = evidence/"twyman-streamed-fourier"
tw, tw_sha = read(tw_dir/"2161-independent-receipt.json")
tw_model = prefix(tw_dir/"Probe-TwymanAxis.py", "\nchief, chief_dir, chief_path, _ = trace", [
    args.repo/"artifacts/validation/hf1107/full/models/070-c1.optic.json", corpus,
    args.repo/"validation/rechecks/2026-11-07/fft-psf-native-window/fixed-window-references.zip", args.output])


def twyman(x, y):
    image, direction, _, stops = tw_model["trace"](x, y)
    return image, direction, np.logical_and.reduce([np.ones(x.size, dtype=bool), *[r <= 1 for r in stops]])


scan_dir = evidence/"scanning-mirror-folded-fft"
scan, scan_sha = read(scan_dir/"verification.json")
scan1, scan1_sha = read(scan_dir/"configuration-one-control.json")
scan_model = prefix(scan_dir/"Probe-FoldedMirror.py", "\nrows = [run", [corpus, args.output])


def scan_trace(tilt):
    def evaluate(x, y):
        image, direction, _, _ = scan_model["trace"](x, y, tilt)
        return image, direction, np.ones(x.size, dtype=bool)
    return evaluate


spec_dir = evidence/"spectrometer-fft-psf-reference"
spec, spec_sha = read(spec_dir/"verification.json")
spec_model = prefix(spec_dir/"SpectrometerReference.py", "\nnative_root=", [args.repo, args.output/"unused.json"])


def spectrometer(x, y):
    image, direction, _, _, accepted = spec_model["trace"](x, y)[-1]
    return image, direction, accepted


toroid_dir = evidence/"toroid-fft-phase-domain"
toroid, toroid_sha = read(toroid_dir/"triage.json")
toroid_model = prefix(toroid_dir/"ToroidTriage.py", "\narchive=", [args.repo, args.output/"unused.json"])


def trace_toroid(x, y):
    direction, image, _ = toroid_model["trace"](x, y)
    return image, direction, np.ones(x.size, dtype=bool)


control_cases = [("Twyman Green #2157", twyman, tw_model["wave"], tw["virtualSphereRadiusMm"], tw_sha),
                 ("Scanning mirror #2875", scan_trace(scan1["mirrorTiltDegrees"]), scan_model["wavelength"],
                  scan1["fitSphereRadiusMm"], scan1_sha)]
for item in scan:
    control_cases.append((f"Scanning mirror #{item['frozenIndex']}", scan_trace(item["mirrorTiltDegrees"]),
                          scan_model["wavelength"], item["fitSphereRadiusMm"], scan_sha))
control_cases.extend([("Sample Spectrometer #443", spectrometer, spec_model["wave"], spec["referenceSphereRadiusMm"], spec_sha),
                      ("Toroid #1365 conditional only", trace_toroid, toroid_model["wavelength"], toroid["referenceSphereMm"], toroid_sha)])
plans = [(48, 128), (96, 256)]
lo, controls = [
    [control(*case, plan, (lambda x, y: (1+.25*(x*x+y*y))**-1.5) if i == 0 else None)
     for i, case in enumerate(control_cases)] for plan in plans]


result = {"nativeOriginalDiagnostics": native, "centralProbeNormalizedRadii": central_r.tolist(),
          "axicon": axicon, "controlProbePlansRadialAndAzimuthal": plans,
          "controlsCoarse": lo, "controls": controls}
(args.output/"fft-near-focus-guard-evidence.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
for item in [axicon, *controls]:
    print(item["case"], item["transverseMissInChiefNormalPlaneWaves"], item["transportSagAfterPistonTiltWaves"]["rms"],
          item["bestAffineMapResidualSine"]["rms"], item["opposingCentralRaySineGap"][-1])
