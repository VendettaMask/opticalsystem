"""Reconstruct the original #1203 FFT PSF using independently traced phase."""
import hashlib
import json
import re
import sys
from pathlib import Path
from zipfile import ZipFile

import numpy as np
from scipy.special import roots_legendre
from scipy.spatial.transform import Rotation

repo = Path(sys.argv[1])
output = Path(sys.argv[2])
axis_requested = '--axis' in sys.argv[5:]
lens = 'Miscellaneous/Irregular surface.ZMX'
source = (repo / 'src/OpticalSystemDesign.App/Assets/SequentialExamples' / lens).read_bytes()
validation = repo / 'validation'
frozen_archive = validation / 'repairs/2026-10-21/sequential-corpus-full-evidence.zip'
package = validation / 'rechecks/2026-11-08/irregular-fft-psf-reference'
native_wavefront = 'run/analyses/1202-038-irregular-surface-c1-primary-f1-w1-wavefront/native/data.json'
native_ray = 'run/analyses/1198-038-irregular-surface-c1-primary-f1-w1-single-ray-trace/native/data.txt'
frozen_psf = ('run/analyses/1207-038-irregular-surface-c1-primary-f1-w1-fft-psf-cross-section/workbench/request.json'
              if axis_requested else 'run/analyses/1203-038-irregular-surface-c1-primary-f1-w1-psf/workbench/request.json')

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
    frozen_request_bytes = archive.read(frozen_psf)
    policy_bytes = archive.read('run/comparison-settings.json')
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


if axis_requested:
    # For automatic scale the original 2N transform has N visible positive
    # half-width knots after one Nyquist endpoint is wrapped. Cancel N to
    # obtain the full physical half span from the independently traced F/#.
    original = json.loads(frozen_request_bytes)['request']
    product_path, request_path = Path(sys.argv[3]), Path(sys.argv[4])
    job = json.loads(request_path.read_bytes())['request']
    core = json.loads(product_path.read_bytes())
    def physical_settings(settings):
        analysis = {k: v for k, v in settings['workbenchSettings'].items()
                    if k not in {'FourierSamplingMode','FourierMaximumSampling','FourierRelativeAccuracy'}}
        return {**settings,'workbenchSettings':analysis}

    assert physical_settings(job) == physical_settings(original)
    assert original['canonicalAnalysisKey'] == 'FFT PSF Cross Section'
    assert original['configuration'] == original['field'] == original['wavelength'] == 1
    assert original['workbenchSettings']['GraphScaleMicrometers'] == '0'
    assert original['workbenchSettings']['Type'].startswith('X') and original['workbenchSettings']['Row'] == '中心'
    assert core['outcome'] == 'Success' and len(core['seriesList']) == 1
    certificate = core['values']['FourierSamplingValidation']
    assert certificate['validated'] and all(s['phaseResolved'] for s in certificate['steps'][-2:])
    series = core['seriesList'][0]
    assert series['xUnit'] == 'Micrometer' and series['yQuantity'] == 'Irradiance'
    assert series['yUnit'] == 'Dimensionless'
    points = np.array([[p['x'],p['y']] for p in series['points']])
    axis_na = np.sqrt((edge_na**2).mean(axis=1))
    half_span = wave*(.5/max(axis_na))*(original['pupilSampling']-2)/2
    x = np.linspace(-half_span, half_span, 201)
    coordinate_residual = float(np.max(abs(x-points[:,0])))
    assert len(points) == 201 and coordinate_residual < 3e-10

    def integrate_axis(radial_count, azimuth_count):
        radial, weight = roots_legendre(radial_count)
        radial = (radial+1)/2
        angle = np.arange(azimuth_count)*(2*np.pi/azimuth_count)
        px = (radial[:,None]*np.cos(angle)).ravel()
        py = (radial[:,None]*np.sin(angle)).ravel()
        weight = np.repeat(weight*radial/azimuth_count, azimuth_count)
        assert abs(np.sum(weight)-1) < 1e-12
        field = np.zeros(x.size, complex)
        for j in range(0,len(px),4096):
            chunk = slice(j,j+4096)
            amplitude = weight[chunk]*np.exp(-2j*np.pi*phase(trace(px[chunk],py[chunk]),radius))
            kernel = np.exp(-2j*np.pi/wave*px[chunk,None]*x*axis_na[0])
            field += amplitude @ kernel
        return np.abs(field)**2

    gate = dict(json.loads(policy_bytes)['analyses']['FFT PSF Cross Section']['quantities']['Irradiance'],
                minimumCoverage=.95)
    assert gate['nrmse'] == .003

    def axis_metric(candidate,reference):
        error = np.abs(candidate-reference)
        assert np.all(np.isfinite(error))
        rms = float(np.sqrt(np.mean(error**2))/max(np.max(np.abs(reference)),gate['absolute']))
        pointwise = bool(np.all(error <= gate['absolute']+gate['relative']*np.abs(reference)))
        verdict = 'Pass' if pointwise or rms <= gate['nrmse'] else 'Close' if rms <= gate['closeNrmse'] else 'Difference'
        return {'count':int(error.size),'coverage':1,'maximumAbsolute':float(error.max()),
                'nrmse':rms,'pointwisePass':pointwise,'conclusion':verdict}

    coarse = integrate_axis(768,1536)
    fine = integrate_axis(1024,2048)
    convergence = axis_metric(coarse,fine)
    comparison = axis_metric(points[:,1],fine)
    assert convergence['nrmse'] < 1e-7 and comparison['conclusion'] == 'Pass'
    report = {
        'index':1207,'lens':lens,'frozenVerdict':'Incomparable','sourceSha256':sha(source),
        'frozenArchiveSha256':frozen_sha,'frozenRequestMember':frozen_psf,
        'frozenRequestSha256':sha(frozen_request_bytes),'frozenComparisonPolicySha256':sha(policy_bytes),
        'nativeWavefrontMember':native_wavefront,'nativeWavefrontSha256':sha(phase_bytes),
        'nativeReferenceSphereRadiusMm':float(radius),'maximumNativeOpdResidualWaves':reference_phase_error,
        'originalGate':gate,'profileAxisMicrometers':x.tolist(),'referenceIrradiance':fine.tolist(),
        'maximumAutoAxisCoordinateResidualMicrometers':coordinate_residual,
        'independentQuadratureConvergence':convergence,'workbenchVersusIndependentReference':comparison,
        'workbenchDataSha256':archive_sha(product_path),'workbenchRequestSha256':archive_sha(request_path),
        'workbenchProvenance':json.loads(product_path.with_name('provenance.json').read_bytes()),
        'workbenchCertificate':certificate
    }
    output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    raise SystemExit(0)


