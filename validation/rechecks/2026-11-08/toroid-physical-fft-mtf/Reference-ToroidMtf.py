import argparse
import hashlib
import json
from pathlib import Path
import runpy
import sys
from zipfile import ZipFile

import numpy as np
from numpy.polynomial.chebyshev import chebval, chebval2d, chebvander2d
from scipy.special import eval_legendre, roots_legendre, spherical_jn


def sha256(payload):
    return hashlib.sha256(payload).hexdigest()


def load_model(repo, output):
    triage = repo / 'validation/rechecks/2026-11-08/toroid-fft-phase-domain/ToroidTriage.py'
    argv = sys.argv
    try:
        sys.argv = [str(triage), str(repo), str(output / 'triage-replay.json')]
        return runpy.run_path(str(triage))
    finally:
        sys.argv = argv


def archive_mtf(model):
    with ZipFile(model['archive']) as archive:
        member = next(path for path in archive.namelist()
                      if '/1367-' in path and path.endswith('/native/data.json'))
        payload = archive.read(member)
        captured = archive.read(member.replace('data.json', 'captured-settings.json'))
    native = json.loads(payload)
    settings = json.loads(captured)
    properties = settings['properties']
    assert properties['Type'] == 'Modulation' and properties['MaximumFrequency'] == 50
    assert not properties['ShowDiffractionLimit'] and not properties['UsePolarization']
    assert settings['selectors'] == {'Field': 1, 'Surface': 0, 'Wavelength': 1}
    assert len(native['dataSeries']) == 1 and any('取样太低' in m['Text'] for m in native['messages'])
    series = native['dataSeries'][0]
    frequency = np.asarray(series['x'])
    assert series['seriesLabels'] == ['子午', '弧矢'] and np.shape(series['y']) == (300, 2)
    assert frequency[0] == 0 and frequency[-1] == 50 and np.all(np.diff(frequency) > 0)
    metadata = {'member': member, 'sha256': sha256(payload), 'messages': native['messages'],
                'capturedSettingsSha256': sha256(captured), 'readback': properties,
                'axisLabel': series['xLabel'], 'valueUsage': 'Abscissae and series names only'}
    return frequency, series['seriesLabels'], metadata


def local_phase(model, rotation):
    def phase(x, y):
        x, y = np.broadcast_arrays(x, y)
        local = np.column_stack((x.ravel(), y.ravel()))
        global_pupil = local @ rotation.T
        return model['phase'](*global_pupil.T, model['radius']).reshape(x.shape)
    return phase


def fit_residual(phase, chirp):
    nodes = (roots_legendre(26)[0]+1)/2
    x2 = np.broadcast_to(nodes[:, None], (26, 26))
    y2 = (1-nodes[:, None])*nodes[None, :]
    target = (phase(np.sqrt(x2), np.sqrt(y2)) - chirp*x2).ravel()
    rng = np.random.default_rng(1367)
    angle = rng.uniform(0, 2*np.pi, 5000)
    radius = np.sqrt(rng.uniform(0, 1, 5000))
    x, y = radius*np.cos(angle), radius*np.sin(angle)
    raw = phase(x, y)
    symmetry = max(float(np.max(np.abs(raw-phase(i*x, j*y))))
                   for i, j in [(-1, 1), (1, -1), (-1, -1)])
    coeffs = {}
    controls = {'holdoutCount': len(x), 'principalSignSymmetryMaxWaves': symmetry, 'fits': {}}
    for degree in (8, 10):
        mask = np.indices((degree+1, degree+1)).sum(axis=0) <= degree
        vandermonde = chebvander2d(2*x2.ravel()-1, 2*y2.ravel()-1, [degree, degree])
        coeff = np.zeros_like(mask, dtype=float)
        coeff[mask] = np.linalg.lstsq(vandermonde[:, mask.ravel()], target, rcond=1e-13)[0]
        error = raw - chirp*x*x - chebval2d(2*x*x-1, 2*y*y-1, coeff)
        controls['fits'][degree] = {'maximumErrorWaves': float(np.max(abs(error))),
                                    'rmsErrorWaves': float(np.sqrt(np.mean(error**2)))}
        coeffs[degree] = coeff
    assert symmetry < 1e-8 and controls['fits'][10]['maximumErrorWaves'] < 1e-8
    return coeffs, controls


def residual_phase(x, y, coefficients):
    # x is constant along each weak-coordinate row.
    rows = chebval(2*x*x-1, coefficients)
    return chebval(2*y*y-1, rows[:, :, None], tensor=False)


