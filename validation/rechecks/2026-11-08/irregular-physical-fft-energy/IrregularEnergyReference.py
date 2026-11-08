"""Independently reconstruct the two full original #1210 energy curves."""
import argparse
import json
from pathlib import Path
import runpy
from zipfile import ZipFile

import numpy as np
from numpy.polynomial.chebyshev import chebvander
from scipy.interpolate import CubicSpline
from scipy.special import j1, roots_legendre


parser = argparse.ArgumentParser()
parser.add_argument('repo', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
here = Path(__file__).resolve().parent
model = runpy.run_path(str(here.parent/'irregular-fft-psf-reference/IrregularReference.py'),
                       run_name='irregular_geometry')
trace, phase, sha = model['trace'], model['phase'], model['sha']
wave, na = model['wave'], model['directional_na']
native_radius, nominal_radius = model['radius'], model['parax_R']
total_power = wave**2/(np.pi*np.prod(na))
radii = np.arange(401)*5/400

with ZipFile(model['frozen_archive']) as archive:
    request_member = next(path for path in archive.namelist()
                          if '/1210-' in path and path.endswith('/workbench/request.json'))
    request_bytes = archive.read(request_member)
    settings_member = request_member.replace('/workbench/request.json', '/native/captured-settings.json')
    settings_bytes = archive.read(settings_member)
    data_member = request_member.replace('/workbench/request.json', '/native/data.json')
    data_bytes = archive.read(data_member)
original = json.loads(request_bytes)['request']
native = json.loads(settings_bytes)['properties']
messages = json.loads(data_bytes)['messages']
assert original['canonicalAnalysisKey'] == 'Diffraction Encircled Energy'
assert original['configuration'] == original['field'] == original['wavelength'] == 1
assert original['reference'] == 'Centroid' and original['surface'] == -1
assert not original['polarization'] and not original['useRayAiming']
assert original['workbenchSettings']['MaximumDistanceMicrometers'] == '5'
assert original['workbenchSettings']['NumPoints'] == '401'
assert native['ReferTo'] == 'Centroid' and native['RadiusMaximum'] == 5
assert not native['UseHuygensPSF'] and native['ShowDiffractionLimit'] and messages
policy = model['policy_bytes']
gate = dict(json.loads(policy)['analyses']['Diffraction Encircled Energy']['quantities']['EnergyFraction'],
            minimumCoverage=.95)
assert gate['nrmse'] == .003


def metric(candidate, reference):
    error = np.abs(candidate-reference)
    assert len(error) == len(radii) and np.all(np.isfinite(error))
    nrmse = float(np.sqrt(np.mean(error**2))/max(reference.max(), gate['absolute']))
    pointwise = bool(np.all(error <= gate['absolute']+gate['relative']*np.abs(reference)))
    conclusion = 'Pass' if pointwise or nrmse <= gate['nrmse'] else 'Difference'
    return {'count': len(error), 'coverage': 1, 'nrmse': nrmse,
            'maximumAbsolute': float(error.max()), 'pointwisePass': pointwise, 'conclusion': conclusion}


def pupil_rule(radial_order, angle_order):
    radial, weight = roots_legendre(radial_order)
    radial = (radial+1)/2
    angles = np.arange(angle_order)*2*np.pi/angle_order
    px = (radial[:, None]*np.cos(angles)).ravel()
    py = (radial[:, None]*np.sin(angles)).ravel()
    weights = np.repeat(weight*radial/angle_order, angle_order)
    assert abs(weights.sum()-1) < 1e-13
    return px, py, weights


def gradient_centroid(radial_order, angle_order, step, sphere):
    # The Fourier first moment is the pupil power weighted phase gradient.
    # For the hard edge, take the symmetric spectral first-moment limit.
    px, py, weights = pupil_rule(radial_order, angle_order)
    means = []
    for dx, dy in ((step, 0), (0, step)):
        positive = phase(trace(px+dx, py+dy), sphere)
        negative = phase(trace(px-dx, py-dy), sphere)
        means.append(float(np.dot(weights, (positive-negative)/(2*step))))
    return -(wave/na)*np.array(means)


def energy(radial_coordinates, angular_intensity):
    primitive = CubicSpline(radial_coordinates, 2*np.pi*radial_coordinates*angular_intensity/total_power).antiderivative()
    result = primitive(radii)-primitive(0)
    assert result[0] == 0 and np.all(np.diff(result) > 0) and result[-1] < 1
    return result


def curve(radial_order, angle_order, degree, radial_samples, azimuth_samples, center, sphere, ideal):
    px, py, weights = pupil_rule(radial_order, angle_order)
    knots = np.cos(np.pi*(np.arange(degree)+.5)/degree)
    xx, yy = center[0]+5*knots, center[1]+5*knots
    field = np.zeros((degree, degree), complex)
    kernel = -2j*np.pi/wave
    for index in range(0, len(px), 4096):
        part = slice(index, index+4096)
        amplitude = weights[part]
        if not ideal:
            amplitude = amplitude*np.exp(-2j*np.pi*phase(trace(px[part], py[part]), sphere))
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
    return energy(radial, average)


def ideal_bessel_curve():
    radial = np.linspace(0, 5, 2001)
    angles = np.arange(160)*2*np.pi/160
    argument = 2*np.pi/wave*radial[:, None]*np.hypot(na[0]*np.cos(angles), na[1]*np.sin(angles))
    amplitude = np.divide(2*j1(argument), argument, out=np.ones_like(argument), where=argument != 0)
    return energy(radial, np.mean(amplitude**2, axis=1))


def period_moment(size, sphere):
    # Evaluate a complete physical Fourier period by Parseval reduction
    # over the other dimension. No image crop or centroid threshold.
    step = 2/(size-2)
    axis = np.arange(-size//2+1, size//2)*step
    logical_size = 2*size
    moments, errors, nyquist = [], [], []
    counts = []
    for component in (0, 1):
        spectrum = np.zeros(logical_size)
        count = 0
        for index in range(0, len(axis), 48):
            yy, xx = np.meshgrid(axis[index:index+48], axis, indexing='ij')
            if component == 1:
                xx, yy = yy, xx
            mask = xx**2+yy**2 <= 1
            pupil = np.zeros(mask.shape, complex)
            pupil[mask] = np.exp(-2j*np.pi*phase(trace(xx[mask], yy[mask]), sphere))
            spectrum += np.sum(abs(np.fft.fft(pupil, n=logical_size, axis=1))**2, axis=0)
            count += int(mask.sum())
        counts.append(count)
        frequency = np.fft.fftfreq(logical_size)
        frequency[logical_size//2] = 0
        scale = wave/(na[component]*step)
        moments.append(float(spectrum@frequency/spectrum.sum()*scale))
        nyquist.append(float(spectrum[logical_size//2]/spectrum.sum()*scale/2))
        errors.append(float(abs(spectrum.sum()/(logical_size*count)-1)))
    assert counts[0] == counts[1] and max(errors) < 1e-12
    return {'physicalPupilSampling': size, 'logicalFourierSize': logical_size, 'transmittedNodes': counts[0],
            'centroidMicrometers': moments, 'marginalParsevalRelativeErrors': errors,
            'nyquistEndpointHalfDifferenceMicrometers': nyquist}


gradient_orders = [(64, 128, 1e-4), (96, 192, 5e-5), (128, 256, 2.5e-5)]
native_centers = [gradient_centroid(*order, native_radius).tolist() for order in gradient_orders]
nominal_centers = [gradient_centroid(*order, nominal_radius).tolist() for order in gradient_orders]
actual_orders = [(768, 1536, 24, 801, 80), (1024, 2048, 32, 1601, 120)]
ideal_orders = [(64, 128, 24, 801, 80), (96, 192, 32, 1601, 120)]
actual_coarse, actual = [curve(*order, native_centers[-1], native_radius, False) for order in actual_orders]
ideal_coarse, ideal = [curve(*order, [0, 0], native_radius, True) for order in ideal_orders]
nominal_order = (768, 1536, 32, 1601, 120)
alternate = curve(*nominal_order, nominal_centers[-1], nominal_radius, False)
finite_centers = [period_moment(size, nominal_radius) for size in (3072, 4096, 6144)]
for witness in finite_centers:
    shifted = curve(*nominal_order, witness['centroidMicrometers'], nominal_radius, False)
    witness['energySensitivityVersusNativeGradientCenter'] = metric(shifted, actual)

report = {
    'index': 1210, 'lens': model['lens'], 'sourceSha256': sha(model['source']), 'frozenVerdict': 'Incomparable',
    'frozenArchiveSha256': model['frozen_sha'], 'frozenRequestMember': request_member,
    'frozenRequestSha256': sha(request_bytes), 'frozenNativeSettingsMember': settings_member,
    'frozenNativeSettingsSha256': sha(settings_bytes), 'frozenNativeDataMember': data_member,
    'frozenNativeDataSha256': sha(data_bytes), 'frozenDiagnostics': messages,
    'frozenComparisonPolicySha256': sha(policy), 'originalGate': gate,
    'nativeWavefrontMember': model['native_wavefront'], 'nativeWavefrontSha256': sha(model['phase_bytes']),
    'nativeWavefrontNodes': int(model['mask'].sum()), 'nativePhaseMaximumResidualWaves': model['reference_phase_error'],
    'wavelengthMicrometers': wave, 'directionalWorkingFNumbersXY': (.5/na).tolist(),
    'unitPeakIrradianceParsevalTotalPowerMicrometersSquared': total_power,
    'actualCentroid': {'phaseGradientOrders': gradient_orders, 'nativeSphereRadiusMm': native_radius,
                       'nativeSphereCentersMicrometers': native_centers, 'nominalSphereRadiusMm': nominal_radius,
                       'nominalSphereCentersMicrometers': nominal_centers, 'nominalSphereFinitePeriods': finite_centers},
    'radiusMicrometers': radii.tolist(),
    'components': [
        {'name': 'DiffractionLimit', 'centroidMicrometers': [0, 0], 'quadratureOrders': ideal_orders,
         'quadrature': metric(ideal_coarse, ideal), 'analyticEllipticalBesselCheck': metric(ideal_bessel_curve(), ideal),
         'referenceEnergyFraction': ideal.tolist()},
        {'name': 'Actual', 'centroidMicrometers': native_centers[-1], 'quadratureOrders': actual_orders,
         'quadrature': metric(actual_coarse, actual), 'nominalSphereControl': metric(alternate, actual),
         'referenceEnergyFraction': actual.tolist()}
    ],
    'independentReferenceComplete': True
}
args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
