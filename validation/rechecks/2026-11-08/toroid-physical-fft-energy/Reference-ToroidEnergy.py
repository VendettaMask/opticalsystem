import argparse
import json
from pathlib import Path
import runpy
from zipfile import ZipFile

import numpy as np
from numpy.polynomial.chebyshev import chebval2d
from scipy.fft import dctn
from scipy.special import eval_legendre, j0, j1, roots_legendre, spherical_jn


def frozen_energy(model, digest):
    with ZipFile(model['archive']) as archive:
        prefix = next(path.rsplit('/native/', 1)[0] for path in archive.namelist()
                      if '/1372-' in path and path.endswith('/native/data.json'))
        files = {}
        for part in ('native/data.json', 'native/captured-settings.json', 'workbench/request.json', 'workbench/data.json'):
            content = archive.read(f'{prefix}/{part}')
            files[part] = json.loads(content), {'member': f'{prefix}/{part}', 'sha256': digest(content)}
    native, native_identity = files['native/data.json']
    assert native['messages'] and not native['dataSeries'] and not native['dataGrids']
    settings, settings_identity = files['native/captured-settings.json']
    props = settings['properties']
    assert props['RadiusMaximum'] == 5 and props['Type'] == 'Encircled' and props['ReferTo'] == 'Centroid'
    assert props['ShowDiffractionLimit'] and not props['UseHuygensPSF'] and not props['UsePolarization']
    assert settings['selectors'] == {'Field': 1, 'Surface': 0, 'Wavelength': 1}
    request, request_identity = files['workbench/request.json']
    assert request['request']['workbenchSettings']['NumPoints'] == '401'
    assert request['request']['configuration'] == 1
    workbench, workbench_identity = files['workbench/data.json']
    ideal, actual = workbench['seriesList']
    radius = np.array([point['x'] for point in actual['points']])
    assert np.array_equal(radius, [point['x'] for point in ideal['points']])
    assert radius.size == 401 and radius[0] == 0 and radius[-1] == 5 and np.all(np.diff(radius) > 0)
    return radius, {'native': {**native_identity, 'messages': native['messages'], 'seriesCount': 0},
                    'nativeSettings': {**settings_identity, 'properties': props}, 'workbenchRequest': request_identity,
                    'originalRadiusSource': {**workbench_identity, 'usage': 'Abscissae only'}}


def field_nodes(phase, chirp, image_coordinate, diffraction_scale, plan, use_carrier):
    panels, order, weak_order = plan
    nodes, weights = roots_legendre(order)
    half = np.pi/(4*panels)
    centers = (2*np.arange(panels)+1)*half
    theta = (centers[:, None]+half*nodes).ravel()
    x, jacobian = np.sin(theta), np.cos(theta)
    if use_carrier:
        center_phase = chirp*np.sin(centers)**2
        slope = half*chirp*np.sin(2*centers)
        linear_phase = (center_phase[:, None]+slope[:, None]*nodes).ravel()
        argument = 2*np.pi*slope
        degree = np.arange(order)
        moments = spherical_jn(degree[:, None], abs(argument)[None, :]).T
        moments = moments*(1j*np.sign(argument[:, None]))**degree*(2*degree+1)
        outer_weights = (half*np.exp(2j*np.pi*center_phase[:, None])
                         * (moments @ eval_legendre(degree[:, None], nodes))*weights).ravel()
    else:
        linear_phase = np.zeros_like(x)
        outer_weights = np.tile(half*weights, panels)
    v, w = roots_legendre(weak_order)
    v, w = (v+1)/2, w/2
    image_wavevector = 2*np.pi*image_coordinate/diffraction_scale
    marginal = np.empty((len(x), len(image_coordinate)), dtype=complex)
    for start in range(0, len(x), 64):
        rows = slice(start, start+64)
        y = jacobian[rows, None]*v
        pupil = np.exp(2j*np.pi*(phase(x[rows, None], y)-linear_phase[rows, None]))*w
        marginal[rows] = np.einsum('ij,ijk->ik', pupil, np.cos(y[:, :, None]*image_wavevector))
    marginal *= (4/np.pi*jacobian**2*outer_weights)[:, None]
    return np.cos(x[:, None]*image_wavevector).T @ marginal


