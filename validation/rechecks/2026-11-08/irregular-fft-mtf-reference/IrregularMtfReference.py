"""Reconstruct #1205 FFT MTF by continuous pupil autocorrelation."""
import hashlib
import json
import re
import sys
from pathlib import Path
from zipfile import ZipFile

import numpy as np
from scipy.interpolate import CubicSpline
from scipy.special import roots_legendre
from scipy.spatial.transform import Rotation

repo = Path(sys.argv[1])
output = Path(sys.argv[2])
lens = 'Miscellaneous/Irregular surface.ZMX'
source = (repo / 'src/OpticalSystemDesign.App/Assets/SequentialExamples' / lens).read_bytes()
validation = repo / 'validation'
frozen_archive = validation / 'repairs/2026-10-21/sequential-corpus-full-evidence.zip'
native_wavefront = 'run/analyses/1202-038-irregular-surface-c1-primary-f1-w1-wavefront/native/data.json'
native_ray = 'run/analyses/1198-038-irregular-surface-c1-primary-f1-w1-single-ray-trace/native/data.txt'

def sha(payload):
    return hashlib.sha256(payload).hexdigest()

def archive_sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

head, *surf = re.split(r'^SURF \d+\s*$', source.decode('utf-16' if source[:2] == b'\xff\xfe' else 'utf-8-sig'),
                       flags=re.M)

def get(section, key, default=None):
    match = re.search(r'^\s*' + key + r'\s+(\S+)', section, re.M)
    return float(match.group(1)) if match else default

pupil_radius = get(head, 'ENPD')/2
wave = get(head, 'WAVM 1')
z_front = get(surf[1], 'DISZ')
z_rear = z_front + get(surf[2], 'DISZ')
z_image = z_rear + get(surf[3], 'DISZ')
R_front, R_rear = [1/get(surf[i], 'CURV') for i in (2, 3)]
K_front, K_rear = [get(surf[i], 'CONI', 0) for i in (2, 3)]
dx, dy, tx, ty, S, A, C, phi = [get(surf[3], f'PARM {i}') for i in range(1, 9)]
semi = get(surf[3], 'DIAM')
rx, ry = [Rotation.from_euler(axis, angle, degrees=True).as_matrix() for axis, angle in zip('xy', [tx, ty])]
frame = rx @ ry
pivot = np.array([dx, dy, z_rear])
# SCHOTT BK7 Sellmeier coefficients in micrometers.
B = np.array([1.03961212, .231792344, 1.01046945])
L = np.array([.00600069867, .0200179144, 103.560653])
index = np.sqrt(1 + np.sum(B*wave*wave/(wave*wave - L)))

def dot(a, b):
    return np.sum(a*b, axis=-1)

def sag(x, y, radius, conic):
    r2 = x*x + y*y
    root = np.sqrt(radius*radius - (1 + conic)*r2)
    return r2/(radius + root), x/root, y/root

def front(x, y):
    return sag(x, y, R_front, K_front)

def rear(x, y):
    z, gx, gy = sag(x, y, R_rear, K_rear)
    angle = np.deg2rad(phi)
    si, co = np.sin(angle), np.cos(angle)
    u, v = x/semi, y/semi
    r2 = u*u + v*v
    axis = v*co - u*si
    return (z + S*r2*r2 + A*axis*axis + C*r2*axis,
            gx + (4*S*r2*u - 2*A*axis*si + C*(2*u*axis - r2*si))/semi,
            gy + (4*S*r2*v + 2*A*axis*co + C*(2*v*axis + r2*co))/semi)

def refract(direction, gx, gy, index_ratio):
    normal = np.column_stack((-gx, -gy, np.ones(gx.size)))
    normal /= np.linalg.norm(normal, axis=1)[:, None]
    incident = dot(direction, normal)
    transmitted = np.sqrt(1 - index_ratio**2*(1 - incident*incident))
    return index_ratio*direction + (transmitted - index_ratio*incident)[:, None]*normal

