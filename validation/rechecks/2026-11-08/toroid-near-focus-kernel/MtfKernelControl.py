import argparse
import json
from pathlib import Path
import runpy

import numpy as np
from scipy.interpolate import PchipInterpolator
from scipy.optimize import brentq
from scipy.special import roots_legendre


def map_jacobian(project, p, step):
    dx = (project(p+[step, 0])[0]-project(p-[step, 0])[0])/(2*step)
    dy = (project(p+[0, step])[0]-project(p-[0, step])[0])/(2*step)
    det = dx[:, 0]*dy[:, 1]-dx[:, 1]*dy[:, 0]
    assert np.min(det) > .9
    return dx, dy, det


def inverse(project, q, step):
    p = q.copy()
    for _ in range(5):
        current, phase = project(p)
        error = current-q
        dx, dy, det = map_jacobian(project, p, step)
        residual = float(np.max(abs(error)))
        if residual < 5e-13:
            assert np.max(np.sum(p*p, axis=1)) <= 1+5e-8
            return phase, 1/det, residual
        correction = np.column_stack((dy[:, 1]*error[:, 0]-dy[:, 0]*error[:, 1],
                                      dx[:, 0]*error[:, 1]-dx[:, 1]*error[:, 0]))
        p -= correction/det[:, None]
    raise ValueError('projected-pupil inverse did not converge')


def dc_power(project, count, step):
    nodes, weights = roots_legendre(count)
    theta = np.pi/4*(nodes+1)
    x, y = np.broadcast_arrays(np.sin(theta)[:, None], np.cos(theta)[:, None]*(nodes+1)/2)
    p = np.column_stack((x.ravel(), y.ravel()))
    _, _, det = map_jacobian(project, p, step)
    # Full p-space integral of 1/J equals integral of 1/J² on the
    # normalized direction domain used in the pupil overlap calculation.
    area = (np.pi/2*(weights*np.cos(theta)**2)[:, None]*weights).ravel()
    return float(area @ (1/det))


def overlap(project, boundary, h, plan, dc, step):
    hx, hy = h
    end = 1-hx/2
    switch = brentq(lambda x: boundary(x-hx/2)-boundary(x+hx/2)-hy, 0, end)
    angles = np.arcsin([hx/2, switch+hx/2, 1.])
    panels, order, weak = plan
    nodes, weights = roots_legendre(order)
    ynodes, yweights = roots_legendre(weak)
    total, residual = 0., 0.
    for lo, hi in zip(angles[:-1], angles[1:]):
        half = (hi-lo)/(2*panels)
        centers = lo+(2*np.arange(panels)+1)*half
        theta = (centers[:, None]+half*nodes).ravel()
        x = np.sin(theta)-hx/2
        b, c = boundary(x+hx/2), boundary(x-hx/2)
        bottom = np.maximum(-hy/2-b, hy/2-c)
        top = -hy/2+b
        spread = (top-bottom)/2
        y = (top+bottom)[:, None]/2+spread[:, None]*ynodes
        xx = np.broadcast_to(x[:, None], y.shape)
        q = np.column_stack((xx.ravel(), y.ravel()))
        a, wa, ra = inverse(project, q+h/2, step)
        b, wb, rb = inverse(project, q-h/2, step)
        integrand = (wa*wb*np.cos(2*np.pi*(a-b))).reshape(y.shape)
        total += np.dot((integrand@yweights)*(np.cos(theta)*spread), np.tile(half*weights, panels))
        residual = max(residual, ra, rb)
    return float(2*total/dc), residual


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
    direction = kernel['reference_sphere']
    scale = direction(model, np.array([1., 0.]), np.array([0., 1.]))[0].diagonal()
    def project(p):
        projected, phase = direction(model, p[:, 0], p[:, 1])
        return projected/scale, phase
    original = (rechecks/'toroid-physical-fft-mtf/results.json').read_bytes()
    series = json.loads(original)['series']
    indices = [0, 1, 2, 3, 4, 6, 10, 16]
    frequency = np.asarray(series['frequencyCyclesPerMm'])[indices]
    fft = np.asarray(series['modulation'])[0, indices]
    shift = (model['wavelength']*1e-3/np.sqrt(2))*frequency[:, None]/scale
    plans = [((12, 12, 28), 1025, 1e-4), ((24, 16, 40), 2049, 5e-5)]
    tiers, residuals, powers = [], [], []
    for plan, samples, step in plans:
        theta = np.linspace(0, np.pi/2, samples)
        points = np.column_stack((np.cos(theta), np.sin(theta)))
        rim, _ = project(points)
        rim[0], rim[-1] = [1., 0.], [0., 1.]
        x, y = rim[::-1].T
        ratio = y[:-1]/np.sqrt(1-x[:-1]**2)
        fit = PchipInterpolator(x[:-1]**2, ratio, extrapolate=True)
        def boundary(x):
            square = np.minimum(x*x, 1.)
            return np.sqrt(1-square)*fit(square)
        dc = dc_power(project, 32, step)
        powers.append(dc)
        data = [overlap(project, boundary, h, plan, dc, step) for h in shift[1:]]
        tiers.append(np.r_[1., [item[0] for item in data]])
        residuals.append(max(item[1] for item in data))
        print('Completed', plan, tiers[-1].tolist(), flush=True)
    first, high = tiers
    assert np.max(abs(first-high)) < 1e-6
    diff = abs(abs(high)-fft)
    pointwise = diff <= .001+.001*abs(high)
    assert not np.all(pointwise)
    result = {'requestId': 1367, 'status': 'SharedPupilSphereTangentKernelWitness',
              'fftEvidenceSha256': shared['sha256'](original), 'originalFrequencyIndices': indices,
              'frequenciesCyclesPerMm': frequency.tolist(), 'axisLabels': series['axisLabels'],
              'axisSymmetry': 'Both signed image axes project to the same two principal shift magnitudes',
              'spherePrincipalEdgeProjection': scale.tolist(), 'normalizedDirectionPupilDcByTier': powers,
              'planDimensions': ['panelsPerBoundaryPieceStrongOrderWeakOrder', 'boundarySamples', 'jacobianStep'],
              'plans': plans, 'maximumInverseResidualByTier': residuals,
              'maximumSignedOtfConvergenceDifference': float(np.max(abs(first-high))),
              'originalPointwiseGate': {'absolute': .001, 'relative': .001},
              'fftModulation': fft.tolist(), 'sphereSignedOtf': high.tolist(),
              'modulationDifference': diff.tolist(), 'pointwisePassAtWitnessFrequencies': pointwise.tolist(),
              'coverage': len(frequency)/300, 'fullRequestComparison': 'Unestablished'}
    (args.output/'mtf-kernel-control.json').write_text(json.dumps(result, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
    print('Max convergence', result['maximumSignedOtfConvergenceDifference'], 'FFT', fft, 'difference', diff)


if __name__ == '__main__':
    main()
