import argparse
import gzip
import hashlib
import json
from pathlib import Path
import struct

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import eval_legendre, roots_legendre, spherical_jn


parser = argparse.ArgumentParser()
parser.add_argument("rays", type=Path)
parser.add_argument("corpus", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


root = Path(__file__).parent
proof = root/"fft-945-radial-model.json"
psf = json.loads(proof.read_bytes())
assert sha(args.rays) == psf["nativeRaySha256"]
with gzip.open(args.rays, "rb") as f:
    count = struct.unpack("<i", f.read(4))[0]
    rays = np.frombuffer(f.read(), dtype="<f8").reshape((count, 6))
assert count == 8191*12+16384*8
stages = (rays[:8191*12], rays[8191*12:])
sphere = psf["nativeReferenceSphereRecoveredMm"]
wave = psf["wavelengthMicrometers"]
f_number = psf["workingFNumberFromMarginalRay"]
impact = (rays[:, 4]*rays[:, 5])**2
phase = (rays[:, 2]-rays[0, 2]+impact/(sphere+np.sqrt(sphere*sphere-impact)))/(wave*.001)
# Only the exact slope subtraction matters to the operator. Fit the small
# remainder from distinct sets of native rays and keep all other rays for checks.
slope = float((phase[-1]-phase[0])/(rays[-1, 0]-rays[0, 0]))
residual = phase-slope*rays[:, 0]
models = []
heldout = []
for start, stop, degree in [(0, len(stages[0]), 12), (len(stages[0]), len(rays), 16)]:
    mask = np.zeros(len(rays), dtype=bool)
    mask[start:stop:23] = True
    model = Chebyshev.fit(rays[mask, 0], residual[mask], degree, domain=[0, 1])
    error = float(np.max(abs(model(rays[~mask, 0])-residual[~mask])))
    assert error < 5e-9
    models.append(model)
    heldout.append({"fitNodes": int(mask.sum()), "heldoutNodes": int((~mask).sum()), "maxAbsWaves": error})
samples = np.linspace(0, 1, 131073)
between_fits = float(np.max(abs(models[0](samples)-models[1](samples))))
assert between_fits < 5e-9

case, = (args.corpus/"analyses").glob("0947-*")
native_path = case/"native/data.json"
settings = case/"native/captured-settings.json"
request = case/"workbench/request.json"
native = json.loads(native_path.read_bytes())
assert any("取样太低" in entry["Text"] for entry in native["messages"])
values = json.loads(request.read_bytes())["request"]
captured = json.loads(settings.read_bytes())["request"]
assert values == {key: captured[key] for key in values}
assert values["pupilSampling"] == 64 and values["configuration"] == values["field"] == values["wavelength"] == 1
assert not values["polarization"] and not values["useRayAiming"]
assert json.loads(values["apodization"])["numbers"] == {"type": 0, "factor": 0}
assert values["workbenchSettings"]["MaximumFrequency"] == "50"
original_series, = native["dataSeries"]
frequencies = np.asarray(original_series["x"])
assert frequencies.size == 300 and np.allclose(frequencies, np.linspace(0, 50, 300), rtol=0, atol=1e-14)
lag = 2*wave*f_number*frequencies*.001
ideal = 2/np.pi*(np.arccos(lag/2)-(lag/2)*np.sqrt(1-(lag/2)**2))


def angular_rule(panels, order):
    node, weights = roots_legendre(order)
    half = np.pi/(4*panels)
    centers = (2*np.arange(panels)+1)*half
    theta = centers[:, None]+half*node
    polynomial = eval_legendre(np.arange(order)[:, None], node)
    return theta, centers, node, weights, half, polynomial


def overlap(frequencies_index, model, plan, carrier):
    panels, angular_order, depth_order = plan
    theta, centers, node, weights, half, polynomial = angular_rule(panels, angular_order)
    t = np.cos(theta.ravel())
    nodes, depth_weights = roots_legendre(depth_order)
    nodes, depth_weights = (nodes+1)/2, depth_weights/2
    output = []
    ideal_geometry = []
    for index in frequencies_index:
        if index == 0:
            output.append(1.)
            ideal_geometry.append(1.)
            continue
        shift = lag[index]
        a = shift/2
        # Symmetric two-focus ellipse coordinates. For the first quadrant of
        # the common disk, r± = a(cosh h ± cos theta). The Jacobian is
        # a²(cosh² h − cos² theta) and h ends at r+ = 1.
        stop = np.arccosh((1-a*t)/a)
        radius = a*np.cosh(stop[:, None]*nodes)
        dt = (a*t)[:, None]
        weight = (radius*radius-dt*dt)*stop[:, None]*depth_weights
        small_phase = model(radius+dt)-model(radius-dt)
        if carrier:
            phase_center = slope*shift*np.cos(centers)
            local_slope = -slope*shift*np.sin(centers)
            linear = phase_center[:, None]+(half*local_slope[:, None])*node
            small_phase += (slope*shift*t-linear.ravel())[:, None]
            argument = 2*np.pi*half*local_slope
            degree = np.arange(angular_order)
            moment = spherical_jn(degree[:, None], abs(argument)[None, :]).T
            moment = moment*(1j*np.sign(argument[:, None]))**degree*(2*degree+1)
            angular_weights = half*np.exp(2j*np.pi*phase_center[:, None])*(moment@polynomial)*weights
            marginal = np.sum(np.exp(2j*np.pi*small_phase)*weight, axis=1)
            value = np.real(np.dot(angular_weights.ravel(), marginal))
        else:
            small_phase += (slope*shift*t)[:, None]
            marginal = np.sum(np.cos(2*np.pi*small_phase)*weight, axis=1)
            value = np.dot(np.tile(half*weights, panels), marginal)
        geometry = np.dot(np.tile(half*weights, panels), weight.sum(axis=1))
        output.append((4/np.pi)*value)
        ideal_geometry.append((4/np.pi)*geometry)
    return np.array(output), np.array(ideal_geometry)


segmented_plans = [(96, 12, 384), (144, 16, 448)]
direct_plans = [(320, 20, 384), (512, 22, 448)]
indices = np.arange(len(frequencies))
controls = np.array([0, 1, 2, 3, 7, 15, 27, 44, 63, 81, 100, 119, 139, 161, 186, 213, 242, 271, 299])
segmented, direct = [], []
for plan, model in zip(segmented_plans, models):
    print("segmented", plan, flush=True)
    segmented.append(overlap(indices, model, plan, True))
for plan, model in zip(direct_plans, models):
    print("direct", plan, flush=True)
    direct.append(overlap(controls, model, plan, False))


def metrics(first, second):
    diff = abs(first-second)
    return {"points": int(diff.size), "maxAbs": float(diff.max()),
            "relativeL2": float(np.linalg.norm(diff)/np.linalg.norm(second))}


first, last = (stage[0] for stage in segmented)
one, two = (stage[0] for stage in direct)
comparison = {"signedOtfBetweenSegmentedTiers": metrics(first[1:], last[1:]),
              "modulationBetweenSegmentedTiers": metrics(abs(first), abs(last)),
              "directControlOtfBetweenTiers": metrics(one[1:], two[1:]),
              "directControlOtfVersusSegmented": metrics(two[1:], last[controls[1:]]),
              "segmentedIdealGeometryVersusCircleOverlap": metrics(segmented[-1][1], ideal),
              "directIdealGeometryVersusCircleOverlap": metrics(direct[-1][1], ideal[controls])}
print(json.dumps(comparison, indent=2), flush=True)
assert max(error["maxAbs"] for error in comparison.values()) < 2e-8
assert max(error["relativeL2"] for error in comparison.values()) < 2e-7
assert np.all(abs(last) <= ideal+1e-12) and last[0] == 1
payload = args.output/"fft-947-mtf.npz"
np.savez_compressed(payload, frequency_cycles_per_mm=frequencies, normalized_pupil_shift=lag,
                    ideal_otf=ideal, actual_otf_by_tier=np.stack((first, last)),
                    actual_modulation=abs(last), direct_control_indices=controls,
                    direct_control_otf_by_tier=np.stack((one, two)))
receipt = {
    "status": "ConditionalScalarFftOperatorOnly", "frozenIndex": 947,
    "nativeRaySha256": sha(args.rays), "referencePsfReceiptSha256": sha(proof),
    "nearFocusApplicabilityReceiptSha256": sha(root/"fft-huygens-applicability.json"),
    "originalNativeSettingsSha256": sha(settings), "originalWorkbenchRequestSha256": sha(request),
    "originalNativeResultSha256": sha(native_path), "originalNativeMessages": native["messages"],
    "wavelengthMicrometers": wave, "workingFNumber": f_number, "referenceSphereMm": sphere,
    "radialCarrierWavesPerUnitPupil": slope, "nativePhaseFitHeldout": heldout,
    "maxDifferenceBetweenPhaseFitsWaves": between_fits,
    "segmentedPlansPanelsAngularOrderDepthOrder": segmented_plans,
    "directPlansPanelsAngularOrderDepthOrder": direct_plans,
    "frequencyRangeCyclesPerMm": [float(frequencies[0]), float(frequencies[-1])],
    "frequencyCountPerDirection": int(frequencies.size), "sameTangentialSagittalByRotationalSymmetry": True,
    "directControlCount": int(controls.size), "normalizedPupilShiftAtMaximum": float(lag[-1]),
    "actualModulationAt50": float(abs(last[-1])), "idealModulationAt50": float(ideal[-1]),
    "comparisons": comparison, "rawPayloadSha256": sha(payload),
}
(args.output/"fft-947-mtf-continuum.json").write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
