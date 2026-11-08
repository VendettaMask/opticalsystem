import argparse
import json
from pathlib import Path
import runpy

import numpy as np
from scipy.special import eval_legendre, roots_legendre, spherical_jn


def reference_sphere(model, x, y):
    shape = np.broadcast_shapes(x.shape, y.shape)
    local = np.column_stack([np.broadcast_to(axis, shape).ravel() for axis in (x, y)])
    rotation = model['local'][:2, :2]
    direction, image, path = model['trace'](*(local@rotation.T).T)
    offset = image-model['chief'][1][0]
    radial = model['radius']
    along = np.sum(offset*direction, axis=1)
    cross_squared = np.sum(np.cross(offset, direction)**2, axis=1)
    root = np.sqrt(radial**2-cross_squared)
    t = along+root
    point = offset-t[:, None]*direction
    assert np.max(abs(np.linalg.norm(point, axis=1)-radial)) < 1e-10
    center_opd = model['chief_path']-path+along-cross_squared/(radial+root)
    return point[:, :2]@rotation/radial, center_opd/(model['wavelength']*1e-3)


def source_nodes(model, chirp, plan):
    panels, strong, weak, use_carrier = plan
    nodes, weights = roots_legendre(strong)
    half = np.pi/(4*panels)
    centers = (2*np.arange(panels)+1)*half
    theta = (centers[:, None]+half*nodes).ravel()
    sine, cosine = np.sin(theta), np.cos(theta)
    if use_carrier:
        center = chirp*np.sin(centers)**2
        slope = half*chirp*np.sin(2*centers)
        carrier = (center[:, None]+slope[:, None]*nodes).ravel()
        degrees = np.arange(strong)
        arg = 2*np.pi*slope
        moments = spherical_jn(degrees[:, None], abs(arg)[None, :]).T
        moments = moments*(1j*np.sign(arg[:, None]))**degrees*(2*degrees+1)
        angular = (half*np.exp(2j*np.pi*center[:, None])
                   *(moments @ eval_legendre(degrees[:, None], nodes))*weights).ravel()
    else:
        carrier = np.zeros_like(theta)
        angular = np.tile(half*weights, panels)
    weak_nodes, weak_weights = roots_legendre(weak)
    v, w = (weak_nodes+1)/2, weak_weights/2
    projection = np.empty((theta.size, weak, 2))
    amplitude = np.empty((theta.size, weak), dtype=complex)
    for i in range(0, len(theta), 64):
        rows = slice(i, i+64)
        direction, phase = reference_sphere(model, sine[rows, None], cosine[rows, None]*v)
        projection[rows] = direction.reshape(-1, weak, 2)
        phase = phase.reshape(-1, weak)-carrier[rows, None]
        amplitude[rows] = ((4/np.pi*angular[rows]*cosine[rows]**2)[:, None]
                           * w * np.exp(2j*np.pi*phase))
    return projection.reshape(-1, 2), amplitude.ravel()


def tangent_grid(projection, amplitude, coordinate, wavelength):
    k = 2*np.pi*coordinate/wavelength
    result = np.zeros((len(k), len(k)), dtype=complex)
    for i in range(0, len(amplitude), 8192):
        rows = slice(i, i+8192)
        cos_x = np.cos(projection[rows, 0, None]*k)
        cos_y = np.cos(projection[rows, 1, None]*k)
        result.real += cos_x.T @ (amplitude[rows, None].real*cos_y)
        result.imag += cos_x.T @ (amplitude[rows, None].imag*cos_y)
    return result