def surface(position, direction, model):
    distance = -position[:, 2]/direction[:, 2]
    for _ in range(12):
        intercept = position + distance[:, None]*direction
        sag_z, gx, gy = model(intercept[:, 0], intercept[:, 1])
        error = intercept[:, 2] - sag_z
        if np.max(np.abs(error)) < 1e-12:
            return distance, intercept, gx, gy
        distance -= error/(direction[:, 2] - gx*direction[:, 0] - gy*direction[:, 1])
    raise ValueError('no smooth intercept')

def trace(px, py):
    launch = np.column_stack((pupil_radius*px, pupil_radius*py, np.full(px.size, -z_front)))
    d0 = np.tile([0., 0., 1.], (px.size, 1))
    t1, a, gx, gy = surface(launch, d0, front)
    d1 = refract(d0, gx, gy, 1/index)
    a[:, 2] += z_front
    b0 = (a-pivot) @ frame
    bd = d1 @ frame
    t2, b, gx, gy = surface(b0, bd, rear)
    d2 = refract(bd, gx, gy, index) @ frame.T
    b = b @ frame.T + pivot
    t3 = (z_image - b[:, 2])/d2[:, 2]
    image = b + t3[:, None]*d2
    return a, d1, b, d2, image, t1 + index*t2 + t3

chief = trace(np.zeros(1), np.zeros(1))
pchief, dchief, oplchief = chief[4][0], chief[3][0], chief[5][0]

def phase(paths, radius):
    _, _, _, direction, image, path = paths
    displacement = image - pchief
    cross = np.cross(displacement, direction)
    offset_squared = dot(cross, cross)
    sphere_correction = offset_squared/(radius + np.sqrt(radius*radius - offset_squared))
    return (oplchief - path + dot(displacement, direction) - sphere_correction)/(wave*1e-3)

def transfer(distance, index=1):
    return np.array([[1, distance/index], [0, 1]])

def refraction_matrix(radius, before, after):
    return np.array([[1, 0], [-(after-before)/radius, 1]])

matrix = (transfer(z_image-z_rear) @ refraction_matrix(R_rear, index, 1)
          @ transfer(z_rear-z_front, index) @ refraction_matrix(R_front, 1, index) @ transfer(z_front))
parax_R = matrix[0, 1]/matrix[1, 1]/dchief[2]

assert len(surf) == 5 and 'UNIT MM ' in head and 'ROPD 2' in head
assert 'STOP' in surf[1] and 'DISZ INFINITY' in surf[0] and 'GLAS BK7 ' in surf[2]
assert 'TYPE IRREGULA' in surf[3] and tx == .1 and ty == 0
assert all('TYPE STANDARD' in part for i, part in enumerate(surf) if i != 3)
assert all('FLAP 0 2.40E+01 0' in surf[i] for i in (2,3))
assert not re.search(r'^\s*(?:COAT|CLAP|OBSC)\b', '\n'.join(surf), re.M)

# Bound the entire illuminated disk. The glass ray travels radially inward
# before reaching the decentered rear sag. The (unrotated) apertures both
# have 24 mm radius; the ray's radial height remains at most 20 mm.
qmin = np.sqrt(R_front**2-(1+K_front)*pupil_radius**2)
cosmin = qmin/np.sqrt(qmin*qmin+pupil_radius**2)
eta = 1/index
relative_glass_slope = (1-eta*cosmin)/(qmin*(eta+(1-eta)*cosmin))
maximum_glass_slope = pupil_radius*relative_glass_slope
assert (25-z_front)*relative_glass_slope < 1
assert z_front+pupil_radius**2/(R_front+qmin) < 15
tilt = np.deg2rad(abs(tx))
rear_xy_bound = pupil_radius + np.hypot(dx,dy) + 8*np.sin(tilt)
normalized_bound = rear_xy_bound/semi
minimum_sag = -(abs(S)*normalized_bound**4 + abs(A)*normalized_bound**2 + abs(C)*normalized_bound**3)
rear_qmin = np.sqrt(R_rear**2-(1+K_rear)*rear_xy_bound**2)
maximum_sag = rear_xy_bound**2/(R_rear+rear_qmin) - minimum_sag
rear_gradient = (rear_xy_bound/rear_qmin + (4*abs(S)*normalized_bound**3 +
                 2*abs(A)*normalized_bound + 3*abs(C)*normalized_bound**2)/semi)
