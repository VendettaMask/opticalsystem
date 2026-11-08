import argparse
import hashlib
import json
from pathlib import Path
import runpy
import sys
from zipfile import ZipFile

import numpy as np
from scipy.special import eval_legendre, roots_legendre, spherical_jn


parser = argparse.ArgumentParser()
parser.add_argument("repo", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


argv = sys.argv
try:
    sys.argv = ["ToroidTriage.py", str(args.repo), str(args.output / "toroid-triage-replay.json")]
    model = runpy.run_path(str(Path(__file__).with_name("ToroidTriage.py")))
finally:
    sys.argv = argv

trace = model["trace"]
phase = model["phase"]
sphere = model["radius"]
wave = model["wavelength"]
f_number = model["F"]
rotation = model["local"][:2, :2]


def local_phase(x, y):
    shape = np.broadcast_shapes(x.shape, y.shape)
    p = np.column_stack((np.broadcast_to(x, shape).ravel(), np.broadcast_to(y, shape).ravel())) @ rotation.T
    return phase(*p.T, sphere).reshape(shape)


edge = np.eye(2) @ rotation.T
_, hit, _ = trace(*edge.T)
edge_miss = np.linalg.norm(hit[:, :2] - model["chief"][1][0, :2], axis=1)
chirp = local_phase(np.array([1.]), np.array([0.]))[0]
rng = np.random.default_rng(1365)
angle = rng.uniform(0, np.pi/2, 400)
radial = np.sqrt(rng.uniform(0, 1, 400))
sx, sy = radial*np.cos(angle), radial*np.sin(angle)
symmetry_error = max(float(np.max(abs(local_phase(sx, sy) - local_phase(i*sx, j*sy))))
                     for i, j in [(-1, 1), (1, -1), (-1, -1)])
assert symmetry_error < 1e-8

image_axis = np.arange(-63, 65)*.25
profile_half = wave*f_number*(32-2)/2
profile_axis = -profile_half + 2*profile_half*np.arange(201)/200
profile_size = 101
psf_bin_k = np.arange(129)*np.pi*.25/(np.sqrt(2)*wave*f_number)
profile_k = np.abs(profile_axis[100:])*np.pi/(np.sqrt(2)*wave*f_number)
psf_pair_indices = [(0, 0), (1, 0), (0, 8), (7, 3), (11, 25), (20, 20), (26, 3),
                    (32, 48), (53, 41), (65, 63), (88, 40), (96, 0), (116, 12), (128, 0), (0, 128)]


def quadrature(panels, order, carrier):
    nodes, weights = roots_legendre(order)
    half = (np.pi/4)/panels
    centers = (2*np.arange(panels)+1)*half
    theta = centers[:, None] + half*nodes
    if not carrier:
        return theta.ravel(), np.tile(half*weights, panels), 0
    phase_at_center = chirp*np.sin(centers)**2
    slope_cycles = chirp*np.sin(2*centers)
    linear_phase = phase_at_center[:, None] + (half*slope_cycles[:, None])*nodes
    argument = 2*np.pi*half*slope_cycles
    degree = np.arange(order)
    legendre = eval_legendre(degree[:, None], nodes)
    j = spherical_jn(degree[:, None], abs(argument)[None, :]).T
    moment = j*(1j*np.sign(argument[:, None]))**degree*(2*degree+1)
    modified_weights = half*np.exp(2j*np.pi*phase_at_center[:, None])*(moment @ legendre)*weights
    return theta.ravel(), modified_weights.ravel(), linear_phase.ravel()


def field(panels, order, weak_order, carrier, complete):
    theta, theta_weights, linear_phase = quadrature(panels, order, carrier)
    if complete:
        weak_frequencies = np.r_[psf_bin_k, profile_k]
        psf_y_index = np.arange(psf_bin_k.size)
    else:
        psf_y_index = np.unique([j for _, j in psf_pair_indices])
        weak_frequencies = np.r_[psf_bin_k[psf_y_index], profile_k]
    v, w = roots_legendre(weak_order)
    v, w = (v+1)/2, w/2
    x, cos_theta = np.sin(theta), np.cos(theta)
    weighted_marginal = np.empty((theta.size, weak_frequencies.size), dtype=complex)
    for start in range(0, theta.size, 64):
        rows = slice(start, start+64)
        weak_coord = cos_theta[rows, None]*v
        eikonal = local_phase(x[rows, None], weak_coord)
        if carrier:
            eikonal -= linear_phase[rows, None]
        values = np.exp(2j*np.pi*eikonal)*w
        weak_kernel = np.cos(weak_coord[:, :, None]*weak_frequencies)
        weighted_marginal[rows] = np.einsum("ij,ijk->ik", values, weak_kernel, optimize=False)
    weighted_marginal *= ((4/np.pi)*cos_theta**2*theta_weights)[:, None]
    profile = np.einsum("ij,ij->j", np.cos(x[:, None]*profile_k), weighted_marginal[:, -profile_size:])
    if complete:
        psf = np.cos(x[:, None]*psf_bin_k).T @ weighted_marginal[:, :psf_bin_k.size]
        grid_x, grid_y = np.meshgrid(np.arange(-63, 65), np.arange(-63, 65), indexing="xy")
        image = psf[abs(grid_x+grid_y), abs(grid_y-grid_x)]
        selected = np.array([psf[i, j] for i, j in psf_pair_indices])
    else:
        image = None
        selected = np.array([np.cos(x*psf_bin_k[i]) @ weighted_marginal[:, np.searchsorted(psf_y_index, j)]
                             for i, j in psf_pair_indices])
    return image, np.r_[profile[:0:-1], profile], selected


def measure(candidate, reference):
    diff = abs(candidate-reference)
    return {"points": int(diff.size), "maxAbs": float(diff.max()),
            "peakNormalizedRms": float(np.sqrt(np.mean(diff**2))/abs(reference).max())}


segmented_plans = [(144, 12, 160), (211, 16, 224), (288, 18, 288)]
direct_plans = [(768, 24, 224), (1024, 26, 288)]
segmented = []
direct = []
for plan in segmented_plans:
    print("segmented", plan, flush=True)
    segmented.append(field(*plan, carrier=True, complete=True))
for tier, plan in enumerate(direct_plans):
    print("direct control", plan, flush=True)
    direct.append(field(*plan, carrier=False, complete=tier == len(direct_plans)-1))


def intensity(values):
    return abs(values)**2


first, final = segmented[-2:]
comparison = {"initialFullImageIrradianceConvergence": measure(intensity(segmented[0][0]), intensity(first[0])),
              "fullImageComplexConvergence": measure(first[0], final[0]),
              "fullImageIrradianceConvergence": measure(intensity(first[0]), intensity(final[0])),
              "profileIrradianceConvergence": measure(intensity(first[1]), intensity(final[1])),
              "directProfileIrradianceConvergence": measure(intensity(direct[0][1]), intensity(direct[1][1])),
              "directSelectedComplexConvergence": measure(direct[0][2], direct[1][2]),
              "directVersusSegmentedFullImageComplex": measure(direct[1][0], final[0]),
              "directVersusSegmentedFullImageIrradiance": measure(intensity(direct[1][0]), intensity(final[0])),
              "directVersusSegmentedProfileComplex": measure(direct[1][1], final[1]),
              "directVersusSegmentedSelectedComplex": measure(direct[1][2], final[2]),
              "directVersusSegmentedProfileIrradiance": measure(intensity(direct[1][1]), intensity(final[1])),
              "directVersusSegmentedSelectedIrradiance": measure(intensity(direct[1][2]), intensity(final[2]))}
print(json.dumps(comparison, indent=2), flush=True)
assert all(check["peakNormalizedRms"] < 1e-8 for check in comparison.values())

native = {}
with ZipFile(model["archive"]) as archive:
    for index, filename in [(1365, "data.json"), (1369, "error.json")]:
        member = next(name for name in archive.namelist() if f"/{index}-" in name and name.endswith(f"/native/{filename}"))
        raw = archive.read(member)
        report = json.loads(raw)
        native[str(index)] = {"member": member, "sha256": hashlib.sha256(raw).hexdigest()}
        if index == 1365:
            assert report["messages"] and not any(g["values"] for g in report["dataGrids"])
            native[str(index)].update({"messages": report["messages"], "numericGridValues": 0})
        else:
            assert report["errorCode"] == "NativeInvalidResult" and "null data-series buffer" in report["error"]
            native[str(index)]["errorCode"] = report["errorCode"]

payload = args.output / "toroid-original-windows.npz"
np.savez_compressed(payload, image_axis_micrometers=image_axis, profile_axis_micrometers=profile_axis,
                    psf_field_by_tier=np.stack([stage[0] for stage in segmented]),
                    profile_field_by_tier=np.stack([stage[1] for stage in segmented]),
                    direct_full_image_field=direct[-1][0],
                    direct_profile_fields=np.stack([stage[1] for stage in direct]),
                    direct_selected_fields=np.stack([stage[2] for stage in direct]),
                    selected_rotated_bins=np.array(psf_pair_indices))
receipt = {
    "status": "ConditionalNearFocusFftOperatorOnly",
    "sourceSha256": model["record"]["actualSha256"],
    "nativeWavefrontSha256": hashlib.sha256(model["ref"]).hexdigest(),
    "nativeWavefrontMaximumErrorWaves": float(np.max(abs(model["native_error"]))),
    "originalNativeDiagnostics": native,
    "frozenRequestSha256": {str(i): model["request_members"][i]["sha256"] for i in (1365, 1369)},
    "referenceSphereMm": sphere, "wavelengthMicrometers": wave, "workingFNumbersXY": [f_number, f_number],
    "principalEdgeMissMm": edge_miss.tolist(), "principalEdgeMissWavelengths": (edge_miss*1000/wave).tolist(),
    "quarterPupilPhaseSymmetryMaxWaves": symmetry_error,
    "image": {"coordinates": list(final[0].shape), "pixelMicrometers": .25,
              "axisMicrometers": [float(image_axis[0]), float(image_axis[-1])],
              "idealNormalizedCenter": float(intensity(final[0][63, 63])),
              "idealNormalizedPeakWithinWindow": float(intensity(final[0]).max())},
    "profile": {"coordinates": int(profile_axis.size), "axisMicrometers": [float(profile_axis[0]), float(profile_axis[-1])],
                "idealNormalizedCenter": float(intensity(final[1][100])),
                "idealNormalizedPeakWithinWindow": float(intensity(final[1]).max())},
    "segmentedPlansPanelsOuterOrderWeakOrder": segmented_plans, "directPlansPanelsOuterOrderWeakOrder": direct_plans,
    "principalAxisCarrierEdgeWaves": float(chirp), "maximumPanelCarrierRemainderBoundWaves": [
        float(abs(chirp)*(np.pi/(4*plan[0]))**2) for plan in segmented_plans],
    "comparisons": comparison, "payloadSha256": digest(payload),
}
(args.output / "toroid-segmented-window.json").write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
