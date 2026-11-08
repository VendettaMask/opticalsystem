"""Reconstruct the exact scalar #2499 image window from independent conic tracing."""
import hashlib
import json
import re
import sys
from pathlib import Path
from zipfile import ZipFile

import numpy as np
from mpmath import iv
from scipy.special import roots_legendre

repo = Path(sys.argv[1])
output = Path(sys.argv[2])
src = repo / 'src/OpticalSystemDesign.App/Assets/SequentialExamples/Telescopes/Unobscured Gregorian.zmx'
validation = repo / 'validation'
frozen_archive = validation / 'repairs/2026-10-21/sequential-corpus-full-evidence.zip'
ray_root = validation / 'rechecks/2026-11-07/menu-remaining-eight'
core_root = validation / 'rechecks/2026-11-08/fft-adaptive-coarse-retry'
request_root = validation / 'rechecks/2026-11-08/diffraction-2499-native-high'
frozen_wavefront = 'run/analyses/2498-082-unobscured-gregorian-c1-primary-f1-w1-wavefront/native/data.json'
frozen_request = 'run/analyses/2499-082-unobscured-gregorian-c1-primary-f1-w1-psf/workbench/request.json'

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def archive_sha(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()

head, *surf = re.split(r'^SURF \d+\s*$', src.read_text(encoding='utf-16'), flags=re.M)

def get(section, key):
    return float(re.search(r'^\s*' + key + r'\s+(\S+)', section, re.M).group(1))

v4 = get(surf[1], 'DISZ') + get(surf[2], 'DISZ')
v5 = v4 + get(surf[4], 'DISZ')
img = v5 + get(surf[5], 'DISZ') + get(surf[6], 'DISZ')
pupil_z = get(surf[1], 'DISZ')
pupil_radius = get(head, 'ENPD')/2
decenter = get(surf[3], 'PARM 2')
wavelength = get(head, 'WAVM 1')
R4 = 1/get(surf[4], 'CURV')
R5 = 1/get(surf[5], 'CURV')
k4 = get(surf[4], 'CONI')
k5 = get(surf[5], 'CONI')
secondary_clear = float(re.search(r'^\s*FLAP\s+\S+\s+(\S+)', surf[5], re.M).group(1))

def dot(a, b):
    return np.sum(a*b, axis=-1)

def bounce(p, d, vertex, radius, conic, clear=None):
    q = p.copy()
    q[:, 2] -= vertex
    a = dot(d, d) + conic*d[:, 2]**2
    b = 2*(dot(q, d) + conic*q[:, 2]*d[:, 2] - radius*d[:, 2])
    c = dot(q, q) + conic*q[:, 2]**2 - 2*radius*q[:, 2]
    discriminant = b*b - 4*a*c
    if np.min(discriminant) < 0:
        raise ValueError('no conic intercept')
    stable = -.5*(b + np.copysign(np.sqrt(discriminant), b))
    t1, t2 = stable/a, c/stable

    def valid(t):
        admissible = (t > 0) & (((1+conic)*(q[:, 2] + t*d[:, 2]) - radius)*radius < 0)
        if clear is not None:
            intercept = p[:, :2] + t[:, None]*d[:, :2]
            admissible &= dot(intercept, intercept) <= clear**2
        return admissible

    t = np.minimum(np.where(valid(t1), t1, np.inf), np.where(valid(t2), t2, np.inf))
    if not np.all(np.isfinite(t)):
        raise ValueError('no conic vertex branch')
    intercept = p + t[:, None]*d
    normal = intercept.copy()
    normal[:, 2] = (1+conic)*(normal[:, 2] - vertex) - radius
    reflected = d - 2*(dot(d, normal)/dot(normal, normal))[:, None]*normal
    return intercept, reflected, t

def trace(px, py, tilt_y=0):
    launch = np.column_stack((pupil_radius*px, pupil_radius*py-decenter, np.full(px.size, pupil_z)))
    incident = np.tile([0, tilt_y, 1], (px.size, 1))/np.sqrt(1 + tilt_y*tilt_y)
    primary, reflected, primary_path = bounce(launch, incident, v4, R4, k4)
    secondary, outgoing, secondary_path = bounce(primary, reflected, v5, R5, k5, secondary_clear)
    image_path = (img-secondary[:, 2])/outgoing[:, 2]
    image = secondary + image_path[:, None]*outgoing
    return primary, reflected, secondary, outgoing, image, primary_path+secondary_path+image_path

center = trace(np.array([0.]), np.array([0.]))
pchief = center[4][0]
dchief = center[3][0]
oplchief = center[5][0]

def sphere_radius(h):
    p = trace(np.array([0.]), np.array([0.]), h)
    m = trace(np.array([0.]), np.array([0.]), -h)
    height_difference = p[4][0, 1] - m[4][0, 1]
    slope_difference = p[3][0, 1]/p[3][0, 2] - m[3][0, 1]/m[3][0, 2]
    distance = -height_difference/slope_difference
    return -distance/dchief[2]

def phase(out, sphere):
    _, _, _, direction, image, path = out
    delta = image - pchief
    projection = dot(delta, direction)
    cross = np.cross(delta, direction)
    offset_squared = dot(cross, cross)
    correction = projection - offset_squared/(sphere + np.sqrt(sphere*sphere - offset_squared))
    return (oplchief - path + correction)/(wavelength*1e-3)

assert len(surf) == 8 and 'UNIT MM ' in head and 'ROPD 2' in head
assert 'STOP' in surf[2] and 'DISZ INFINITY' in surf[0]
assert 'TYPE COORDBRK' in surf[3] and all(get(surf[3], f'PARM {i}') == 0 for i in [1, 3, 4, 5, 6])
assert all('GLAS MIRROR ' in surf[i] and not re.search(r'^\s*COAT', surf[i], re.M) for i in (4, 5))
assert all('TYPE STANDARD' in section for i, section in enumerate(surf) if i != 3)
assert np.isclose(get(surf[4], r'OBDC\s+\S+'), -decenter)
assert get(surf[1], 'DIAM') > pupil_radius
assert float(re.search(r'^\s*CLAP\s+\S+\s+(\S+)', surf[4], re.M).group(1)) > pupil_radius

# The primary and secondary share the same parent axis. Each ray therefore
# stays in one meridional plane, with incident radius from 50 to 150 mm.
# Prove that the reflected ray intersects the near-vertex secondary cap
# strictly inside the +28 mm rim throughout that entire interval.
def sag(r, radius, conic):
    return r*r / (radius * (1 + iv.sqrt(1 - (1 + conic)*r*r/(radius*radius))))

interval_count = 1024
bounds = np.linspace(decenter-pupil_radius, decenter+pupil_radius, interval_count + 1)
margins = [float('inf')] * 3
max_gradient = 0.0
for start, end in zip(bounds[:-1], bounds[1:]):
    rho = iv.mpf([float(start), float(end)])
    z = v4 + sag(rho, R4, k4)
    gradient = -rho / (R4 - (1 + k4) * (z - v4))
    slope = (gradient*gradient - 1) / (2*gradient)

    def height_difference(y):
        return z + (y + rho)*slope - v5 - sag(iv.mpf(y), R5, k5)

    local = [float(height_difference(-secondary_clear).a), float(height_difference(0).a),
             -float(height_difference(secondary_clear).b)]
    margins = [min(a, b) for a, b in zip(margins, local)]
    max_gradient = max(max_gradient, float(gradient.b))
assert min(margins) > 0 and max_gradient < 1 and R5 > 0
# z_ray - z_cap is concave. Positive values at -28 and 0 exclude a root
# in the negative half. On the positive half it decreases strictly.

ray_archive = ray_root / 'native-mask-controls.zip'
ray_summary = json.loads((ray_root/'native-mask-summary.json').read_bytes())
case_identity = next(row for row in ray_summary['cases'] if row['index'] == 2498)
assert sha(src.read_bytes()) == case_identity['sourceSha256']
ray_archive_sha = archive_sha(ray_archive)
assert ray_archive_sha == ray_summary['archiveSha256']
with ZipFile(ray_archive) as archive:
    native_ray_bytes = archive.read('2498/ray-audit.json')
    model = json.loads(archive.read('2498/model.json'))
    solves = json.loads(archive.read('2498/surface-aperture-solves.json'))
native = json.loads(native_ray_bytes)
assert native['wavelength'] == 1 and model['aperture']['ApodizationType'] == 'Uniform'
assert native['rayAiming']['RayAiming'] == 'Off' and native['entrancePupilDiameter'] == 2*pupil_radius
assert model['advanced']['ReferenceOPD'] == 'ExitPupil'
assert solves['surfaces'][5]['aperture']['CurrentType'] == 'FloatingAperture'
assert solves['surfaces'][5]['semiDiameter'] == secondary_clear
assert all(ray['hx'] == ray['hy'] == 0 for ray in native['inputs'])
xy = np.array([[ray['px'], ray['py']] for ray in native['inputs']])
paths = trace(*xy.T)
position_errors, direction_errors = [], []
for index, p, d, vertex, sign in [(4, paths[0], paths[1], v4, -1),
                                   (5, paths[2], paths[3], v5, 1),
                                   (7, paths[4], paths[3], img, 1)]:
    surface = next(item for item in native['surfaces'] if item['surface'] == index)
    rays = surface['rays']
    assert all(ray['error'] == ray['vignette'] == 0 and ray['intensity'] == 1 for ray in rays)
    positions = np.array([[ray['x'], ray['y'], ray['z'] + vertex] for ray in rays])
    directions = sign*np.array([[ray['l'], ray['m'], ray['n']] for ray in rays])
    position_errors.append(float(np.max(np.abs(positions - p))))
    direction_errors.append(float(np.max(np.abs(directions - d))))
assert len(xy) == 3001 and max(position_errors) < 2e-10 and max(direction_errors) < 2e-12

# The real stop-pencilled reference sphere is the chief's distance to the
# meridional pupil conjugate. Symmetric differences converge as h².
steps = [1e-3, 5e-4, 2.5e-4]
radii = [sphere_radius(h) for h in steps]
sphere = (4*radii[2] - radii[1])/3
prior_sphere = (4*radii[1] - radii[0])/3
assert abs(sphere-prior_sphere) < 1e-7

frozen_archive_sha = archive_sha(frozen_archive)
assert frozen_archive_sha == '1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'
with ZipFile(frozen_archive) as archive:
    phase_bytes = archive.read(frozen_wavefront)
    original_request_bytes = archive.read(frozen_request)
    policy_bytes = archive.read('run/comparison-settings.json')
native_grid = json.loads(phase_bytes)['dataGrids'][0]
assert native_grid['nx'] == native_grid['ny'] == 64 and native_grid['minX'] == native_grid['minY'] == -1
assert native_grid['dx'] == native_grid['dy'] == 2/63
native_opd = np.asarray(native_grid['values'], dtype=float)
sample = (np.arange(64) - 32)/31
wx, wy = np.meshgrid(sample, sample)
edges = trace(np.array([1., -1., 0., 0.]), np.array([0., 0., 1., -1.]))[3]
edge_na = np.linalg.norm(np.cross(edges, dchief), axis=1).reshape(2, 2)
arithmetic_na = edge_na.mean(axis=1)
display_scale = arithmetic_na / arithmetic_na.max()
wave_mask = np.isfinite(native_opd)
assert np.array_equal(wave_mask, (wx/display_scale[0])**2 + (wy/display_scale[1])**2 <= 1)
reference_phase = phase(trace(wx[wave_mask]/display_scale[0], wy[wave_mask]/display_scale[1]), sphere)
phase_residual = float(np.max(np.abs(reference_phase-reference_phase.min()-native_opd[wave_mask])))
assert len(reference_phase) == 2999 and phase_residual < 1e-7

core_archive = core_root/'core-numeric-evidence.zip'
with ZipFile(core_archive) as archive:
    core_bytes = archive.read('2499/data.json')
    core_request_bytes = archive.read('2499/request.json')
    core_provenance = json.loads(archive.read('2499/provenance.json'))
core = json.loads(core_bytes)
job = json.loads(core_request_bytes)['request']
original = json.loads(original_request_bytes)['request']
retry = {key: value for key, value in job['workbenchSettings'].items() if key not in
         {'FourierSamplingMode', 'FourierMaximumSampling'}}
assert {**job, 'workbenchSettings': retry} == original
assert job['canonicalAnalysisKey'] == 'PSF' and job['configuration'] == job['field'] == job['wavelength'] == 1
assert job['reference'] == 'ChiefRay' and not job['polarization'] and not job['useRayAiming']
assert job['surface'] == -1 and job['normalization'] == 'NativePhysical'
assert job['imageSampling'] == 128 and job['imageDeltaMicrometers'] == .25
assert core['outcome'] == 'Success' and core['values']['FourierSamplingValidation']['validated']
assert core['values']['FourierSamplingValidation']['returnedPupilSampling'] == 2048
assert len(core['seriesList']) == 1
series = core['seriesList'][0]
assert series['xUnit'] == series['yUnit'] == 'Micrometer' and series['valueQuantity'] == 'Irradiance'
assert series['valueUnit'] == 'Dimensionless'
points = np.array([[point[k] for k in ('x', 'y', 'value')] for point in series['points']]).reshape(128, 128, 3)
x = (np.arange(128) - 63)*.25
xx, yy = np.meshgrid(x, x)
assert np.array_equal(xx, points[:, :, 0]) and np.array_equal(yy, points[:, :, 1])
assert np.all(np.isfinite(points[:, :, 2]))
na = np.sqrt(np.mean(edge_na**2, axis=1))
working_f_number = float(.5/np.sqrt(np.mean(edge_na**2)))
assert abs(working_f_number - core['values']['WorkingFNumber']) < 1e-8

# Normalized Fourier integral over the physical entrance disk. The phase is
# traced to the actual reference sphere. The original FFT uses linear pupil
# coordinates with separately scaled sagittal and tangential image axes.
def integrate(radial, azimuth, exact_directions=False):
    radius, weight = roots_legendre(radial)
    radius = (radius+1)/2
    theta = np.arange(azimuth)*(2*np.pi/azimuth)
    px = (radius[:, None]*np.cos(theta)).ravel()
    py = (radius[:, None]*np.sin(theta)).ravel()
    rays = trace(px, py)
    weights = np.repeat(weight*radius/azimuth, azimuth)
    assert abs(np.sum(weights) - 1) < 1e-13
    amplitude = weights*np.exp(-2j*np.pi*phase(rays, sphere))
    if exact_directions:
        fx, fy = rays[3][:, 0], rays[3] @ np.array([0, dchief[2], -dchief[1]])
    else:
        fx, fy = na[0]*px, na[1]*py
    kernel = -2j*np.pi/wavelength
    ex = np.exp(kernel*fx[:, None]*x)
    ey = np.exp(kernel*x[:, None]*fy)
    return np.abs(ey @ (amplitude[:, None]*ex))**2

policy = json.loads(policy_bytes)
gate = dict(policy['analyses']['PSF']['quantities']['Irradiance'], minimumCoverage=.95)
assert gate['nrmse'] == .01

def metric(candidate, reference):
    error = np.abs(candidate-reference)
    assert np.all(np.isfinite(error))
    nrmse = float(np.sqrt(np.mean(error**2)) / max(np.max(np.abs(reference)), gate['absolute']))
    pointwise = bool(np.all(error <= gate['absolute'] + gate['relative']*np.abs(reference)))
    conclusion = ('Pass' if pointwise or nrmse <= gate['nrmse']
                  else 'Close' if nrmse <= gate['closeNrmse'] else 'Difference')
    return {'count': int(error.size), 'coverage': 1, 'maximumAbsolute': float(error.max()),
            'nrmse': nrmse, 'conclusion': conclusion}

coarse = integrate(24, 96)
fine = integrate(48, 192)
direct = integrate(48, 192, exact_directions=True)
authority = json.loads((request_root/'verification.json').read_bytes())
assert authority['sourceSha256'] == case_identity['sourceSha256']
assert all(tier['apiGridShape'] == [0, 0] for tier in authority['native'])
report = {
    'index': 2499, 'lens': authority['lens'], 'originalRequest': authority['originalRequest'],
    'sourceSha256': case_identity['sourceSha256'], 'frozenVerdict': 'Incomparable',
    'frozenArchiveSha256': frozen_archive_sha, 'frozenRequestMember': frozen_request,
    'frozenRequestSha256': sha(original_request_bytes), 'frozenComparisonPolicySha256': sha(policy_bytes),
    'frozenWavefrontMember': frozen_wavefront, 'frozenWavefrontSha256': sha(phase_bytes),
    'nativeRayArchiveSha256': ray_archive_sha, 'nativeRayMemberSha256': sha(native_ray_bytes),
    'workbenchArchiveSha256': archive_sha(core_archive), 'workbenchDataSha256': sha(core_bytes),
    'workbenchRequestSha256': sha(core_request_bytes), 'workbenchProvenance': core_provenance,
    'workbenchCertificate': core['values']['FourierSamplingValidation'],
    'apertureCertificate': {'radialIntervals': interval_count, 'radiusRangeMm': bounds[[0,-1]].tolist(),
                            'signedSecondarySagMarginsMm': margins, 'upperReflectionSlopeParameter': max_gradient},
    'nativeRays': len(xy), 'maximumNativeInterceptResidualMm': max(position_errors),
    'maximumNativeDirectionResidual': max(direction_errors),
    'nativeWavefrontNodes': len(reference_phase), 'maximumNativeOpdResidualWaves': phase_residual,
    'referenceSphereRadiusMm': float(sphere), 'referenceSphereRichardsonChangeMm': float(abs(sphere-prior_sphere)),
    'edgeNumericalApertures': edge_na.tolist(), 'axisRmsNumericalApertures': na.tolist(),
    'workingFNumber': working_f_number, 'originalGate': gate,
    'imageGrid': {'rows': 128, 'columns': 128, 'pixelPitchMicrometers': .25, 'first': -15.75, 'last': 16.0},
    'independentQuadratureConvergence': metric(coarse, fine),
    'nonlinearDirectionMapControl': metric(direct, fine),
    'workbenchVersusIndependentFftReference': metric(points[:,:,2], fine),
    'workbenchVersusExactDirectionControl': metric(points[:,:,2], direct)
}
assert report['workbenchVersusIndependentFftReference']['conclusion'] == 'Pass'
output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')