axis_offset = (pupil_radius+abs(dy))*np.sin(tilt)
assert (15-z_rear)*np.cos(tilt) + axis_offset < minimum_sag
assert (25-z_rear)*np.cos(tilt) - axis_offset > maximum_sag
assert np.cos(tilt)-np.sin(tilt)*maximum_glass_slope > rear_gradient*(maximum_glass_slope+np.sin(tilt))
incidence_limit = np.arctan(maximum_glass_slope)+np.arctan(rear_gradient)+tilt
critical = np.arcsin(eta)
assert incidence_limit < critical

frozen_sha = archive_sha(frozen_archive)
assert frozen_sha == '1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'
with ZipFile(frozen_archive) as archive:
    phase_bytes = archive.read(native_wavefront)
    native_ray_bytes = archive.read(native_ray)
    sources = json.loads(archive.read('run/sources.json'))
source_identity = next(item for item in sources if item['source']['id'] == lens)
assert sha(source) == source_identity['source']['sha256'] == source_identity['actualSha256']
native_grid = json.loads(phase_bytes)['dataGrids'][0]
assert native_grid['nx'] == native_grid['ny'] == 64 and native_grid['minX'] == native_grid['minY'] == -1
assert native_grid['dx'] == native_grid['dy'] == 2/63

# The native text report's physical ray at normalized pupil (0,+0.8).
# Published intercepts are local to each surface's sequential vertex.
ray_text = native_ray_bytes.decode('utf-16')
assert re.search(r'\(Py\).*0\.8000000000', ray_text)
rows = {}
for line in ray_text.splitlines():
    if line.startswith('近轴'):
        break
    cols = line.split()
    if len(cols) > 11 and cols[0].isdigit():
        rows[int(cols[0])] = [float(v) for v in cols[1:7]]
p, d, q, e, r, _ = trace(np.array([0.]), np.array([.8]))
local = np.array([np.r_[p[0]-[0,0,z_front],d[0]], np.r_[q[0]-[0,0,z_rear],e[0]],
                  np.r_[r[0]-[0,0,z_image],e[0]]])
error = np.abs(np.array([rows[i] for i in (2,3,4)])-local)
assert error.max() < 6e-11

native_opd = np.asarray(native_grid['values'], dtype=float)
samples = (np.arange(64)-32)/31
wave_x, wave_y = np.meshgrid(samples,samples)
edges = trace(np.array([1.,-1.,0.,0.]), np.array([0.,0.,1.,-1.]))[3]
edge_na = np.linalg.norm(np.cross(edges,dchief), axis=1).reshape(2,2)
directional_na = edge_na.mean(axis=1)
display_scale = directional_na/directional_na.max()
mask = np.isfinite(native_opd)
assert np.array_equal(mask, (wave_x/display_scale[0])**2+(wave_y/display_scale[1])**2 <= 1)
assert np.count_nonzero(mask) == 2869

# Invert the exact spherical path correction using one preselected
# full-pupil edge and chief from the frozen native wavefront. The other
# signed edge and all remaining finite nodes independently check the radius.
def native_sphere(y_index):
    _, _, _, direction, image, path = trace(np.zeros(1), np.array([samples[y_index]/display_scale[1]]))
    displacement = image-pchief
    cross = np.cross(displacement, direction)
    offset_squared = dot(cross,cross)[0]
    opd_mm = (native_opd[y_index,32]-native_opd[32,32])*wave*1e-3
    correction = opd_mm-(oplchief-path+dot(displacement,direction))[0]
    assert correction < 0
    return -(offset_squared+correction*correction)/(2*correction)