if __name__ == '__main__':
    core_archive = package/'workbench-evidence.zip'
    with ZipFile(core_archive) as archive:
        manifest = json.loads(archive.read('manifest.json'))
        for name, digest in manifest.items():
            assert sha(archive.read(name)) == digest
        core = json.loads(archive.read('refined/data.json'))
        original_default = json.loads(archive.read('default/data.json'))
        provenance = json.loads(archive.read('refined/provenance.json'))
        refined_job = json.loads(archive.read('refined/request.json'))['request']
        default_job = json.loads(archive.read('default/request.json'))['request']

    original = json.loads(frozen_request_bytes)['request']
    def physical_settings(job):
        original_options = {key: value for key, value in job['workbenchSettings'].items()
                            if key not in {'FourierSamplingMode', 'FourierMaximumSampling','FourierRelativeAccuracy'}}
        return {**job, 'workbenchSettings': original_options}
    assert physical_settings(refined_job) == physical_settings(default_job) == physical_settings(original)
    assert original['canonicalAnalysisKey'] == 'PSF' and original['imageSampling'] == 128
    assert original['configuration'] == original['field'] == original['wavelength'] == 1
    assert original['imageDeltaMicrometers'] == .25 and original['reference'] == 'ChiefRay'
    assert original['surface'] == -1 and not original['polarization'] and not original['useRayAiming']
    assert original['normalization'] == 'NativePhysical' and original['workbenchSettings']['Normalized'] == 'False'
    assert core['outcome'] == 'Success' and original_default['outcome'] == 'Unavailable'
    cert = core['values']['FourierSamplingValidation']
    default_cert = original_default['values']['FourierSamplingValidation']
    assert cert['validated'] and cert['returnedPupilSampling'] == 8192 and cert['relativeRmsAccuracy'] == .003
    assert not default_cert['validated'] and default_cert['relativeRmsAccuracy'] == .001
    assert [entry['pupilSampling'] for entry in cert['steps'][-2:]] == [7168,8192]
    assert all(entry['phaseResolved'] and entry['usesStreamedPhysicalPupil'] for entry in cert['steps'][-2:])
    assert core['values']['StreamedFourierPupil']['tracedNodes'] == core['values']['StreamedFourierPupil']['transmittedNodes']

    series = core['seriesList'][0]
    assert len(core['seriesList']) == 1 and series['xUnit'] == series['yUnit'] == 'Micrometer'
    assert series['valueQuantity'] == 'Irradiance' and series['valueUnit'] == 'Dimensionless'
    points = np.array([[p[k] for k in ('x','y','value')] for p in series['points']]).reshape(128,128,3)
    x = (np.arange(128)-63)*.25
    xx, yy = np.meshgrid(x,x)
    assert np.array_equal(xx,points[:,:,0]) and np.array_equal(yy,points[:,:,1])
    assert np.all(np.isfinite(points[:,:,2]))
    axis_na = np.sqrt(np.mean(edge_na**2,axis=1))
    working_f = .5/np.sqrt(np.mean(edge_na**2))
    assert abs(core['values']['WorkingFNumber']-working_f) < 1e-8

    def integrate(radial, azimuth, sphere):
        r, weight = roots_legendre(radial)
        r = (r+1)/2
        theta = np.arange(azimuth)*(2*np.pi/azimuth)
        px = (r[:,None]*np.cos(theta)).ravel()
        py = (r[:,None]*np.sin(theta)).ravel()
        aperture_weights = np.repeat(weight*r/azimuth, azimuth)
        assert abs(np.sum(aperture_weights)-1) < 1e-12
        field = np.zeros((128,128),complex)
        kernel = -2j*np.pi/wave
        for start in range(0,len(px),4096):
            section = slice(start,start+4096)
            ex = np.exp(kernel*px[section,None]*x*axis_na[0])
            ey = np.exp(kernel*x[:,None]*py[section]*axis_na[1])
            phase_waves = phase(trace(px[section],py[section]),sphere)
            amplitudes = aperture_weights[section]*np.exp(-2j*np.pi*phase_waves)
            field += ey @ (amplitudes[:,None]*ex)
        return np.abs(field)**2

    gate = dict(json.loads(policy_bytes)['analyses']['PSF']['quantities']['Irradiance'], minimumCoverage=.95)
    assert gate['nrmse'] == .01
    def metric(a,b):
        error = np.abs(a-b)
        assert np.all(np.isfinite(error))
        nrms = float(np.sqrt(np.mean(error**2))/max(np.max(np.abs(b)),gate['absolute']))
        pointwise = bool(np.all(error <= gate['absolute']+gate['relative']*np.abs(b)))
        verdict = 'Pass' if pointwise or nrms <= gate['nrmse'] else 'Close' if nrms <= gate['closeNrmse'] else 'Difference'
        return {'count': int(error.size), 'coverage': 1, 'maximumAbsolute': float(error.max()),
                'nrmse': nrms, 'pointwisePass': pointwise, 'conclusion': verdict}

    coarse = integrate(768,1536,radius)
    fine = integrate(1024,2048,radius)
    nominal_sphere_control = integrate(768,1536,parax_R)
    result = {
        'index': 1203, 'lens': lens, 'frozenVerdict': 'Incomparable', 'originalGate': gate,
        'sourceSha256': sha(source), 'frozenArchiveSha256': frozen_sha,
        'frozenComparisonPolicySha256': sha(policy_bytes),
        'frozenRequestMember': frozen_psf, 'frozenRequestSha256': sha(frozen_request_bytes),
        'nativeWavefrontMember': native_wavefront, 'nativeWavefrontSha256': sha(phase_bytes),
        'nativeSingleRayMember': native_ray, 'nativeSingleRaySha256': sha(native_ray_bytes),
        'nativeRayMaxInterceptDifferenceMm': float(error[:,:3].max()),
        'nativeRayMaxDirectionDifference': float(error[:,3:].max()),
        'apertureCertificate': {'maximumPupilHeightMm': pupil_radius, 'lensClearRadiusMm': semi,
                                'maxInternalIncidenceDegrees': float(np.rad2deg(incidence_limit)),
                                'criticalIncidenceDegrees': float(np.rad2deg(critical))},
        'refractiveIndex': float(index), 'workingFNumber': float(working_f),
        'nativePhaseNodes': int(mask.sum()), 'referenceSphereRadiusMm': float(radius),
        'oppositeEdgeRadiusMm': float(opposite), 'nominalParaxialRadiusMm': float(parax_R),
        'maximumNativeOpdResidualWaves': reference_phase_error, 'nominalSphereOpdResidualWaves': nominal_phase_error,
        'imageGrid': {'columns': 128, 'rows': 128, 'pixelPitchMicrometers': .25, 'first': -15.75, 'last': 16.0},
        'independentQuadratureConvergence': metric(coarse,fine),
        'nominalSphereControl': metric(nominal_sphere_control,coarse),
        'workbenchVersusIndependentReference': metric(points[:,:,2],fine),
        'workbenchArchiveSha256': archive_sha(core_archive), 'workbenchEvidenceManifest': manifest,
        'workbenchProvenance': provenance, 'workbenchAdaptiveRefined': cert, 'workbenchAdaptiveDefault': default_cert
    }
    assert result['workbenchVersusIndependentReference']['conclusion'] == 'Pass'
    assert result['independentQuadratureConvergence']['nrmse'] < 1e-7
    output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')