def overlap_integral(shift, plan, chirp, coefficients=None, phase=None):
    panels, order, weak_order = plan
    hx, hy = abs(shift)
    distance = np.hypot(hx, hy)
    assert distance < 2
    switch = hy/distance*np.sqrt(1-distance**2/4) if distance else 1/np.sqrt(2)
    first = np.arcsin(hx/2)
    middle = np.arcsin(switch+hx/2)
    nodes, weights = roots_legendre(order)
    weak_nodes, weak_weights = roots_legendre(weak_order)
    power, total = 0., 0j
    carrier = 2*chirp*hx
    for start, end in ((first, middle), (middle, np.pi/2)):
        half = (end-start)/(2*panels)
        center = start+(2*np.arange(panels)+1)*half
        theta = (center[:, None]+half*nodes).ravel()
        x = np.sin(theta)-hx/2
        jacobian = np.cos(theta)
        # On x >= 0 the circle centered at (-hx/2,-hy/2) gives the upper
        # boundary. The lower boundary changes circles at theta = middle.
        other = np.sqrt(1-(x-hx/2)**2)
        lower = np.maximum(-hy/2-jacobian, hy/2-other)
        upper = -hy/2+jacobian
        spread = (upper-lower)/2
        y = (upper+lower)[:, None]/2+spread[:, None]*weak_nodes
        assert np.all(spread >= 0)
        for sign in (-1, 1):
            assert np.max((x[:, None]+sign*hx/2)**2+(y+sign*hy/2)**2) <= 1+1e-13
        if coefficients is not None:
            # Separate the linearized strong carrier within each angular panel.
            # Its Legendre moments are known exactly via spherical Bessel functions.
            center_phase = carrier*(np.sin(center)-hx/2)
            slope = half*carrier*np.cos(center)
            argument = 2*np.pi*slope
            degrees = np.arange(order)
            moments = spherical_jn(degrees[:, None], abs(argument)[None, :]).T
            moments *= 2*degrees+1
            moments = moments*(1j*np.sign(argument[:, None]))**degrees
            legendre = eval_legendre(degrees[:, None], nodes)
            outer_weights = (half*np.exp(2j*np.pi*center_phase[:, None])*(moments@legendre)*weights).ravel()
            linear_phase = (center_phase[:, None]+slope[:, None]*nodes).ravel()
            difference = residual_phase(x+hx/2, y+hy/2, coefficients)
            difference -= residual_phase(x-hx/2, y-hy/2, coefficients)
            difference += (carrier*x-linear_phase)[:, None]
        else:
            # This control uses only direct ray-traced phases, ordinary Gauss
            # weights and no evaluation of the polynomial or carrier moments.
            outer_weights = np.tile(half*weights, panels)
            difference = np.empty_like(y)
            for start_row in range(0, len(x), 96):
                rows = slice(start_row, start_row+96)
                difference[rows] = (phase(x[rows, None]+hx/2, y[rows]+hy/2)
                                    - phase(x[rows, None]-hx/2, y[rows]-hy/2))
        area_weights = jacobian*spread
        total += np.dot(area_weights*(np.exp(2j*np.pi*difference)@weak_weights), outer_weights)
        power += 2*np.dot(area_weights, np.tile(half*weights, panels))
    # Inversion sends the phase difference to its negative. The other half
    # of the overlap is therefore the conjugate, including its phase sign.
    return 2*total.real/np.pi, 2*power


