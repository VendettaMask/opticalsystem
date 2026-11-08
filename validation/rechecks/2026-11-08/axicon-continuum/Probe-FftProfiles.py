import argparse
import gzip
import hashlib
import json
from pathlib import Path
import struct

import numpy as np
from scipy.special import j0, j1, roots_legendre


parser = argparse.ArgumentParser()
parser.add_argument("rays", type=Path)
parser.add_argument("corpus", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


source = Path(__file__).parent
psf = json.loads((source/"fft-945-radial-model.json").read_bytes())
applicability = source/"fft-huygens-applicability.json"
assert sha(args.rays) == psf["nativeRaySha256"]
with gzip.open(args.rays, "rb") as f:
    count = struct.unpack("<i", f.read(4))[0]
    rays = np.frombuffer(f.read(), dtype="<f8").reshape((count, 6))
assert count == 8191*12+16384*8
stages = (rays[:8191*12], rays[8191*12:])


native = {}
options = {}
for index in (949, 952):
    case, = (args.corpus/"analyses").glob(f"{index:04d}-*")
    settings = case/"native/captured-settings.json"
    request = case/"workbench/request.json"
    values = json.loads(request.read_bytes())["request"]
    native_settings = json.loads(settings.read_bytes())
    captured = native_settings["request"]
    assert values == {key: captured[key] for key in values}
    assert values["pupilSampling"] == 32 and values["configuration"] == values["field"] == values["wavelength"] == 1
    assert not values["polarization"] and not values["useRayAiming"]
    assert json.loads(values["apodization"])["numbers"] == {"type": 0, "factor": 0}
    options[index] = values["workbenchSettings"]
    key = str(index)
    native[key] = {"case": case.name, "workbenchRequestSha256": sha(request), "nativeCapturedSettingsSha256": sha(settings)}
    if index == 949:
        path = case/"native/error.json"
        data = json.loads(path.read_bytes())
        assert data["errorCode"] == "NativeInvalidResult" and "null data-series buffer" in data["error"]
        native[key].update({"errorCode": data["errorCode"], "nativePayloadSha256": sha(path)})
    else:
        properties = native_settings["properties"]
        assert properties["UseHuygensPSF"] is False and properties["ShowDiffractionLimit"] is True
        assert properties["RadiusMaximum"] == 5 and properties["ReferTo"] == "Centroid"
        path = case/"native/data.json"
        data = json.loads(path.read_bytes())
        assert any("取样太低" in entry["Text"] for entry in data["messages"])
        assert not data["dataSeries"] and not data["dataGrids"]
        native[key].update({"messages": data["messages"], "nativePayloadSha256": sha(path)})


assert options[949]["Row"] == "中心" and options[949]["Type"] == "X-线性"
assert options[949]["GraphScaleMicrometers"] == "0" and options[949]["Normalized"] == "False"
assert options[952]["MaximumDistanceMicrometers"] == "5" and options[952]["NumPoints"] == "401"
assert options[952]["Reference"] == "centroid" and options[952]["Type"] == "encircled"
assert options[952].get("UseHuygens", "False") == "False"
wave = psf["wavelengthMicrometers"]
f_number = psf["workingFNumberFromMarginalRay"]
sphere = psf["nativeReferenceSphereRecoveredMm"]
assert wave == .55
k = np.pi/(wave*f_number)
half = wave*f_number*(32-2)/2
axis = np.arange(-100, 101)*half/100
radii = np.arange(401)*.0125
assert radii[-1] == 5
# The zeroth Hankel transform is unitary with measures u du and z dz.
# Field uses A(z) = 2 H_0[P](z), and |P(u)| = 1 inside the unit disk.
# 2*pi integral |A(k*r)|^2 r dr = 2*pi/k^2 * 4 * integral_0^1 u du.
full_energy = 4*np.pi/k**2


def fields(samples, radius):
    impact2 = (samples[:, 4]*samples[:, 5])**2
    path = samples[:, 2] - samples[0, 2] + impact2/(sphere+np.sqrt(sphere*sphere-impact2))
    weight = samples[:, 1]/samples[:, 1].sum()
    coefficient = np.column_stack((weight*np.cos(2*np.pi*path/(wave*.001)),
                                   weight*np.sin(2*np.pi*path/(wave*.001)), weight))
    result = np.empty((radius.size, 3))
    for first in range(0, radius.size, 32):
        block = slice(first, first+32)
        result[block] = j0((k*radius[block, None])*samples[:, 0]) @ coefficient
    return np.hypot(result[:, 0], result[:, 1])**2, result[:, 2]**2


def integrate(samples, order):
    profile, ideal_profile = fields(samples, axis[100:])
    nodes, weights = roots_legendre(order)
    half_step = .5*np.diff(radii)
    quadrature = .5*(radii[:-1]+radii[1:])[:, None] + half_step[:, None]*nodes
    actual, ideal = fields(samples, quadrature.ravel())
    measure = 2*np.pi/full_energy*half_step[:, None]*quadrature*weights

    def accumulate(y):
        return np.r_[0, np.cumsum(np.sum(measure*y.reshape(quadrature.shape), axis=1))]

    return np.r_[profile[:0:-1], profile], np.r_[ideal_profile[:0:-1], ideal_profile], accumulate(actual), accumulate(ideal)


tiers = (integrate(stages[0], 6), integrate(stages[1], 10))
z = k*radii
ideal = 1-j0(z)**2-j1(z)**2
ideal[0] = 0
ideal_peak_profile = np.ones(axis.size)
z_profile = k*axis
nonzero = z_profile != 0
ideal_peak_profile[nonzero] = (2*j1(z_profile[nonzero])/z_profile[nonzero])**2


def compare(a, b):
    d = np.abs(a-b)
    return {"coordinates": int(d.size), "maxAbs": float(d.max()),
            "nrmse": float(np.sqrt(np.mean(d**2))/np.max(np.abs(b))),
            "relativeL2": float(np.linalg.norm(d)/np.linalg.norm(b))}


controls = {"profileBetweenNativeTiers": compare(tiers[0][0], tiers[1][0]),
            "actualEnergyBetweenNativeTiers": compare(tiers[0][2], tiers[1][2]),
            "idealProfileVersusAiry": compare(tiers[1][1], ideal_peak_profile),
            "idealEnergyVersusBesselPrimitive": compare(tiers[1][3], ideal)}
assert max(value["nrmse"] for value in controls.values()) < 1e-6
assert min(np.min(np.diff(tier[2])) for tier in tiers) >= 0
assert min(np.min(np.diff(tier[3])) for tier in tiers) >= 0
assert abs(tiers[1][0][100]-psf["intensityQuadrature2"][0]) < 1e-18
previous_signal = json.loads(applicability.read_bytes())["commonIdealScaledIrradianceIntegralWithin5Micrometers"]["fft"]
scaled_signal_error = abs(tiers[1][2][-1]*full_energy-previous_signal)
assert scaled_signal_error < 1e-15

payload = args.output/"fft-949-952-axes.npz"
np.savez_compressed(payload, x_micrometers=axis, radius_micrometers=radii,
                    irradiance_by_tier=np.stack([tier[0] for tier in tiers]),
                    ideal_irradiance=ideal_peak_profile,
                    actual_energy_by_tier=np.stack([tier[2] for tier in tiers]),
                    ideal_energy_by_tier=np.stack([tier[3] for tier in tiers]), ideal_energy=ideal)
receipt = {
    "status": "ConditionalScalarFftOperatorOnly",
    "nativeRaySha256": sha(args.rays), "referencePsfReceiptSha256": sha(source/"fft-945-radial-model.json"),
    "nearFocusApplicabilityReceiptSha256": sha(applicability), "nativeOriginals": native,
    "wavelengthMicrometers": wave, "workingFNumber": f_number, "referenceSphereMm": sphere,
    "pupilRayCounts": [int(stage.shape[0]) for stage in stages], "radialBinQuadratureOrders": [6, 10],
    "profile": {"xQuantity": "ImageHeight", "xUnit": "Micrometer", "yQuantity": "Irradiance",
                "yUnit": "Dimensionless", "points": int(axis.size), "range": [float(axis[0]), float(axis[-1])],
                "centerIdealPeakNormalizedIrradiance": float(tiers[1][0][100]),
                "peakIdealPeakNormalizedIrradiance": float(tiers[1][0].max())},
    "energy": {"xQuantity": "ImageHeight", "xUnit": "Micrometer", "yQuantity": "Fraction",
               "yUnit": "Dimensionless", "pointsPerCurve": int(radii.size), "range": [0, 5], "stepMicrometers": .0125,
               "centroidMicrometers": [0, 0], "centroidBasis": "Exactly zero on every centered finite disk by rotational symmetry",
               "bothFullPlaneParsevalEnergySquareMicrometers": full_energy,
               "actualAt5Micrometers": float(tiers[1][2][-1]), "idealAt5Micrometers": float(ideal[-1]),
               "previousCommonIdealScaledIntegralMaxErrorSquareMicrometers": float(scaled_signal_error)},
    "comparisons": controls, "rawPayloadSha256": sha(payload),
}
(args.output/"fft-949-952-continuum.json").write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
print(json.dumps(receipt["comparisons"], indent=2))