radius = native_sphere(1)
opposite = native_sphere(63)
assert abs(radius-opposite) < 1e-7
phase_paths = trace(wave_x[mask]/display_scale[0], wave_y[mask]/display_scale[1])
native_relative = native_opd[mask]-native_opd[32,32]
reference_phase_error = float(np.max(np.abs(phase(phase_paths,radius)-native_relative)))
nominal_phase_error = float(np.max(np.abs(phase(phase_paths,parax_R)-native_relative)))
assert reference_phase_error < 1e-8


with ZipFile(frozen_archive) as archive:
    request_member = next(name for name in archive.namelist()
                          if '/1205-' in name and name.endswith('/workbench/request.json'))
    original_request_bytes = archive.read(request_member)
    policy_bytes = archive.read('run/comparison-settings.json')
original = json.loads(original_request_bytes)['request']
product_path = Path(sys.argv[3])
request_path = Path(sys.argv[4])
core = json.loads(product_path.read_bytes())
job = json.loads(request_path.read_bytes())['request']

def physical_settings(settings):
    analysis = {key: value for key, value in settings['workbenchSettings'].items()
                if key not in {'FourierSamplingMode', 'FourierMaximumSampling', 'FourierRelativeAccuracy'}}
    return {**settings, 'workbenchSettings': analysis}

assert physical_settings(job) == physical_settings(original)
assert original['canonicalAnalysisKey'] == 'MTF' and original['maximumFrequency'] == 50
assert original['configuration'] == original['field'] == original['wavelength'] == 1
assert not original['polarization'] and not original['useRayAiming'] and original['surface'] == -1
assert core['outcome'] == 'Success'
certificate = core['values']['FourierSamplingValidation']
assert certificate['validated'] and certificate['returnedPupilSampling'] == 4096
assert certificate['relativeRmsAccuracy'] == .003
assert all(step['phaseResolved'] and step['usesStreamedPhysicalPupil'] for step in certificate['steps'][-2:])

# The OTF is conjugate to lateral direction cosines on the actual image
# plane, so its scale uses transverse chief-relative marginal components.
transverse_na = np.abs((edges-dchief)[np.arange(4), [0, 0, 1, 1]]).reshape(2, 2).mean(axis=1)
fourier_f = .5/transverse_na
frequencies = np.linspace(0, 50, 300)
rules = {size: roots_legendre(size) for size in (64, 96, 112, 160, 192, 224, 256)}

def autocorrelation(frequency, axis, radial, transverse, sphere=radius):
    lag = 2*(wave*.001)*fourier_f[axis]*frequency
    if lag == 0:
        return 1+0j
    theta_end = np.arccos(lag/2)
    theta, theta_weight = rules[radial]
    side, side_weight = rules[transverse]
    theta = (theta+1)*(theta_end/2)
    theta_weight = theta_weight*(theta_end/2)

    # The two half sections cover exactly the overlap of two unit disks
    # centered at +/-lag/2. The cosine coordinate removes the endpoint
    # square root, leaving a smooth sine-squared Jacobian.
    u = np.cos(theta)-lag/2
    h = np.sin(theta)
    along = np.broadcast_to(np.stack((-u, u))[:, :, None], (2, radial, transverse)).ravel()
    cross = np.broadcast_to((h[:, None]*side)[None, :, :], (2, radial, transverse)).ravel()
    weights = np.broadcast_to(((theta_weight*h*h)[:, None]*side_weight)[None, :, :],
                              (2, radial, transverse)).ravel()
    assert abs(np.sum(weights)-2*(theta_end-(lag/2)*np.sqrt(1-(lag/2)**2))) < 2e-13
    if axis == 0:
        left = phase(trace(along-lag/2, cross), sphere)
        right = phase(trace(along+lag/2, cross), sphere)
    else:
        left = phase(trace(cross, along-lag/2), sphere)
        right = phase(trace(cross, along+lag/2), sphere)
    return np.sum(weights*np.exp(2j*np.pi*(left-right)))/np.pi

gate = dict(json.loads(policy_bytes)['analyses']['MTF']['quantities']['Modulation'], minimumCoverage=.95)
assert gate['nrmse'] == .01

