import argparse
import hashlib
import json
import math
import re
from pathlib import Path

import numpy as np
from numpy.polynomial.legendre import leggauss
from scipy.optimize import least_squares
from scipy.special import j0, j1


parser = argparse.ArgumentParser()
parser.add_argument("corpus", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--native-control", type=Path)
parser.add_argument("--formal-axis-root", type=Path)
parser.add_argument("--encircled-energy", action="store_true")
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)

source = args.corpus / "sources/Zoom systems/Scanning mirror.zmx"
source_bytes = source.read_bytes()
source_hash = hashlib.sha256(source_bytes).hexdigest()
assert source_hash == "ad2b7a0922cf0dbc29d560a8507ce38254b0862c3f0d94329d91d22b48d30af6"
prescription = source_bytes.decode("utf-16" if source_bytes.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
sections = re.split(r"(?m)^SURF\s+(\d+)\s*$", prescription)
surfaces = dict(zip(map(int, sections[1::2]), sections[2::2]))


def surface_value(surface, key):
    return float(re.search(rf"(?m)^\s+{key}\s+(\S+)", surfaces[surface]).group(1))


launch_radius = float(re.search(r"(?m)^ENPD\s+(\S+)", prescription).group(1)) / 2
mirror_vertex = surface_value(1, "DISZ")
first_vertex = surface_value(6, "DISZ")
second_vertex = first_vertex + surface_value(7, "DISZ")
image_vertex = second_vertex + surface_value(8, "DISZ")
lens = [(first_vertex, 1 / surface_value(7, "CURV")), (second_vertex, 1 / surface_value(8, "CURV"))]
wavelength = .55
# SF1 Sellmeier coefficients in the archived original prescription's glass data.
b = np.array([1.55912923, .284246288, .968842926])
c = np.array([.0121481001, .0534549042, 112.174809])
glass = np.sqrt(1 + np.sum(b * wavelength**2 / (wavelength**2 - c)))
axis = np.arange(-63, 65) * .25


def native(row, name="data.json"):
    case, = (args.corpus / "analyses").glob(f"{row:04d}-*")
    path = case / "native" / name
    data = path.read_bytes()
    if name == "data.json":
        value = json.loads(data)
        assert not value["messages"]
    else:
        value = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    return value, hashlib.sha256(data).hexdigest()


def dot(a, b):
    return np.sum(a * b, axis=-1)


def refract(direction, normal, incident, transmitted):
    normal = np.where((dot(direction, normal) > 0)[..., None], -normal, normal)
    cosine = -dot(direction, normal)
    eta = incident / transmitted
    return eta * direction + (eta * cosine - np.sqrt(1 - eta**2 * (1 - cosine**2)))[..., None] * normal


def trace(px, py, mirror_angle):
    px, py = np.broadcast_arrays(px, py)
    theta = np.deg2rad(mirror_angle)
    mirror_z = mirror_vertex + launch_radius * py * np.tan(theta)
    # The nominal folded frame has local axes world X, -Z and +Y.
    point = np.stack((launch_radius * px, mirror_vertex - mirror_z, launch_radius * py), axis=-1)
    direction = np.broadcast_to([0, np.cos(2 * theta), np.sin(2 * theta)], point.shape).copy()
    path = mirror_z.copy()
    hit_radii = [np.hypot(launch_radius * px, launch_radius * py / np.cos(theta))]
    for (vertex, radius), (incident, transmitted) in zip(lens, ((1, glass), (glass, 1))):
        center = np.array([0, 0, vertex + radius])
        delta = point - center
        h = dot(delta, direction)
        t = -h + np.sqrt(h*h - dot(delta, delta) + radius**2)
        hit = point + t[..., None] * direction
        direction = refract(direction, (hit-center) / radius, incident, transmitted)
        path += incident * t
        point = hit
        hit_radii.append(np.linalg.norm(hit[..., :2], axis=-1))
    t = (image_vertex - point[..., 2]) / direction[..., 2]
    return point + t[..., None] * direction, direction, path + t, hit_radii


def image_metrics(candidate, reference):
    diff = candidate - reference
    return {
        "coordinates": int(diff.size),
        "maximumAbsolute": float(np.max(abs(diff))),
        "peakNormalizedRms": float(np.sqrt(np.mean(diff**2)) / reference.max()),
        "relativeL2": float(np.linalg.norm(diff) / np.linalg.norm(reference)),
    }


def asymmetric_energy(phase, sphere, sagittal_f, tangential_f, row):
    radii = np.arange(401) * (5/400)
    kx = np.pi / (wavelength*sagittal_f)
    ky = np.pi / (wavelength*tangential_f)
    parseval = 4*np.pi / (kx*ky)

    azimuth = np.arange(2048)*2*np.pi/2048
    cx, cy = np.cos(azimuth), np.sin(azimuth)
    rim_opd = phase(cx, cy, sphere)
    rim_gradient = 2*np.array([np.mean(rim_opd*cx), np.mean(rim_opd*cy)])
    scale = -2*wavelength*np.array([sagittal_f, tangential_f])
    centroid = scale*rim_gradient
    assert abs(centroid[0]) < 1e-8
    centroid[0] = 0

    def disk(order, swapped):
        roots, weights = leggauss(order)
        angles = roots*np.pi/2
        chord = np.cos(angles)
        varying = chord[:, None]*roots[None, :]
        fixed = np.sin(angles)[:, None]
        px, py = (fixed, varying) if swapped else (varying, fixed)
        px, py = np.broadcast_arrays(px, py)
        weights = (weights*chord**2)[:, None]*weights[None, :]/2
        return px, py, weights

    px, py, weights = disk(96, False)
    delta = 2e-5

    def derivative(horizontal):
        def shift(q):
            return phase(px+q if horizontal else px, py if horizontal else py+q, sphere)
        return (shift(-2*delta)-8*shift(-delta)+8*shift(delta)-shift(2*delta))/(12*delta)

    interior_centroid = scale*np.array([np.sum(weights*derivative(True)), np.sum(weights*derivative(False))])
    centroid_error = float(np.max(abs(interior_centroid-centroid)))
    assert centroid_error < 1e-6

    def integrate(order, degree, angular_order, swapped, ideal):
        px, py, weights = disk(order, swapped)
        if ideal:
            pupil = weights.astype(np.complex128)
        else:
            pupil = weights*np.exp(-2j*np.pi*phase(px, py, sphere) - 1j*ky*centroid[1]*py)
        powers = np.arange(degree+1)[:, None]
        x_power = px.ravel()[None, :]**powers
        y_power = py.ravel()[None, :]**powers
        moments = (x_power*pupil.ravel()) @ y_power.T
        angles = np.arange(angular_order)*2*np.pi/angular_order
        x_direction = 5*kx*np.cos(angles)
        y_direction = 5*ky*np.sin(angles)
        coefficients = np.zeros((angular_order, degree+1), dtype=np.complex128)
        for d in range(degree+1):
            for a in range(d+1):
                b = d-a
                coefficients[:, d] += math.comb(d, a)*x_direction**a*y_direction**b*moments[a, b]
            coefficients[:, d] *= (-1j)**d/math.factorial(d)
        # Each intensity coefficient is a trigonometric polynomial of degree
        # at most 2*degree, so the uniform angular sum is exact at this order.
        assert angular_order > 2*degree
        intensity = np.mean([np.convolve(c, c.conj()).real for c in coefficients], axis=0)
        p = np.arange(intensity.size)+2
        return (2*np.pi*25/parseval)*(radii[:, None]/5)**p @ (intensity/p)

    settings = [(256, 26, 64, False), (320, 30, 80, True)]
    coarse, fine = [(integrate(*setting, False), integrate(*setting, True)) for setting in settings]
    roots, weights = leggauss(48)
    radial = .5*radii[:, None]*(roots+1)
    angle = np.arange(80)*2*np.pi/80
    q = radial[..., None]*np.hypot(kx*np.cos(angle), ky*np.sin(angle))
    jinc = np.divide(2*j1(q), q, out=np.ones_like(q), where=q != 0)
    analytical = (2*np.pi/parseval)*np.sum(
        np.mean(jinc**2, axis=2)*radial*.5*radii[:, None]*weights, axis=1)
    np.savez_compressed(args.output / f"{row}-independent.npz", radius=radii, actual=fine[0], ideal=fine[1],
                        actualLower=coarse[0], idealLower=coarse[1], analyticalIdeal=analytical)
    return {
        "frozenIndex": row, "radiusCount": 401, "radiusRangeMicrometers": [0, 5],
        "actualCentroidMicrometers": centroid.tolist(), "idealCentroidMicrometers": [0, 0],
        "centroidInteriorGradientMicrometers": interior_centroid.tolist(),
        "centroidAgreementMaximumMicrometers": centroid_error,
        "encircledEnergyWorkingFNumbersTS": [float(tangential_f), float(sagittal_f)],
        "parsevalTotalPowerSquareMicrometers": float(parseval),
        "quadratureOrdersPupilTaylorAndAzimuth": [list(item[:3]) for item in settings],
        "fourierFieldTaylorRemainderBounds": [(5*max(kx, ky))**(m+1)/math.factorial(m+1)
                                               for _, m, _, _ in settings],
        "actualBetweenQuadratures": image_metrics(coarse[0], fine[0]),
        "idealBetweenQuadratures": image_metrics(coarse[1], fine[1]),
        "idealVersusEllipticalJinc": image_metrics(fine[1], analytical),
        "actualEnergyAt5Micrometers": float(fine[0][-1]),
        "idealEnergyAt5Micrometers": float(fine[1][-1]),
    }


def run(first_order_row, scan_angle):
    # Configuration rotates the nominal -45 degree fold by the raw PRAM angle.
    tilt = -45 + scan_angle
    chief, chief_dir, chief_path, _ = trace(0., 0., tilt)
    first_order, first_order_hash = native(first_order_row, "data.txt")
    native_working = float(re.search(r"工作F/#\s*:\s*(\S+)", first_order).group(1))
    fan, fan_hash = native(first_order_row + 3)
    fan_checks = []
    for direction, series in enumerate(fan["dataSeries"]):
        p = np.array(series["x"])
        axis_index = 1 - direction
        point, _, _, _ = trace(0*p if direction == 0 else p, p if direction == 0 else 0*p, tilt)
        reference = np.asarray(series["y"])[:, 0]
        values = (point[:, axis_index] - chief[axis_index]) * 1000
        fan_checks.append({
            "coordinates": len(p),
            "nativeMicrometerRange": [float(reference.min()), float(reference.max())],
            "maxAbsoluteMicrometers": float(np.max(abs(reference - values))),
        })

    report, ray_hash = native(first_order_row + 1, "data.txt")
    ray = [float(value) for value in re.search(r"(?m)^\s*9\t([^\r\n]+)", report).group(1).split()[:6]]
    position, direction, _, _ = trace(0., .8, tilt)
    # The native report uses a signed path direction after the reflection.
    single_position_error = np.max(abs(position - np.array([0, 0, image_vertex]) - ray[:3]))
    single_direction_error = np.max(abs(-direction - ray[3:6]))

    def phase(px, py, sphere):
        point, direction, path, _ = trace(px, py, tilt)
        offset = point - chief
        longitudinal = dot(direction, offset)
        plane = path - chief_path - longitudinal
        impact2 = dot(offset, offset) - longitudinal**2
        return -(plane + impact2 / (sphere + np.sqrt(sphere**2 - impact2))) / (wavelength * .001)

    wavefront, wavefront_hash = native(first_order_row + 5)
    values = np.asarray(wavefront["dataGrids"][0]["values"], dtype=float)
    valid = np.isfinite(values)
    y, x = np.indices(values.shape)
    ix = x[valid] - 32
    iy = y[valid] - 32
    target = values[valid]
    # Fit on a subset of distinct meridional/sagittal positions. Equivalent
    # ±sagittal coordinates stay in the same subset.
    fit = (abs(ix) + 2*iy) % 3 == 0
    px, py = ix / 31, iy / 31
    parameters = least_squares(
        lambda p: phase(px[fit], py[fit]*p[2], p[0]) + p[1] - target[fit],
        x0=[229, 0, 1], bounds=([200, -100, .9], [300, 100, 1.1]),
    ).x
    sphere, piston, tangential_stretch = parameters
    holdout = phase(px[~fit], py[~fit]*tangential_stretch, sphere) + piston - target[~fit]

    _, cone, _, _ = trace(np.array([0, 0, 1, -1]), np.array([1, -1, 0, 0]), tilt)
    sine = np.linalg.norm(np.cross(chief_dir, cone), axis=1)
    number_t, number_s = 1 / (2*np.sqrt(np.mean(sine.reshape(2, 2)**2, axis=1)))
    def integrate(order, swapped, x_axis, y_axis):
        kx = np.pi * x_axis / (wavelength * number_s)
        ky = np.pi * y_axis / (wavelength * number_t)
        roots, weights = leggauss(order)
        angles = roots * np.pi / 2
        chord = np.cos(angles)
        varying = chord[:, None] * roots[None, :]
        fixed = np.sin(angles)[:, None]
        rx, ry = (fixed, varying) if swapped else (varying, fixed)
        field = np.exp(-2j * np.pi * phase(rx, ry, sphere))
        spectral = kx if swapped else ky
        line_spectral = ky if swapped else kx
        inner = np.empty((order, line_spectral.size), dtype=complex)
        for i in range(order):
            inner[i] = np.exp(-1j * line_spectral[:, None] * varying[i]) @ (field[i] * weights)
        amplitude = np.exp(-1j * spectral[:, None] * fixed[:, 0]) @ (inner * (weights * chord**2)[:, None])
        return np.abs((amplitude.T if swapped else amplitude)/2) ** 2

    # The second disk quadrature integrates the orthogonal physical chord
    # first as well as using a different count.
    first = integrate(256, False, axis, axis)
    second = integrate(320, True, axis, axis)
    row = first_order_row + 6
    np.savez_compressed(args.output / f"{row}-independent.npz", axis=axis, first=first, second=second)

    az = np.linspace(0, 2*np.pi, 16385)[:-1]
    _, _, _, apertures = trace(np.cos(az), np.sin(az), tilt)
    peak_y, peak_x = np.unravel_index(second.argmax(), second.shape)
    receipt = {
        "frozenIndex": row,
        "mirrorTiltDegrees": tilt,
        "nativeSourceSha256": source_hash,
        "nativeFanSha256": fan_hash,
        "nativeFirstOrderReportSha256": first_order_hash,
        "nativeRayReportSha256": ray_hash,
        "nativeWavefrontSha256": wavefront_hash,
        "chiefImageLocalXYMm": chief[:2].tolist(),
        "chiefAngleToImageNormalDegrees": float(np.degrees(np.arccos(abs(chief_dir[2])))),
        "nativeRayFanChecks": fan_checks,
        "nativeSingleRayPositionMaxMm": float(single_position_error),
        "nativeSingleRayDirectionMax": float(single_direction_error),
        "pupilBoundaryMaximumHitRadiusMm": [float(np.max(hit)) for hit in apertures],
        "fitSphereRadiusMm": float(sphere),
        "fitTangentialPupilStretch": float(tangential_stretch),
        "wavefrontFitNodes": int(fit.sum()),
        "wavefrontHoldoutNodes": int((~fit).sum()),
        "wavefrontHoldoutMaxAbsWaves": float(np.max(abs(holdout))),
        "workingFNumbersFromMarginalRaysTS": [float(number_t), float(number_s)],
        "reportedCombinedWorkingFNumber": native_working,
        "quadratureOrders": [256, 320],
        "quadratureComparison": image_metrics(first, second),
        "centerRelativeToIdeal": float(second[63, 63]),
        "peakRelativeToIdeal": float(second.max()),
        "peakPhysicalCoordinatesMicrometers": [float(axis[peak_x]), float(axis[peak_y])],
        "status": "ScalarFftContinuumDiagnosticOnly",
    }
    if scan_angle and args.formal_axis_root:
        section_row = row + 4
        # Original automatic scale uses a 32-point pupil, 64-point padded
        # transform, unit launch stretch, and the faster working direction.
        half_range = wavelength * min(number_s, number_t) * (32 - 2) / 2
        auto_axis = -half_range + 2*half_range*np.arange(201) / 200
        formal_path = args.formal_axis_root / f"{section_row}-axis-formal/data.json"
        if formal_path.exists():
            formal = json.loads(formal_path.read_bytes())
            if formal["outcome"] == "Success":
                plot, = formal["plotSeries"]
                actual_axis = np.array([p["x"] for p in plot["points"]])
                assert actual_axis.shape == auto_axis.shape
            else:
                actual_axis = auto_axis
        else:
            actual_axis = auto_axis
        axis_error = float(np.max(abs(actual_axis - auto_axis)))
        assert axis_error <= 1e-10
        section_1 = integrate(256, False, actual_axis, np.array([0.]))[0]
        section_2 = integrate(320, True, actual_axis, np.array([0.]))[0]
        np.savez_compressed(args.output / f"{section_row}-independent.npz",
                            axis=actual_axis, first=section_1, second=section_2)
        receipt["centralXSection"] = {
            "frozenIndex": section_row,
            "automaticHalfRangeMicrometers": float(half_range),
            "actualAxisMaximumDifferenceMicrometers": axis_error,
            "quadratureComparison": image_metrics(section_1, section_2),
        }
    if scan_angle == 0 and args.encircled_energy:
        assert abs(number_t - number_s) <= 1e-10 and np.linalg.norm(chief[:2]) <= 1e-10
        radial = np.linspace(0, 1, 1025)
        assert np.max(abs(phase(radial, 0*radial, sphere) - phase(0*radial, radial, sphere))) <= 1e-8
        radii = np.arange(401) * (5/400)
        k = np.pi / (wavelength * number_s)
        # For E = integral(P exp(-i k rho dot pupil)) / pi, Parseval gives
        # total intensity 4 pi / k² for this unit-amplitude full circular pupil.
        total = 4*np.pi/k**2

        def energy(pupil_order, image_order, along_y):
            pupil_x, pupil_w = leggauss(pupil_order)
            image_x, image_w = leggauss(image_order)
            pupil_r = .5*(pupil_x+1)
            pupil_weight = pupil_r*pupil_w
            field = phase(0*pupil_r, pupil_r, sphere) if along_y else phase(pupil_r, 0*pupil_r, sphere)
            pupil_weighted_field = np.exp(-2j*np.pi*field)*pupil_weight
            image_r = .5*radii[:, None]*(image_x+1)
            kernel = j0(k*image_r[..., None]*pupil_r)
            actual = kernel @ pupil_weighted_field
            ideal = kernel @ pupil_weight
            image_weight = (2*np.pi/total)*image_r*.5*radii[:, None]*image_w
            return np.sum(abs(actual)**2*image_weight, axis=1), np.sum(ideal**2*image_weight, axis=1)

        coarse, fine = energy(96, 48, False), energy(144, 72, True)
        analytical = 1-j0(k*radii)**2-j1(k*radii)**2
        np.savez_compressed(args.output / "2882-independent.npz", radius=radii, actual=fine[0], ideal=fine[1],
                            actualLower=coarse[0], idealLower=coarse[1], analyticalIdeal=analytical)
        receipt["encircledEnergy"] = {
            "frozenIndex": 2882, "radiusCount": 401, "radiusRangeMicrometers": [0, 5],
            "actualCentroidMicrometers": [0, 0], "idealCentroidMicrometers": [0, 0],
            "centroidReason": "Axially symmetric pupil phase and amplitude on the folded axis",
            "parsevalTotalPowerSquareMicrometers": float(total),
            "quadratureOrdersPupilAndImage": [[96, 48], [144, 72]],
            "actualBetweenQuadratures": image_metrics(coarse[0], fine[0]),
            "idealBetweenQuadratures": image_metrics(coarse[1], fine[1]),
            "idealVersusAiry": image_metrics(fine[1], analytical),
            "actualEnergyAt5Micrometers": float(fine[0][-1]),
            "idealEnergyAt5Micrometers": float(fine[1][-1]),
        }
    if scan_angle and args.encircled_energy:
        mean_t, mean_s = .5 / np.mean(sine.reshape(2, 2), axis=1)
        receipt["encircledEnergy"] = asymmetric_energy(phase, sphere, mean_s, mean_t, row+7)
    return receipt


rows = [run(2891, -10), run(2913, 10)]
(args.output / "reference-receipt.json").write_text(json.dumps(rows, indent=2) + "\n", encoding="utf-8")
if args.native_control or args.encircled_energy:
    control = run(2869, 0)
    if args.encircled_energy:
        (args.output / "2882-independent-receipt.json").write_text(json.dumps(control, indent=2) + "\n", encoding="utf-8")
        for case in rows:
            row = case["encircledEnergy"]["frozenIndex"]
            (args.output / f"{row}-independent-receipt.json").write_text(json.dumps(case, indent=2) + "\n", encoding="utf-8")
if args.native_control:
    capture = args.native_control.read_bytes()
    raw = json.loads(capture)
    assert not raw["messages"] and len(raw["dataGrids"]) == 1
    grid = raw["dataGrids"][0]
    assert grid["nx"] == grid["ny"] == 128 and grid["dx"] == grid["dy"] == .25
    assert grid["minX"] == grid["minY"] == -15.75
    calculation = np.load(args.output / "2875-independent.npz")["second"]
    original = np.asarray(grid["values"])
    control["nativeConverged2048Sha256"] = hashlib.sha256(capture).hexdigest()
    control["comparisonToNative2048"] = image_metrics(calculation, original)
    (args.output / "configuration-one-control.json").write_text(json.dumps(control, indent=2) + "\n", encoding="utf-8")