def encircle(radius, diffraction_scale, phase, chirp, pupil_plan, use_carrier, image_order, disk_orders):
    maximum = radius[-1]
    # The even complex field is smooth in each squared image coordinate.
    # Chebyshev interpolation on that domain gives the field at polar nodes.
    nodes = np.cos(np.pi*(np.arange(image_order)+.5)/image_order)
    coordinate = maximum*np.sqrt((nodes+1)/2)
    field = field_nodes(phase, chirp, coordinate, diffraction_scale, pupil_plan, use_carrier)
    coefficients = dctn(field, type=2)/(image_order*image_order)
    coefficients[0, :] /= 2
    coefficients[:, 0] /= 2
    radial_nodes, radial_weights = roots_legendre(disk_orders[0])
    angle_nodes, angle_weights = roots_legendre(disk_orders[1])
    radial_nodes, radial_weights = (radial_nodes+1)/2, radial_weights/2
    angle_nodes, angle_weights = np.pi*(angle_nodes+1)/4, np.pi*angle_weights/4
    weights = 4*radial_nodes[:, None]*radial_weights[:, None]*angle_weights
    total = diffraction_scale**2/np.pi
    actual = np.empty_like(radius)
    for start in range(0, len(radius), 24):
        rows = slice(start, start+24)
        normalized_r = (radius[rows, None, None]/maximum)*radial_nodes[None, :, None]
        x = normalized_r*np.cos(angle_nodes)
        y = normalized_r*np.sin(angle_nodes)
        irradiance = abs(chebval2d(2*x*x-1, 2*y*y-1, coefficients))**2
        actual[rows] = radius[rows]**2*np.sum(weights*irradiance, axis=(1, 2))/total
    q = 2*np.pi*radius[:, None]*radial_nodes/diffraction_scale
    airy = np.divide(2*j1(q), q, out=np.ones_like(q), where=q != 0)
    ideal = 2*np.pi*radius**2*np.sum((radial_nodes*radial_weights)*airy**2, axis=1)/total
    center = abs(chebval2d(-1., -1., coefficients))**2
    return actual, ideal, float(center)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('repo', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    mtf = runpy.run_path(str(Path(__file__).parent.parent / 'toroid-physical-fft-mtf/Reference-ToroidMtf.py'))
    model = mtf['load_model'](args.repo, args.output)
    digest = mtf['sha256']
    radius, archive = frozen_energy(model, digest)
    rotation = model['local'][:2, :2]
    phase = mtf['local_phase'](model, rotation)
    chirp = float(phase(np.array([1.]), np.array([0.]))[0])
    _, symmetries = mtf['fit_residual'](phase, chirp)
    na = np.linalg.norm(np.cross(model['dirs'], model['chief'][0][0]), axis=1)
    f_numbers = .5/na.reshape(2, 2).mean(axis=1)
    assert abs(f_numbers[0]-f_numbers[1]) < 1e-12
    diffraction_scale = model['wavelength']*f_numbers[0]
    # The entrance pupil spans [-1, 1]. A unit index step in the finite
    # transform corresponds to 2/(count-2) of that physical pupil.
    scale = 2*diffraction_scale
    plans = [((144, 12, 160), True, 16, (18, 20)), ((244, 18, 256), True, 24, (24, 28)),
             ((1024, 26, 288), False, 30, (30, 36))]
    results = []
    for plan in plans:
        results.append(encircle(radius, scale, phase, chirp, *plan))
        print('Completed', plan, flush=True)
    coarse, fine, direct = results
    q = 2*np.pi*radius/scale
    exact_ideal = 1-j0(q)**2-j1(q)**2
    assert all(np.all(np.diff(stage[0]) >= 0) for stage in results)
    assert max(np.max(abs(stage[1]-exact_ideal)) for stage in results) < 1e-12
    compare = mtf['compare']
    convergence = compare(coarse[0], fine[0])
    direct_control = compare(direct[0], fine[0])
    assert max(convergence['nrmse'], direct_control['nrmse']) < 1e-7
    output = {'requestId': 1372, 'status': 'ConditionalNearFocusFftOperatorOnly',
              'requestFingerprint': '9f5a3dc475998b3e11dcea25c545015004a14282d0b8b0b66d6b5119109319d1',
              'sourceSha256': digest(model['source']), 'frozenSources': archive,
              'nativePhase': {'member': model['wave_member'], 'sha256': digest(model['ref']),
                              'referenceSphereMm': model['radius'], 'nodes': int(model['mask'].sum()),
                              'maxErrorWaves': float(np.max(abs(model['native_error']))),
                              'principalSignSymmetryMaxWaves': symmetries['principalSignSymmetryMaxWaves']},
              'imageAxes': {'fNumbersXY': f_numbers.tolist(), 'wavelengthMicrometers': model['wavelength'],
                            'principalToImageRotation': rotation.tolist(), 'diffractionScaleMicrometers': diffraction_scale,
                            'fourierDenominatorMicrometers': scale,
                            'centroidMicrometersXY': [0., 0.], 'centroidConvention': 'Symmetric full-image limit'},
              'power': {'normalizedPupilPower': np.pi, 'normalizedFieldImagePowerSquareMicrometers': scale**2/np.pi,
                        'idealNormalizedCentralIrradianceByTier': [r[2] for r in results]},
              'plans': plans, 'fullActualCurveConvergence': convergence, 'fullDirectControl': direct_control,
              'idealAiryMaximumError': float(max(np.max(abs(r[1]-exact_ideal)) for r in results)),
              'series': {'radiusMicrometers': radius.tolist(), 'actual': fine[0].tolist(),
                         'diffractionLimit': exact_ideal.tolist(), 'coarseActual': coarse[0].tolist(),
                         'directActual': direct[0].tolist()}}
    path = args.output / 'results.json'
    path.write_text(json.dumps(output, indent=2, ensure_ascii=False, allow_nan=False)+'\n', encoding='utf-8')
    print(path, 'actual/ideal', fine[0][-1], exact_ideal[-1], convergence, direct_control, flush=True)


if __name__ == '__main__':
    main()
