import argparse
import json
from pathlib import Path
import runpy

import numpy as np
from numpy.polynomial.chebyshev import chebval2d
from scipy.fft import dctn
from scipy.special import roots_legendre


def ideal_pupil(model, order, step, reference_sphere):
    nodes, weights = roots_legendre(order)
    theta = np.pi/4*(nodes+1)
    v = (nodes+1)/2
    x, y = np.broadcast_arrays(np.sin(theta)[:, None], np.cos(theta)[:, None]*v)
    area_weights = ((4/np.pi)*(np.pi/4)*(weights*np.cos(theta)**2)[:, None]*(weights/2)).ravel()
    def direction(u, v):
        return reference_sphere(model, u, v)[0]
    projection = direction(x, y)
    dx = (direction(x+step, y)-direction(x-step, y))/(2*step)
    dy = (direction(x, y+step)-direction(x, y-step))/(2*step)
    jacobian = dx[:, 0]*dy[:, 1]-dx[:, 1]*dy[:, 0]
    assert jacobian.min() > 0 and abs(area_weights.sum()-1) < 1e-13
    # With the same unit launch measure, the direction-space amplitude is
    # 1/(pi J). Full tangent-field Parseval power is lambda²/pi² int dp/J.
    total = model['wavelength']**2/np.pi * (area_weights @ (1/jacobian))
    return projection, area_weights.astype(complex), total, [float(jacobian.min()), float(jacobian.max())]


def disk_curve(grid, radius, total, orders):
    count = len(grid)
    coefficients = dctn(grid, type=2)/count**2
    coefficients[0, :] /= 2
    coefficients[:, 0] /= 2
    radial, rw = roots_legendre(orders[0])
    angle, aw = roots_legendre(orders[1])
    radial, rw = (radial+1)/2, rw/2
    angle, aw = np.pi*(angle+1)/4, np.pi*aw/4
    weights = 4*radial[:, None]*rw[:, None]*aw
    result = np.empty_like(radius)
    for i in range(0, len(radius), 24):
        rows = slice(i, i+24)
        r = (radius[rows, None, None]/radius[-1])*radial[None, :, None]
        x, y = r*np.cos(angle), r*np.sin(angle)
        energy_density = abs(chebval2d(2*x*x-1, 2*y*y-1, coefficients))**2
        result[rows] = radius[rows]**2*np.sum(energy_density*weights, axis=(1, 2))/total
    return result


def metrics(a, b):
    error = abs(a-b)
    nrmse = float(np.sqrt(np.mean(error**2))/max(np.max(abs(b)), 1e-5))
    pointwise = bool(np.all(error <= 1e-5+.002*abs(b)))
    return {'maxAbs': float(error.max()), 'originalScaleNrmse': nrmse, 'originalPointwiseGatePass': pointwise,
            'originalNumericalGatePass': pointwise or nrmse <= .003}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('repo', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rechecks = args.repo/'validation/rechecks/2026-11-08'
    shared = runpy.run_path(str(rechecks/'toroid-physical-fft-mtf/Reference-ToroidMtf.py'))
    model = shared['load_model'](args.repo, args.output)
    kernel = runpy.run_path(str(Path(__file__).with_name('Probe-ToroidKernel.py')))
    energy_source = (rechecks/'toroid-physical-fft-energy/results.json').read_bytes()
    energy = json.loads(energy_source)['series']
    radius = np.asarray(energy['radiusMicrometers'])
    fft_actual, fft_ideal = np.asarray(energy['actual']), np.asarray(energy['diffractionLimit'])
    _, edge = kernel['reference_sphere'](model, np.ones(1), np.zeros(1))
    chirp = edge[0]
    plans = [((172, 14, 176, True), 48, 1e-4, 18, (20, 24)), ((244, 18, 240, True), 72, 5e-5, 26, (26, 32))]
    results = []
    for pupil_plan, ideal_order, step, image_order, disk_orders in plans:
        nodes = np.cos(np.pi*(np.arange(image_order)+.5)/image_order)
        image = radius[-1]*np.sqrt((nodes+1)/2)
        x, y = np.meshgrid(image, image, indexing='ij')
        coords = np.column_stack((x.ravel(), y.ravel()))
        ideal_positions, ideal_weights, total, jacobian = ideal_pupil(model, ideal_order, step, kernel['reference_sphere'])
        ideal_exact = np.concatenate([kernel['sampled_fields'](ideal_positions, ideal_weights, row, model)[0]
                                      for row in np.array_split(coords, image_order)])
        ideal_exact = ideal_exact.reshape(image_order, image_order)
        ideal_tangent = kernel['tangent_grid'](ideal_positions, ideal_weights, image, model['wavelength'])
        actual_positions, actual_weights = kernel['source_nodes'](model, chirp, pupil_plan)
        actual_tangent = kernel['tangent_grid'](actual_positions, actual_weights, image, model['wavelength'])
        results.append((disk_curve(actual_tangent, radius, total, disk_orders),
                        disk_curve(ideal_tangent, radius, total, disk_orders),
                        disk_curve(ideal_exact, radius, total, disk_orders), total, jacobian))
    low, high = results
    convergence = {k: metrics(low[i], high[i]) for i, k in enumerate(('actualTangent', 'idealTangent', 'idealExact'))}
    assert max(x['maxAbs'] for x in convergence.values()) < 1e-8
    phase_effect = metrics(high[1], high[2])
    assert phase_effect['maxAbs'] < 1e-6
    comparison = {'fftVsSphereTangentActual': metrics(fft_actual, high[0]),
                  'fftVsSphereExactIdeal': metrics(fft_ideal, high[2]),
                  'sphereTangentVsExactIdeal': phase_effect}
    assert not comparison['fftVsSphereExactIdeal']['originalNumericalGatePass']
    result = {'requestId': 1372, 'status': 'SharedPupilSphereKernelSensitivity',
              'fftEvidenceSha256': shared['sha256'](energy_source), 'plans': plans,
              'tangentParsevalImagePowerSquareMicrometersByTier': [x[3] for x in results],
              'projectedPupilJacobianRangeByTier': [x[4] for x in results],
              'originalSettings': {'radiusMicrometers': [0, 5], 'points': len(radius), 'centroidMicrometersXY': [0, 0]},
              'originalEnergyFractionGates': {'absolute': 1e-5, 'relative': .002, 'nrmse': .003},
              'coverage': 1., 'convergence': convergence, 'comparisons': comparison,
              'sphereExactWindowNormalization': 'Common full tangent-field Parseval power',
              'series': {'radiusMicrometers': radius.tolist(), 'sphereActualTangent': high[0].tolist(),
                         'sphereIdealTangent': high[1].tolist(), 'sphereIdealExact': high[2].tolist()}}
    (args.output/'energy-kernel-control.json').write_text(json.dumps(result, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    print('Endpoints', *(curve[-1] for curve in high[:3]), 'Power', high[3], comparison, convergence, flush=True)


if __name__ == '__main__':
    main()