def sampled_fields(projection, amplitude, points, model):
    radius = model['radius']
    wave_mm = model['wavelength']*1e-3
    points = points*1e-3
    exact = np.zeros(len(points), dtype=complex)
    tangent = np.zeros(len(points), dtype=complex)
    for i in range(0, len(amplitude), 4096):
        rows = slice(i, i+4096)
        sx = projection[rows, 0, None]*points[:, 0]
        sy = projection[rows, 1, None]*points[:, 1]
        tangent += amplitude[rows] @ (np.cos(2*np.pi*sx/wave_mm)*np.cos(2*np.pi*sy/wave_mm))
        square = np.sum(points**2, axis=1)
        # The sphere is centered at the original image chief intersection.
        # Pair the four source reflections at every unmodified image point.
        kernel = np.zeros(sx.shape, dtype=complex)
        for i_sign, j_sign in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            z = square-2*radius*(i_sign*sx+j_sign*sy)
            difference = z/(np.sqrt(radius**2+z)+radius)
            kernel += np.exp(-2j*np.pi*difference/wave_mm)
        exact += amplitude[rows] @ (kernel/4)
    return exact, tangent


def metric(a, b, relative=.001, nrmse_limit=.01):
    absolute = 1e-5
    assert a.shape == b.shape
    a, b = abs(a)**2, abs(b)**2
    difference = abs(a-b)
    rms = float(np.sqrt(np.mean(difference**2)))
    peak = float(b.max())
    nrmse = rms/max(peak, absolute)
    pointwise = bool(np.all(difference <= absolute+relative*b))
    return {'maxAbs': float(difference.max()), 'referencePeak': peak,
            'peakNormalizedRms': rms/peak, 'originalScaleNrmse': nrmse,
            'originalPointwiseGatePass': pointwise, 'originalNumericalGatePass': pointwise or nrmse <= nrmse_limit}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('repo', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rechecks = args.repo/'validation/rechecks/2026-11-08'
    shared = runpy.run_path(str(rechecks/'toroid-physical-fft-mtf/Reference-ToroidMtf.py'))
    model = shared['load_model'](args.repo, args.output)
    rotation = model['local'][:2, :2]
    chirp = float(shared['local_phase'](model, rotation)(np.array([1.]), np.array([0.]))[0])
    original_path = rechecks/'toroid-fft-phase-domain/toroid-original-windows.npz'
    original = np.load(original_path)
    axis = original['image_axis_micrometers']
    profile_axis = original['profile_axis_micrometers']
    bins = np.arange(-63, 65)
    ix, iy = np.meshgrid(bins, bins)
    coordinates = np.arange(129)*.25/np.sqrt(2)
    indices = [(63, 63), (0, 0), (0, 127), (127, 0), (127, 127), (63, 127), (63, 0), (0, 63),
               (127, 63), (30, 42), (87, 18), (92, 92), (37, 85), (17, 93), (62, 81)]
    image_check = np.array([[axis[col], axis[row]] for row, col in indices]) @ rotation
    profile_check = np.column_stack((profile_axis[100:], np.zeros(101))) @ rotation
    plans = [(172, 14, 176, True), (244, 18, 240, True)]
    tangent_images = []
    sphere_profiles, tangent_profiles, exact_controls = [], [], []
    for plan in plans:
        projections, weights = source_nodes(model, chirp, plan)
        grid = tangent_grid(projections, weights, coordinates, model['wavelength'])
        tangent_images.append(grid[abs(ix+iy), abs(iy-ix)])
        exact, tangent = sampled_fields(projections, weights, np.vstack((image_check, profile_check)), model)
        exact_controls.append(exact[:len(indices)])
        sphere_profiles.append(np.r_[exact[:len(indices):-1], exact[len(indices):]])
        tangent_profiles.append(np.r_[tangent[:len(indices):-1], tangent[len(indices):]])
        print('Completed', plan, flush=True)
    edge = np.eye(2)
    directions, edge_phase = reference_sphere(model, edge[:, 0], edge[:, 1])
    off = model['trace'](*(edge@rotation.T).T)[1][:, :2]-model['chief'][1][0, :2]
    cone = .5/model['F']
    fft_mtf = abs(model['dirs'][0, 0])
    global_x_direction, _ = reference_sphere(model, np.array([rotation[0,0]]), np.array([rotation[0,1]]))
    global_x_projection = float((global_x_direction@rotation.T)[0, 0])
    witness = {'spherePrincipalEdgeDirections': directions.tolist(), 'principalImageEdgeMissMm': np.linalg.norm(off, axis=1).tolist(),
               'edgePhaseWaves': edge_phase.tolist(), 'fftPsfAndEnergyLinearDirectionScale': cone,
               'fftMtfLinearImageDirectionScale': fft_mtf, 'sphereGlobalXDirectionAtGlobalXPupilEdge': global_x_projection,
               'phaseDiscrepancyWavesAt5MicrometerPrincipalRadius': ((directions.diagonal()-cone)*5/model['wavelength']).tolist(),
               'phaseDiscrepancyWavesAt16MicrometerPrincipalRadius': ((directions.diagonal()-cone)*16/model['wavelength']).tolist(),
               'phaseDiscrepancyWavesAtProfileEndpointGlobalX': (global_x_projection-cone)*profile_axis[-1]/model['wavelength'],
               'mtfLinearCutoffCyclesPerMm': 2*fft_mtf/(model['wavelength']*1e-3),
               'sphereAxisProjectedSupportEndpointDifferenceOverWavelengthCyclesPerMm': 2*global_x_projection/(model['wavelength']*1e-3)}
    exact_image = np.asarray(exact_controls[-1])
    image = tangent_images[-1]
    profile = sphere_profiles[-1]
    fft_image = original['psf_field_by_tier'][-1]
    fft_profile = original['profile_field_by_tier'][-1]
    grid_index = tuple(np.asarray(indices).T)
    comparisons = {'tangentFullImageConvergence': metric(tangent_images[0], image),
                   'exactFullProfileConvergence': metric(sphere_profiles[0], profile, .002, .003),
                   'exactImageControlsConvergence': metric(exact_controls[0], exact_image),
                   'fftVsTangentFullImage': metric(fft_image, image),
                   'fftVsExactImageControls': metric(fft_image[grid_index], exact_image),
                   'tangentVsExactImageControls': metric(image[grid_index], exact_image),
                   'fftVsExactFullProfile': metric(fft_profile, profile, .002, .003),
                   'tangentVsExactFullProfile': metric(tangent_profiles[-1], profile, .002, .003)}
    assert max(comparisons[key]['peakNormalizedRms'] for key in comparisons if key.endswith('Convergence')) < 1e-7
    assert np.max(abs(np.array([image[63,63], profile[100]])-fft_image[63,63])) < 1e-10
    payload = args.output/'same-window-fields.npz'
    np.savez_compressed(payload, original_image_axis_um=axis, original_profile_axis_um=profile_axis,
                        tangent_image_fields=tangent_images, exact_profile_fields=sphere_profiles,
                        tangent_profile_fields=tangent_profiles, exact_image_control_fields=exact_controls,
                        exact_image_control_indices=indices)
    result = {'status': 'SharedPupilSphereKernelSensitivity', 'sourceSha256': shared['sha256'](model['source']),
              'nativeWavefrontSha256': shared['sha256'](model['ref']), 'referenceSphereMm': model['radius'],
              'referenceFftPayloadSha256': shared['sha256'](original_path.read_bytes()), 'plans': plans,
              'comparisons': comparisons, 'directionAndPhaseWitness': witness,
              'originalIrradianceGates': {'1365': {'absolute': 1e-5, 'relative': .001, 'nrmse': .01},
                                          '1369': {'absolute': 1e-5, 'relative': .002, 'nrmse': .003}},
              'normalization': 'Same unit-amplitude entrance-pupil measure divided by pi',
              'payloadSha256': shared['sha256'](payload.read_bytes())}
    (args.output/'results.json').write_text(json.dumps(result, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    print(json.dumps(result, indent=2), flush=True)


if __name__ == '__main__':
    main()