def compare(candidate, reference):
    difference = candidate-reference
    nrmse = np.linalg.norm(difference)/np.linalg.norm(reference)
    return {'maxAbs': float(np.max(np.abs(difference))), 'nrmse': float(nrmse)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('repo', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    model = load_model(args.repo, args.output)
    frequency, labels, native = archive_mtf(model)
    rotation = model['local'][:2, :2]
    image_directions = np.array([[0., 1.], [1., 0.]])
    principal_directions = image_directions @ rotation
    phase = local_phase(model, rotation)
    chirp = float(phase(np.array([1.]), np.array([0.]))[0])
    coeffs, phase_control = fit_residual(phase, chirp)

    # Directions are [X+, X-, Y+, Y-]. The native image plane has index 1
    # and its coordinate frame is parallel to the global frame here.
    edge = model['dirs']-model['chief'][0][0]
    fourier_f = 1/np.array([abs(edge[2:, 1]).sum(), abs(edge[:2, 0]).sum()])
    wavelength_mm = model['wavelength']*1e-3
    shifts = frequency[None, :, None]*(2*wavelength_mm*fourier_f[:, None]*principal_directions)[:, None, :]
    assert np.allclose(abs(shifts[0]), abs(shifts[1]), rtol=0, atol=1e-14)
    distances = np.linalg.norm(shifts, axis=-1)
    ideal_area = 2*np.arccos(distances/2) - distances*np.sqrt(1-distances**2/4)

    segmented_plans = [(84, 22, 128), (112, 26, 160)]
    tiers = []
    for plan in segmented_plans:
        axes = []
        for label, direction_shifts in zip(labels, shifts):
            axes.append([overlap_integral(shift, plan, chirp, coefficients=coeffs[10])
                         for shift in direction_shifts])
            print('Completed', plan, label, flush=True)
        tiers.append(np.asarray(axes))
    coarse, fine = tiers
    signed_otf, area = fine[:, :, 0], fine[:, :, 1]
    mtf = abs(signed_otf)
    assert np.max(abs(area-ideal_area)) < 1e-12 and np.max(abs(area[:, 0]-np.pi)) < 1e-12
    assert np.max(abs(mtf[:, 0]-1)) < 1e-12

    indices = np.array([0, 1, 2, 10, 23, 31, 63, 97, 129, 149, 178, 211, 251, 282, 299])
    direct_plan = (224, 30, 160)
    direct = np.asarray([overlap_integral(shifts[direction, index], direct_plan, chirp, phase=phase)[0]
                         for direction, index in zip(np.arange(len(indices)) % 2, indices)])
    alternate = np.asarray([overlap_integral(shifts[direction, index], segmented_plans[-1], chirp,
                                            coefficients=coeffs[8])[0]
                            for direction, index in zip(np.arange(len(indices)) % 2, indices)])
    fine_at_controls = signed_otf[np.arange(len(indices)) % 2, indices]
    convergence = compare(coarse[:, :, 0], signed_otf)
    off_dc = compare(coarse[:, 1:, 0], signed_otf[:, 1:])
    direct_agreement = compare(direct, fine_at_controls)
    fit_agreement = compare(alternate, fine_at_controls)
    directions_agree = compare(signed_otf[0], signed_otf[1])
    assert max(convergence['maxAbs'], direct_agreement['maxAbs'], fit_agreement['maxAbs']) < 1e-8
    assert off_dc['nrmse'] < 1e-5 and directions_agree['maxAbs'] < 1e-10

    principal_edges = np.eye(2) @ rotation.T
    _, edge_hits, _ = model['trace'](*principal_edges.T)
    edge_miss = np.linalg.norm(edge_hits[:, :2]-model['chief'][1][0, :2], axis=1)
    frozen = model['request_members'][1367]
    result = {
        'requestId': 1367,
        'status': 'ConditionalNearFocusFftOperatorOnly',
        'sourceSha256': sha256(model['source']),
        'requestFingerprint': '15549f8f64241044016f717cfb0a943e99f2433f3be2ac9fc095dda6bdf4286b',
        'frozenWorkbenchRequest': frozen,
        'frozenNativeMtf': native,
        'nativePhase': {'member': model['wave_member'], 'sha256': sha256(model['ref']),
                        'illuminatedNodes': int(model['mask'].sum()),
                        'maxRayTraceErrorWaves': float(np.max(abs(model['native_error']))),
                        'referenceSphereMm': model['radius'], 'principalCarrierWaves': chirp,
                        'phaseFitControls': phase_control},
        'coordinateConvention': {'axisLabels': labels, 'imageDirectionsXY': image_directions.tolist(),
                                 'principalDirectionsUV': principal_directions.tolist(),
                                 'principalToImageRotation': rotation.tolist(),
                                 'wavelengthMicrometers': model['wavelength'], 'frequencyUnit': 'cycles/mm',
                                 'workingConeFNumber': model['F'], 'imagePlaneFourierFNumbers': fourier_f.tolist(),
                                 'projectedFourierCutoffCyclesPerMm': (1/(wavelength_mm*fourier_f)).tolist()},
        'pupilPower': {'normalizedPupilRadius': 1., 'entrancePupilRadiusMm': model['pupil'],
                       'powerAtZeroLag': area[:, 0].tolist(), 'exactZeroLagPower': np.pi,
                       'uniformPupilEntranceAreaMm2': np.pi*model['pupil']**2,
                       'idealCircularOverlapMaxAreaError': float(np.max(abs(area-ideal_area)))},
        'quadrature': {'planDimensions': ['panelsPerBoundaryPiece', 'strongOrder', 'weakOrder'],
                       'segmentedPlans': segmented_plans, 'directRayPlan': direct_plan,
                       'fullSignedOtfConvergence': convergence, 'nonzeroFrequencyConvergence': off_dc,
                       'directRayAgreement': direct_agreement, 'degree8PhaseAgreement': fit_agreement,
                       'axisSymmetryAgreement': directions_agree,
                       'directControls': [{'frequencyIndex': int(index), 'axis': labels[j % 2],
                                           'frequencyCyclesPerMm': float(frequency[index]),
                                           'directSignedOtf': float(direct[j]),
                                           'alternatePhaseSignedOtf': float(alternate[j])}
                                          for j, index in enumerate(indices)]},
        'nearFocus': {'principalEdgeMissMm': edge_miss.tolist(),
                      'principalEdgeMissWavelengths': (edge_miss/wavelength_mm).tolist(),
                      'productLocalPhaseWitnessWavesAt6144': model['mtf']['adjacentFilteredPhaseWaves']},
        'series': {'frequencyCyclesPerMm': frequency.tolist(), 'axisLabels': labels,
                   'modulation': mtf.tolist(), 'signedOtf': signed_otf.tolist(),
                   'coarserSignedOtf': coarse[:, :, 0].tolist(),
                   'normalizedOverlapArea': (area/np.pi).tolist()}}
    path = args.output / 'results.json'
    path.write_text(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    print(path, convergence, off_dc, direct_agreement, fit_agreement, directions_agree, flush=True)


if __name__ == '__main__':
    main()
