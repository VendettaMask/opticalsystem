import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path

import numpy as np
from scipy.optimize import least_squares
from scipy.special import roots_legendre as leggauss


parser = argparse.ArgumentParser()
parser.add_argument("snapshot", type=Path)
parser.add_argument("corpus", type=Path)
parser.add_argument("native_window_archive", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--formal", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)

wave = .55
snapshot = args.snapshot.read_bytes()
snapshot_hash = hashlib.sha256(snapshot).hexdigest()
assert snapshot_hash == "e0a8e954c24b6d12150cae65f55d66898c617110b963c022a43fff0b34e32cf2"
model = json.loads(snapshot)
corpus = args.corpus / "analyses"
source = (args.corpus / "sources/Optical testing/Twyman Green Interferometer.zmx").read_bytes()
source_hash = hashlib.sha256(source).hexdigest()
assert source_hash == "975be62693a8c3d8870c2ee3fd01e2f51de37d558afe7265cd939fe262f49051"
bk7 = np.sqrt(1 + np.sum(np.array([1.03961212, .231792344, 1.01046945])*wave**2 /
                         (wave**2 - np.array([.00600069867, .0200179144, 103.560653]))))
media = {"Air": 1., "BK7": bk7}
surface_list = []
for surface in model["surfaces"]:
    frame = surface["coordinateSystem"]
    theta = np.deg2rad(frame["rotationXDegrees"])
    c, s = np.cos(theta), np.sin(theta)
    assert frame["rotationYDegrees"] == frame["rotationZDegrees"] == 0
    matrix = np.array([[1., 0., 0.], [0., c, -s], [0., s, c]])
    origin = np.array([frame["originX"], frame["originY"], frame["originZ"]])
    surface_list.append((surface["components"], origin, matrix))


input_hashes = {}


def native(row, name="data.json"):
    case, = corpus.glob(f"{row:04d}-*")
    data = (case/"native"/name).read_bytes()
    input_hashes[str(row)] = hashlib.sha256(data).hexdigest()
    if name == "data.json":
        result = json.loads(data)
        assert not result["messages"]
        return result
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def dot(a, b):
    return np.sum(a * b, axis=-1)


def normalize(vector):
    return vector / np.linalg.norm(vector, axis=-1)[..., None]


def surface_profile(x, y, geometry):
    kind, parameters = geometry["kind"], geometry["numbers"]
    if kind in ("plane", "tilted_plane"):
        tx, ty = parameters.get("tangentX", 0), parameters.get("tangentY", 0)
        return x*tx + y*ty, np.zeros_like(x)+tx, np.zeros_like(y)+ty
    radius, conic = parameters["radius"], parameters["conic"]
    r2 = x*x + y*y
    root = np.sqrt(1 - (1+conic)*r2/radius**2)
    z = r2 / (radius*(1+root))
    dr = 1/(2*radius*root)
    if kind == "even_asphere":
        for j in range(8):
            coefficient = parameters.get(f"c{j}", 0)
            if coefficient:
                z += coefficient*r2**(j+1)
                dr += (j+1)*coefficient*r2**j
    else:
        assert kind == "standard"
    return z, 2*x*dr, 2*y*dr


def trace(px, py):
    px, py = np.broadcast_arrays(px, py)
    direction = normalize(np.stack((.5*px, .5*py, np.ones_like(px)), axis=-1))
    shape = direction.shape
    point = np.broadcast_to([0., 0., -10.], shape).copy()
    opl = np.zeros(px.shape)
    boundary = []
    for components, origin, matrix in surface_list[1:]:
        p = (point-origin) @ matrix
        d = direction @ matrix
        geometry = components["geometry"]
        t = -p[..., 2] / d[..., 2]
        for _ in range(8):
            ray = p + t[..., None]*d
            z, sx, sy = surface_profile(ray[..., 0], ray[..., 1], geometry)
            residual = ray[..., 2] - z
            t -= residual / (d[..., 2] - sx*d[..., 0] - sy*d[..., 1])
            if np.max(abs(residual)) < 1e-12:
                break
        ray = p + t[..., None]*d
        z, sx, sy = surface_profile(ray[..., 0], ray[..., 1], geometry)
        assert np.max(abs(ray[..., 2] - z)) < 1e-10
        normal = normalize(np.stack((-sx, -sy, np.ones_like(sx)), axis=-1))
        incoming = media[components["materialBefore"]]
        outgoing = media[components["materialAfter"]]
        opl += incoming*t
        if components["interactionKind"] == "reflective":
            d -= (2*dot(d, normal))[..., None]*normal
        elif incoming != outgoing:
            normal = np.where((dot(d, normal)>0)[..., None], -normal, normal)
            cos = -dot(d, normal)
            eta = incoming/outgoing
            d = eta*d + (eta*cos - np.sqrt(1-eta**2*(1-cos*cos)))[..., None]*normal
        point = ray @ matrix.T + origin
        direction = d @ matrix.T
        aperture = components["physicalAperture"]
        if aperture:
            boundary.append(np.hypot(ray[..., 0], ray[..., 1]) / aperture["numbers"]["radius"])
    _, origin, matrix = surface_list[-1]
    return (point-origin) @ matrix, direction @ matrix, opl, boundary


chief, chief_dir, chief_path, _ = trace(0., 0.)
edge, edge_dir, _, _ = trace(0., .8)
ray = [float(v) for v in re.search(r"(?m)^\s*20\t([^\r\n]+)", native(2152, "data.txt")).group(1).split()[:6]]
single_position_error = float(np.max(abs(edge - ray[:3])))
single_direction_error = float(np.max(abs(edge_dir - ray[3:6])))
fan_checks = []
for j, fan in enumerate(native(2154)["dataSeries"]):
    p = np.array(fan["x"])
    exact, _, _, _ = trace(0*p if j == 0 else p, p if j == 0 else 0*p)
    component = 1 - j
    ref = np.array(fan["y"])[:, 0]
    hits = (exact[:, component] - chief[component])*1000
    fan_checks.append({
        "direction": ("Tangential", "Sagittal")[j], "coordinates": len(p),
        "maximumAbsoluteMicrometers": float(np.max(abs(hits-ref))),
        "nativeRangeMicrometers": [float(ref.min()), float(ref.max())],
    })
_, cone, _, _ = trace(np.array([0, 0, 1, -1]), np.array([1, -1, 0, 0]))
sines = np.linalg.norm(np.cross(chief_dir, cone), axis=-1)
numbers = 1/(2*np.sqrt(np.mean(sines.reshape(2, 2)**2, axis=1)))


def phase_factors(px, py):
    point, direction, path, _ = trace(px, py)
    offset = point - chief
    longitudinal = dot(direction, offset)
    return path-chief_path-longitudinal, dot(offset, offset) - longitudinal**2


def from_factors(plane, impact, sphere):
    # The virtual branch adds the sphere travel to the plane eikonal.
    correction = impact/(sphere + np.sqrt(sphere*sphere-impact))
    return -(plane-correction)/(wave*.001)


def phase(px, py, sphere):
    return from_factors(*phase_factors(px, py), sphere)


raw = np.asarray(native(2156)["dataGrids"][0]["values"], dtype=float)
valid = np.isfinite(raw)
y, x = np.indices(raw.shape)
px, py = (x[valid]-32)/31, (y[valid]-32)/31
target = raw[valid]
fit_mask = (abs(x[valid]-32) + 2*(y[valid]-32)) % 3 == 0
parameters = least_squares(
    lambda p: phase(px[fit_mask], py[fit_mask]*p[2], p[0])+p[1]-target[fit_mask],
    x0=[1.169, 0, 1], bounds=([.8,-60,.95],[1.6,60,1.05])).x
rs = parameters[0]
held = phase(px[~fit_mask], py[~fit_mask]*parameters[2], rs)+parameters[1]-target[~fit_mask]


def integrate(order, swapped, x_axis, y_axis, sphere=rs):
    roots, weights = leggauss(order)
    angle = roots*np.pi/2
    chord = np.cos(angle)
    varying = chord[:, None]*roots
    fixed = np.sin(angle)[:, None]
    px, py = (fixed, varying) if swapped else (varying, fixed)
    opd = phase(px, py, sphere)
    amplitude = (1 + .25*(px*px+py*py))**-.75
    field = amplitude*np.exp(-2j*np.pi*opd)
    kx = np.pi*x_axis/(wave*numbers[1])
    ky = np.pi*y_axis/(wave*numbers[0])
    outer, inner = (kx,ky) if swapped else (ky,kx)
    marginal = np.empty((order, inner.size), dtype=complex)
    for i in range(order):
        marginal[i] = np.exp(-1j*inner[:, None]*varying[i]) @ (weights*field[i])
    transform = np.exp(-1j*outer[:, None]*fixed[:, 0]) @ (marginal*(weights*chord**2)[:, None])
    ideal = np.sum(amplitude*weights[None, :]*(weights*chord**2)[:, None])
    return np.abs((transform.T if swapped else transform)/ideal)**2


def metrics(sample, reference):
    e = sample-reference
    return {"coordinates": int(e.size), "maximumAbsolute": float(np.max(abs(e))),
            "peakNormalizedRms": float(np.sqrt(np.mean(e*e))/reference.max()),
            "relativeL2": float(np.linalg.norm(e)/np.linalg.norm(reference))}


half = wave*min(numbers)*(32-2)/2
automatic_axis = -half + 2*half*np.arange(201)/200
section_axis = automatic_axis
formal_result = None
if args.formal:
    data = args.formal.read_bytes()
    formal = json.loads(data)
    formal_result = {"dataSha256": hashlib.sha256(data).hexdigest(), "outcome": formal["outcome"],
                     "certificate": formal["values"]["FourierSamplingValidation"]}
    if formal["outcome"] == "Success":
        series, = formal["plotSeries"]
        assert (series["xQuantity"], series["xUnit"], series["yQuantity"], series["yUnit"]) == (
            "ImageHeight", "Micrometer", "Irradiance", "Dimensionless")
        section_axis = np.array([point["x"] for point in series["points"]])
        assert section_axis.shape == automatic_axis.shape
        assert np.max(abs(section_axis-automatic_axis)) <= 1e-10
section_a = integrate(768, False, section_axis, np.array([0.]))[0]
section_b = integrate(1024, True, section_axis, np.array([0.]))[0]
section_core = integrate(1024, True, section_axis, np.array([0.]), sphere=1.1690297340693634)[0]
psf_axis = np.arange(-63,65)*.25
image = integrate(768, False, psf_axis, psf_axis)
with zipfile.ZipFile(args.native_window_archive) as archived:
    native_payload = archived.read("2157-512/data.json")
    assert hashlib.sha256(native_payload).hexdigest() == "f6cdd466aeed5831a2678ea6fbaa58f84b5910925de0d21aad0227e4a22dd547"
    native_grid = json.loads(native_payload)["dataGrids"][0]
    assert native_grid["nx"] == native_grid["ny"] == 128 and native_grid["dx"] == native_grid["dy"] == .25
    assert native_grid["minX"] == native_grid["minY"] == -15.75
    native_image = np.asarray(native_grid["values"])
payload_path = args.output / "2161-independent.npz"
np.savez_compressed(payload_path, axis=section_axis, first=section_a,
                    second=section_b, image=image, psf_axis=psf_axis)
if formal_result and formal_result["outcome"] == "Success":
    formal_y = np.array([point["y"] for point in series["points"]])
    cross = metrics(formal_y, section_b)
    cross["pointwisePass"] = bool((abs(formal_y - section_b) <= 1e-5 + .002*abs(section_b)).all())
    cross["nrmsePass"] = cross["peakNormalizedRms"] <= .003
    formal_result["versusIndependent"] = cross
receipt = {
    "frozenIndex": 2161, "nativeDirectProfileUsable": False,
    "sourceSha256": source_hash, "snapshotSha256": snapshot_hash,
    "nativeInputsSha256": input_hashes, "singleRayPositionMaxMm": single_position_error,
    "singleRayDirectionMax": single_direction_error, "rayFanChecks": fan_checks,
    "virtualSphereRadiusMm": float(rs), "wavefrontPistonWaves": float(parameters[1]),
    "wavefrontTangentialPupilStretch": float(parameters[2]),
    "wavefrontFitNodes": int(fit_mask.sum()), "wavefrontHoldoutNodes": int(len(held)),
    "wavefrontHoldoutMaximumWaves": float(np.max(abs(held))),
    "workingFNumbersFromMarginalRaysTS": numbers.tolist(),
    "automaticHalfRangeMicrometers": float(half), "axisCoordinates": 201,
    "quadratureOrders": [768, 1024], "betweenIndependentQuadratures": metrics(section_a, section_b),
    "coreSphereSensitivity": metrics(section_core, section_b),
    "centerRelativeToIdeal": float(section_b[100]), "peakRelativeToIdeal": float(section_b.max()),
    "independentVsNative2157Window": metrics(image, native_image),
    "independentPayloadSha256": hashlib.sha256(payload_path.read_bytes()).hexdigest(),
    "independentPayloadArchive": "window-numeric.zip",
    "independentPayloadMember": f"sha256/{hashlib.sha256(payload_path.read_bytes()).hexdigest()}",
    "formal": formal_result,
}
(args.output / "2161-independent-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")

