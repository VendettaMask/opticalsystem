"""Independently integrate the two complete #2164 encircled energy curves."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from zipfile import ZipFile

import numpy as np
from numpy.polynomial.chebyshev import chebvander
from scipy.interpolate import CubicSpline
from scipy.special import roots_legendre


parser = argparse.ArgumentParser()
parser.add_argument('repo', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--finite-period', type=int, action='append', default=[])
args = parser.parse_args()
repo = args.repo
wave = .55
lens = 'Optical testing/Twyman Green Interferometer.zmx'
source = (repo/'src/OpticalSystemDesign.App/Assets/SequentialExamples'/lens).read_bytes()
sha = lambda payload: hashlib.sha256(payload).hexdigest()
assert sha(source) == '975be62693a8c3d8870c2ee3fd01e2f51de37d558afe7265cd939fe262f49051'
snapshot_archive = 'validation/rechecks/2026-11-01/testing-three/evidence.zip'
snapshot_sha = 'e0a8e954c24b6d12150cae65f55d66898c617110b963c022a43fff0b34e32cf2'
snapshot_member = 'payloads/'+snapshot_sha
with ZipFile(repo/snapshot_archive) as archive:
    snapshot = archive.read(snapshot_member)
assert sha(snapshot) == snapshot_sha
model = json.loads(snapshot)
frozen_path = repo/'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
with frozen_path.open('rb') as stream:
    frozen_sha = hashlib.file_digest(stream, 'sha256').hexdigest()
assert frozen_sha == '1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad'
with ZipFile(frozen_path) as archive:
    def read_case(index, suffix):
        member = next(p for p in archive.namelist() if f'/{index}-' in p and p.endswith('/'+suffix))
        return member, archive.read(member)

    request_member, request_raw = read_case(2164, 'workbench/request.json')
    settings_member, settings_raw = read_case(2164, 'native/captured-settings.json')
    native_member, native_raw = read_case(2164, 'native/data.json')
    wavefront_member, wavefront_raw = read_case(2156, 'native/data.json')
    ray_member, ray_raw = read_case(2152, 'native/data.txt')
    policy_raw = archive.read('run/comparison-settings.json')
original = json.loads(request_raw)['request']
settings = json.loads(settings_raw)['properties']
messages = json.loads(native_raw)['messages']
assert original['canonicalAnalysisKey'] == 'Diffraction Encircled Energy'
assert original['configuration'] == original['field'] == original['wavelength'] == 1
assert original['reference'] == 'Centroid' and original['surface'] == -1
assert not original['polarization'] and not original['useRayAiming']
assert original['workbenchSettings']['MaximumDistanceMicrometers'] == '5'
assert original['workbenchSettings']['NumPoints'] == '401'
assert json.loads(original['apodization'])['numbers'] == {'type': 2, 'factor': 0}
assert settings['ReferTo'] == 'Centroid' and settings['RadiusMaximum'] == 5
assert not settings['UseHuygensPSF'] and settings['ShowDiffractionLimit'] and messages
gate = dict(json.loads(policy_raw)['analyses']['Diffraction Encircled Energy']['quantities']['EnergyFraction'],
            minimumCoverage=.95)
assert gate['nrmse'] == .003
radii = np.arange(401)*5/400

# The folded real-ray prescription is the one validated independently for
# #2161. Track signed BK7 distances and a finite object shift for EE.
bk7 = np.sqrt(1+np.sum(np.array([1.03961212, .231792344, 1.01046945])*wave**2 /
                       (wave**2-np.array([.00600069867, .0200179144, 103.560653]))))
media = {'Air': 1., 'BK7': bk7}
surfaces = []
for surface in model['surfaces'][1:]:
    frame = surface['coordinateSystem']
    assert frame['originX'] == frame['rotationYDegrees'] == frame['rotationZDegrees'] == 0
    angle = np.deg2rad(frame['rotationXDegrees'])
    c, s = np.cos(angle), np.sin(angle)
    rotation = np.array([[1., 0., 0.], [0., c, -s], [0., s, c]])
    origin = np.array([frame['originX'], frame['originY'], frame['originZ']])
    components = surface['components']
    assert components['coatingKind'] == 'none'
    assert components['geometry']['numbers'].get('tangentX', 0) == 0
    aperture = components['physicalAperture']
    if aperture:
        assert aperture['kind'] == 'circular' and set(aperture['numbers']) == {'radius'}
    surfaces.append((components, origin, rotation))
assert len(surfaces) == 16 and np.all(surfaces[0][1] == 0)
glass = next(components['materialAfterComponent']['numbers'] for components, _, _ in surfaces
             if components['materialAfter'] == 'BK7')
k = np.interp(wave*1000, [glass[f'kw{i}'] for i in range(21)], [glass[f'k{i}'] for i in range(21)])
absorption = 2*np.pi*k/(wave*.001)


def dot(a, b):
    return np.sum(a*b, axis=-1)


def normalize(vector):
    return vector/np.linalg.norm(vector, axis=-1)[..., None]


def sag(x, y, geometry):
    kind, parameters = geometry['kind'], geometry['numbers']
    if kind in ('plane', 'tilted_plane'):
        tx, ty = parameters.get('tangentX', 0), parameters.get('tangentY', 0)
        return x*tx+y*ty, np.zeros_like(x)+tx, np.zeros_like(y)+ty
    radius, conic = parameters['radius'], parameters['conic']
    squared = x*x+y*y
    root = np.sqrt(1-(1+conic)*squared/radius**2)
    height = squared/(radius*(1+root))
    derivative = 1/(2*radius*root)
    if kind == 'even_asphere':
        for j in range(8):
            coefficient = parameters.get(f'c{j}', 0)
            if coefficient:
                height += coefficient*squared**(j+1)
                derivative += (j+1)*coefficient*squared**j
    else:
        assert kind == 'standard'
    return height, 2*x*derivative, 2*y*derivative


def trace(px, py, object_y=0):
    px, py = np.broadcast_arrays(px, py)
    direction = normalize(np.stack((.5*px, .5*py-object_y/10, np.ones_like(px)), axis=-1))
    point = np.broadcast_to([0., object_y, -10.], direction.shape).copy()
    path, glass_distance = np.zeros(px.shape), np.zeros(px.shape)
    boundaries = []
    for components, origin, rotation in surfaces:
        position = (point-origin)@rotation
        ray = direction@rotation
        geometry = components['geometry']
        distance = -position[..., 2]/ray[..., 2]
        for _ in range(8):
            hit = position+distance[..., None]*ray
            height, sx, sy = sag(hit[..., 0], hit[..., 1], geometry)
            error = hit[..., 2]-height
            distance -= error/(ray[..., 2]-sx*ray[..., 0]-sy*ray[..., 1])
            if np.max(abs(error)) < 1e-12:
                break
        hit = position+distance[..., None]*ray
        height, sx, sy = sag(hit[..., 0], hit[..., 1], geometry)
        assert np.max(abs(hit[..., 2]-height)) < 1e-10
        normal = normalize(np.stack((-sx, -sy, np.ones_like(sx)), axis=-1))
        before, after = components['materialBefore'], components['materialAfter']
        incoming, outgoing = media[before], media[after]
        path += incoming*distance
        if before == 'BK7':
            glass_distance += distance
        if components['interactionKind'] == 'reflective':
            ray -= (2*dot(ray, normal))[..., None]*normal
        elif incoming != outgoing:
            normal = np.where((dot(ray, normal)>0)[..., None], -normal, normal)
            cosine = -dot(ray, normal)
            ratio = incoming/outgoing
            discriminant = 1-ratio**2*(1-cosine*cosine)
            assert np.all(discriminant > 0)
            ray = ratio*ray+(ratio*cosine-np.sqrt(discriminant))[..., None]*normal
        point, direction = hit@rotation.T+origin, ray@rotation.T
        aperture = components['physicalAperture']
        if aperture:
            boundaries.append(np.hypot(hit[..., 0], hit[..., 1])/aperture['numbers']['radius'])
    return hit, ray, path, boundaries, glass_distance


chief, chief_direction, chief_path, _, _ = trace(0., 0.)
_, marginal, _, _, _ = trace([1, -1, 0, 0], [0, 0, 1, -1])
na = np.linalg.norm(np.cross(chief_direction, marginal), axis=-1).reshape(2, 2).mean(axis=1)
anchor = json.loads((Path(__file__).resolve().parent.parent/'twyman-streamed-fourier/2161-independent-receipt.json').read_bytes())
assert anchor['sourceSha256'] == sha(source) and anchor['snapshotSha256'] == snapshot_sha
assert anchor['nativeInputsSha256']['2156'] == sha(wavefront_raw) and anchor['nativeInputsSha256']['2152'] == sha(ray_raw)
native_radius = anchor['virtualSphereRadiusMm']
# A real finite object ray through the fixed stop center perturbs the
# differential exit pupil on the image meridian. Preserve the virtual sign.
probe_offset = np.copysign(10e-6, -chief[1])
probe, direction, _, _, _ = trace(0., 0., probe_offset)
slope_difference = direction[1]/direction[2]-chief_direction[1]/chief_direction[2]
signed_radius = (probe[1]-chief[1])/(slope_difference*chief_direction[2])
assert signed_radius < 0
nominal_radius = abs(signed_radius)


def optical(px, py, sphere):
    point, direction, path, boundary, glass_distance = trace(px, py)
    offset = point-chief
    longitudinal = dot(direction, offset)
    impact = dot(offset, offset)-longitudinal**2
    correction = impact/(sphere+np.sqrt(sphere*sphere-impact))
    opd = -(path-chief_path-longitudinal-correction)/(wave*.001)
    power = (1+.25*(px*px+py*py))**-1.5*np.exp(-2*absorption*glass_distance)
    return opd, power, boundary, glass_distance


edge, edge_direction, _, _, _ = trace(0., .8)
native_ray = ray_raw.decode('utf-16' if ray_raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')
ray = np.array([float(v) for v in re.search(r'(?m)^\s*20\t([^\r\n]+)', native_ray).group(1).split()[:6]])
ray_residual = np.abs(np.r_[edge, edge_direction]-ray)
assert ray_residual[:3].max() < 2e-12 and ray_residual[3:].max() < 6e-11
native_phase = np.asarray(json.loads(wavefront_raw)['dataGrids'][0]['values'], dtype=float)
valid = np.isfinite(native_phase)
row, col = np.indices(native_phase.shape)
px, py = (col[valid]-32)/31, (row[valid]-32)/31
calculated, *_ = optical(px, py*anchor['wavefrontTangentialPupilStretch'], native_radius)
phase_residual = float(np.max(np.abs(calculated+anchor['wavefrontPistonWaves']-native_phase[valid])))
assert valid.sum() == 2999 and phase_residual < 1e-8


def pupil_rule(radial_order, angle_order):
    radial, weight = roots_legendre(radial_order)
    radial = (radial+1)/2
    angle = np.arange(angle_order)*2*np.pi/angle_order
    px = (radial[:, None]*np.cos(angle)).ravel()
    py = (radial[:, None]*np.sin(angle)).ravel()
    weights = np.pi*np.repeat(weight*radial/angle_order, angle_order)
    return px, py, weights


def gradient_centroid(radial_order, angle_order, step, sphere):
    px, py, weights = pupil_rule(radial_order, angle_order)
    opd, power, boundary, path = optical(px, py, sphere)
    positive, *_ = optical(px, py+step, sphere)
    negative, *_ = optical(px, py-step, sphere)
    opposite, opposite_power, _, _ = optical(-px, py, sphere)
    assert np.max(abs(opposite-opd)) < 1e-8 and np.max(abs(opposite_power-power)) < 1e-12
    assert all(np.max(b) < .84 for b in boundary)
    amplitude_integral, power_integral = weights@np.sqrt(power), weights@power
    centroid_y = -(wave/na[1])*((weights*power)@((positive-negative)/(2*step)))/power_integral
    return {'centroidMicrometers': [0., float(centroid_y)], 'pupilAmplitudeIntegral': float(amplitude_integral),
            'pupilPowerIntegral': float(power_integral), 'maximumRelativeApertureRadii': [float(b.max()) for b in boundary],
            'signedBk7PathRangeMm': [float(path.min()), float(path.max())]}


gradient_orders = [(64, 128, 1e-4), (96, 192, 5e-5), (128, 256, 2.5e-5)]
native_centers = [gradient_centroid(*order, native_radius) for order in gradient_orders]
nominal_centers = [gradient_centroid(*order, nominal_radius) for order in gradient_orders]
amplitude_integral = native_centers[-1]['pupilAmplitudeIntegral']
power_integral = native_centers[-1]['pupilPowerIntegral']
total = wave**2/np.prod(na)*power_integral/amplitude_integral**2


def curve(radial_order, angle_order, degree, radial_samples, azimuth_samples, center, sphere, ideal=False):
    px, py, weights = pupil_rule(radial_order, angle_order)
    knots = np.cos(np.pi*(np.arange(degree)+.5)/degree)
    xx, yy = center[0]+5*knots, center[1]+5*knots
    field = np.zeros((degree, degree), complex)
    kernel = -2j*np.pi/wave
    for index in range(0, len(px), 4096):
        part = slice(index, index+4096)
        opd, power, boundary, _ = optical(px[part], py[part], sphere)
        assert all(np.max(b) < .84 for b in boundary)
        amplitude = weights[part]*np.sqrt(power)/amplitude_integral
        if not ideal:
            amplitude = amplitude*np.exp(-2j*np.pi*opd)
        ex = np.exp(kernel*px[part, None]*xx*na[0])
        ey = np.exp(kernel*yy[:, None]*py[part]*na[1])
        field += ey@(amplitude[:, None]*ex)
    inverse = np.linalg.inv(chebvander(knots, degree-1))
    polynomial = inverse@field@inverse.T
    radial = np.linspace(0, 5, radial_samples)
    azimuth = np.arange(azimuth_samples)*2*np.pi/azimuth_samples
    average = np.empty_like(radial)
    for index in range(0, len(radial), 64):
        part = slice(index, index+64)
        vx = chebvander((radial[part, None]*np.cos(azimuth)/5).ravel(), degree-1)
        vy = chebvander((radial[part, None]*np.sin(azimuth)/5).ravel(), degree-1)
        amplitude = np.sum((vx@polynomial.T)*vy, axis=1)
        average[part] = np.mean(abs(amplitude.reshape(-1, azimuth_samples))**2, axis=1)
    primitive = CubicSpline(radial, 2*np.pi*radial*average/total).antiderivative()
    result = primitive(radii)-primitive(0)
    assert result[0] == 0 and np.all(np.diff(result) > 0) and result[-1] < 1
    return result


def metric(candidate, expected):
    error = np.abs(candidate-expected)
    nrmse = float(np.sqrt(np.mean(error**2))/max(expected.max(), gate['absolute']))
    pointwise = bool(np.all(error <= gate['absolute']+gate['relative']*np.abs(expected)))
    return {'count': len(error), 'coverage': 1, 'nrmse': nrmse, 'maximumAbsolute': float(error.max()),
            'pointwisePass': pointwise, 'conclusion': 'Pass' if pointwise or nrmse <= gate['nrmse'] else 'Difference'}


def period_moment(size):
    # The complete Y marginal follows from Parseval in X. Mirror symmetry
    # in X permits each positive-X row to represent its negative partner.
    step = 2/(size-2)
    axis = np.arange(-size//2+1, size//2)*step
    x_positive = axis[axis >= 0]
    logical = 1 << (2*size-1).bit_length()
    spectrum = np.zeros(logical)
    x_nyquist_amplitudes = np.zeros(len(axis), complex)
    count, power_sum, peak_phase, aperture_max = 0, 0., 0., 0.
    for index in range(0, len(x_positive), 32):
        py, px = np.meshgrid(axis, x_positive[index:index+32], indexing='xy')
        mask = px**2+py**2 <= 1
        opd = np.full(mask.shape, np.nan)
        field = np.zeros(mask.shape, complex)
        phase, power, boundary, _ = optical(px[mask], py[mask], nominal_radius)
        opd[mask] = phase
        peak_phase = max(peak_phase, float(np.nanmax(abs(np.diff(opd, axis=1)))))
        aperture_max = max(aperture_max, *(float(values.max()) for values in boundary))
        field[mask] = np.sqrt(power)*np.exp(-2j*np.pi*phase)
        symmetry = np.where(px[:, 0] == 0, 1, 2)
        count += int(symmetry@np.count_nonzero(mask, axis=1))
        power_sum += float(symmetry@np.sum(abs(field)**2, axis=1))
        spectrum += symmetry@abs(np.fft.fft(field, n=logical, axis=1))**2
        parity = 1-2*(np.arange(index, index+len(symmetry)) % 2)
        x_nyquist_amplitudes += (symmetry*parity)@field
    frequency = np.fft.fftfreq(logical)
    frequency[logical//2] = .5
    scale = wave/(na[1]*step)
    y = float(spectrum@frequency/spectrum.sum()*scale)
    nyquist_shift = float(.5*scale*spectrum[logical//2]/spectrum.sum())
    error = float(abs(spectrum.sum()/(logical*power_sum)-1))
    # All signed X frequencies cancel in pairs except the independent
    # positive Nyquist endpoint. Evaluate just that alternating Fourier sum.
    x = float(.5*wave/(na[0]*step)*np.sum(abs(x_nyquist_amplitudes)**2)/(logical*power_sum))
    assert error < 1e-12 and aperture_max < .84
    return {'physicalPupilSampling': size, 'logicalFourierSize': logical, 'transmittedNodes': count,
            'centroidMicrometers': [x, y], 'signedYPositiveNyquistBiasMicrometers': nyquist_shift,
            'yMarginalParsevalRelativeError': error, 'maximumRelativeApertureRadius': aperture_max,
            'rawMaximumAdjacentYPhaseWaves': peak_phase}


actual_orders = [(768, 1536, 48, 1001, 96), (1024, 2048, 64, 1601, 144)]
ideal_orders = [(48, 96, 48, 1001, 96), (64, 128, 64, 1601, 144)]
actual_center = native_centers[-1]['centroidMicrometers']
actual_coarse, actual = [curve(*order, actual_center, native_radius) for order in actual_orders]
ideal_coarse, ideal = [curve(*order, [0., 0.], native_radius, True) for order in ideal_orders]
nominal_order = (768, 1536, 64, 1601, 144)
alternate = curve(*nominal_order, nominal_centers[-1]['centroidMicrometers'], nominal_radius)
assert metric(actual_coarse, actual)['nrmse'] < 1e-7 and metric(ideal_coarse, ideal)['nrmse'] < 1e-7
finite_centers = []
finite_curves = []
for size in args.finite_period:
    assert size >= 256 and size % 256 == 0
    witness = period_moment(size)
    shifted = curve(*nominal_order, witness['centroidMicrometers'], nominal_radius)
    witness['energySensitivityVersusNativeGradientCenter'] = metric(shifted, actual)
    finite_centers.append(witness)
    finite_curves.append(shifted)

report = {
    'index': 2164, 'lens': lens, 'sourceSha256': sha(source), 'frozenVerdict': 'Incomparable',
    'snapshotArchive': snapshot_archive, 'snapshotMember': snapshot_member, 'snapshotSha256': snapshot_sha,
    'frozenArchiveSha256': frozen_sha, 'frozenRequestMember': request_member, 'frozenRequestSha256': sha(request_raw),
    'frozenNativeSettingsMember': settings_member, 'frozenNativeSettingsSha256': sha(settings_raw),
    'frozenNativeDataMember': native_member, 'frozenNativeDataSha256': sha(native_raw), 'frozenDiagnostics': messages,
    'frozenComparisonPolicySha256': sha(policy_raw), 'originalGate': gate,
    'nativeWavefrontMember': wavefront_member, 'nativeWavefrontSha256': sha(wavefront_raw),
    'nativeWavefrontNodes': int(valid.sum()), 'nativePhaseMaximumResidualWaves': phase_residual,
    'nativeRealRayMember': ray_member, 'nativeRealRaySha256': sha(ray_raw),
    'nativeRayPositionMaximumResidualMm': float(ray_residual[:3].max()),
    'nativeRayDirectionMaximumResidual': float(ray_residual[3:].max()),
    'wavelengthMicrometers': wave, 'directionalWorkingFNumbersXY': (.5/na).tolist(),
    'bk7ExtinctionAtWavelength': k, 'unitPeakIrradianceParsevalTotalPowerMicrometersSquared': total,
    'actualCentroid': {'phaseGradientOrders': gradient_orders, 'nativeSphereRadiusMm': native_radius,
                       'nativeSphereControls': native_centers, 'nominalEnergySphereRadiusMm': nominal_radius,
                       'objectYParabasalShiftMm': probe_offset, 'nominalSphereControls': nominal_centers,
                       'nominalSphereFullFourierPeriods': finite_centers,
                       'finestFinitePeriodEnergyDifference': metric(*finite_curves[-2:]) if len(finite_curves)>1 else None},
    'radiusMicrometers': radii.tolist(),
    'components': [
        {'name': 'DiffractionLimit', 'centroidMicrometers': [0., 0.], 'quadratureOrders': ideal_orders,
         'quadrature': metric(ideal_coarse, ideal), 'referenceEnergyFraction': ideal.tolist()},
        {'name': 'Actual', 'centroidMicrometers': actual_center, 'quadratureOrders': actual_orders,
         'quadrature': metric(actual_coarse, actual), 'nominalEnergySphereControl': metric(alternate, actual),
         'referenceEnergyFraction': actual.tolist()}
    ],
    'independentReferenceComplete': True
}
args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