def metric(candidate, reference):
    error = np.abs(candidate-reference)
    assert np.all(np.isfinite(error))
    nrms = float(np.sqrt(np.mean(error**2))/max(np.max(np.abs(reference)), gate['absolute']))
    pointwise = bool(np.all(error <= gate['absolute']+gate['relative']*np.abs(reference)))
    verdict = 'Pass' if pointwise or nrms <= gate['nrmse'] else 'Close' if nrms <= gate['closeNrmse'] else 'Difference'
    return {'count': int(error.size), 'coverage': 1, 'maximumAbsolute': float(error.max()),
            'worstFrequencyCyclesPerMillimeter': float(frequencies[error.argmax()]),
            'nrmse': nrms, 'pointwisePass': pointwise, 'conclusion': verdict}

assert len(core['seriesList']) == 2
rows = []
for series_index, axis, label, coarse_counts, fine_counts in [(0, 1, 'T', (192, 224), (224, 256)),
                                                               (1, 0, 'S', (112, 160), (192, 224))]:
    series = core['seriesList'][series_index]
    assert series['name'].endswith(', '+label) and len(series['points']) == 300
    assert series['xQuantity'] == 'SpatialFrequency' and series['xUnit'] == 'CyclesPerMillimeter'
    assert series['yQuantity'] == 'Modulation' and series['yUnit'] == 'Dimensionless'
    product_curve = np.asarray([[point['x'], point['y']] for point in series['points']])
    assert np.max(np.abs(product_curve[:, 0]-frequencies)) < 2e-14
    coarse = np.array([autocorrelation(f, axis, *coarse_counts) for f in frequencies])
    fine = np.array([autocorrelation(f, axis, *fine_counts) for f in frequencies])
    convergence = metric(coarse, fine)
    comparison = metric(product_curve[:, 1], np.abs(fine))
    assert convergence['nrmse'] < 1e-8 and comparison['conclusion'] == 'Pass'
    # Resample a separate analytic evaluation on the 4096 correlation knots.
    # This isolates the near-DC spline contribution from finite-pupil sampling.
    spacing = 1/(4095*(wave*.001)*fourier_f[axis])
    knots = np.arange(33)*spacing
    knot_otf = [autocorrelation(f, axis, 64, 96) for f in knots]
    f1 = frequencies[1]
    spline_value = float(abs(CubicSpline(knots, knot_otf, bc_type='natural')(f1)))
    phase_control = float(abs(autocorrelation(f1, axis, 64, 96, parax_R)))
    rows.append({'axis': label, 'fourierFNumber': float(fourier_f[axis]),
                 'quadrature': [list(coarse_counts), list(fine_counts)],
                 'independentComplexOtfConvergence': convergence,
                 'workbenchVersusIndependentModulation': comparison,
                 'referenceModulation': np.abs(fine).tolist(),
                 'nearDcControl': {'frequencyCyclesPerMillimeter': f1, 'correlationKnotPitch': spacing,
                                  'workbench': product_curve[1, 1], 'continuous': float(abs(fine[1])),
                                  'nominalSphereContinuous': phase_control, 'analyticKnotNaturalSpline': spline_value}})

report = {
    'index': 1205, 'lens': lens, 'frozenVerdict': 'Incomparable', 'sourceSha256': sha(source),
    'frozenArchiveSha256': frozen_sha, 'frozenRequestMember': request_member,
    'frozenRequestSha256': sha(original_request_bytes), 'frozenComparisonPolicySha256': sha(policy_bytes),
    'originalGate': gate, 'nativeWavefrontMember': native_wavefront, 'nativeWavefrontSha256': sha(phase_bytes),
    'nativePhaseNodes': int(mask.sum()), 'nativeReferenceSphereMm': float(radius),
    'nativeWavefrontMaximumResidualWaves': reference_phase_error,
    'frequencyCyclesPerMillimeter': frequencies.tolist(), 'curves': rows,
    'workbenchDataSha256': archive_sha(product_path), 'workbenchRequestSha256': archive_sha(request_path),
    'workbenchProvenance': json.loads(product_path.with_name('provenance.json').read_bytes()),
    'workbenchCertificate': certificate
}
output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')

