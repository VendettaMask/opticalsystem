import argparse
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j0, j1


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('physical_energy', HERE.parent / 'independent-physical-fft-energy/reference.py')
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)


def run():
    index = 974
    path, lens, header, surfaces = energy.source(index)
    source_hash = energy.sha(path.read_bytes())
    assert source_hash == '6531e67cfdc292d68ad4c5bd23fef5912f69ad9a69879047ec52e818a8b399a2'
    e_dir, w_dir, r_dir = (energy.frozen(n) for n in (974, 966, 962))
    settings = energy.read(e_dir / 'captured-settings.json')
    wave_settings = energy.read(w_dir / 'captured-settings.json')
    ray_settings = energy.read(r_dir / 'captured-settings.json')
    assert settings['selectors'] == wave_settings['selectors']
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for control in (wave_settings, ray_settings):
        for key in ('configuration', 'input', 'useRayAiming', 'field', 'wavelength', 'primaryWavelengthMicrometers'):
            assert control['request'][key] == settings['request'][key]
    assert not settings['request']['useRayAiming'] and settings['request']['configuration'] == 1
    assert [ray_settings['properties'][k] for k in ('Hx', 'Hy', 'Px', 'Py')] == [0, 0, 0, .8]
    model = energy.read(w_dir / 'model.json')
    assert model['aperture']['ApodizationType'] == 'Uniform'
    wavelength = settings['request']['primaryWavelengthMicrometers']

    # Both diagonal interfaces in the transmitted arm have BK7 on both
    # sides, and the intervening coordinate break is the identity. They can
    # be replaced by homogeneous propagation through the cube.
    assert all('GLAS BK7' in surfaces[i] for i in (3, 4, 6))
    assert all(energy.setting(surfaces[5], f'PARM {i}') == 0 for i in range(1, 7))
    assert energy.setting(surfaces[4], 'DISZ') == energy.setting(surfaces[5], 'DISZ') == 0
    radius = energy.setting(header, 'ENPD') / 2
    distance = energy.setting(surfaces[0], 'DISZ')
    assert radius == 10 and distance == 60
    end = max(surfaces)

    def trace(p):
        d = np.array([p * radius / distance, 1.])
        return energy.trace(surfaces, set(surfaces) - {0, 4, 5}, np.zeros(2), d / np.linalg.norm(d), wavelength)

    ray_raw = (r_dir / 'data.txt').read_bytes()
    row = re.search(rf'^\s*{end}\s+([^\r\n]+)', energy.decode(ray_raw), re.M)
    native_ray = np.array(list(map(float, row.group(1).split()[:6])))
    position, direction = trace(.8)[end]
    independent_ray = np.array([0, position[0], 0, 0, *direction])
    assert np.max(abs(independent_ray[:3] - native_ray[:3])) < 1e-8
    assert np.max(abs(independent_ray[3:] - native_ray[3:])) < 1e-10
    f_number = .5 / abs(trace(1)[end][1][0])

    wave_raw = (w_dir / 'data.json').read_bytes()
    wave = json.loads(energy.decode(wave_raw))
    assert not wave['messages']
    z = np.asarray(wave['dataGrids'][0]['values'], float)
    axis = (np.arange(64) - 32) / 31
    x, y = np.meshgrid(axis, axis)
    r2 = x*x + y*y
    mask = np.isfinite(z)
    assert np.array_equal(mask, r2 <= 1 + 1e-12)
    quadrant = mask & (x >= 0) & (y >= 0)
    phase = Chebyshev.fit(r2[mask], z[mask], 10, domain=[0, 1])
    alternate = Chebyshev.fit(r2[quadrant], z[quadrant], 8, domain=[0, 1])
    errors = [float(np.max(abs(p(r2[mask]) - z[mask]))) for p in (phase, alternate)]
    assert max(errors) < 1e-7

    # An independent physical phase control uses the original exit-pupil
    # distance and the ray optical path to the reference sphere. A rational
    # form of the chord correction avoids losing the small OPD to rounding.
    first_order = (energy.frozen(961) / 'data.txt').read_bytes()
    pupil_distance = abs(float(re.search(r'出瞳位置\s*:\s*([-+.\dE]+)', energy.decode(first_order)).group(1)))
    def optical_path(p):
        states = trace(p)
        last = np.zeros(2)
        index = 1.
        length = 0.
        for i, (position, direction) in states.items():
            length += index * np.linalg.norm(position - last)
            last = position
            index = energy.bk7(wavelength) if 'GLAS BK7' in surfaces[i] else 1.
        return length, position[0], direction[0]

    phase_axis = np.unique(r2[mask])
    length, position, direction = np.asarray([optical_path(np.sqrt(t)) for t in phase_axis]).T
    transverse = position**2 * (1 - direction**2)
    chord = transverse / (pupil_distance + np.sqrt(pupil_distance**2 - transverse))
    physical_phase = -(length - length[0] - position * direction + chord) / (wavelength * .001)
    optical_path_error = float(np.max(abs(physical_phase - phase(phase_axis))))
    assert optical_path_error < 2e-9
    traced_phase = Chebyshev.fit(phase_axis, physical_phase, 14, domain=[0, 1])

    def ideal(t): return np.zeros_like(t)
    def integrate(p, n): return energy.curve(p, f_number, wavelength, n, [0], 0, ideal)
    continuum = [integrate(p, 96) for p in (ideal, phase)]
    q = np.pi * energy.R / (wavelength * f_number)
    airy_error = float(np.max(abs(continuum[0] - (1 - j0(q)**2 - j1(q)**2))))
    assert airy_error < 1e-13
    quadrature = [energy.compare(integrate(p, 64), b) for p, b in zip((ideal, phase), continuum)]
    alternate_reference = energy.compare(integrate(alternate, 96), continuum[1])
    traced_reference = energy.compare(integrate(traced_phase, 96), continuum[1])
    assert all(m['verdict'] == 'Pass' for m in quadrature + [alternate_reference, traced_reference])

    def inclusive(x, y): return x*x + y*y <= 1
    def exclusive(x, y): return x*x + y*y < 1 - 1e-13
    native = []
    native_curves = []
    for n in (1024, 2048, 4096):
        curves, centers = energy.native(index, n, settings)
        record = dict(n=n, centroidMicrometers=centers, continuum=[energy.compare(a, b) for a, b in zip(curves, continuum)])
        for method, boundary in [('finiteInclusive', inclusive), ('finiteExclusive', exclusive)]:
            finite = [energy.finite(p, n, f_number, wavelength, boundary) for p in (ideal, phase)]
            record[method] = [dict(energy.compare(a, b), centroidMicrometers=center) for (a, center), b in zip(finite, curves)]
            sampled = [energy.finite(p, n, f_number, wavelength, boundary, interpolate_phase=True, native_knots=True)
                       for p in (ideal, traced_phase)]
            record[method + 'NativeSampling'] = [dict(energy.compare(a, b), centroidMicrometers=center)
                                                  for (a, center), b in zip(sampled, curves)]
        native.append(record)
        native_curves.append(curves)

    core = []
    core_curves = []
    for n in (32768, 65536, 131072):
        pitch, curves, provenance = energy.core(index, n, settings)
        data = energy.archive_json(f'core/{index}-{n}/data.json')
        values = data['values']
        # The most extended output uses the bounded transform pitch of the
        # stored Fourier grid and the finer distance-bin width of the curve.
        bin_width = values['DistanceBinWidth'][0]
        assert abs(bin_width * np.sqrt(n) / wavelength - f_number) < 1e-9
        assert abs(pitch[0] / bin_width - 2 * n / values['BoundedFourierSampling'][0]) < 1e-12
        core.append(dict(n=n, provenance=provenance, pitchMicrometers=pitch[0], distanceBinMicrometers=bin_width,
            continuum=[energy.compare(a, b) for a, b in zip(curves, continuum)]))
        core_curves.append(curves)
    core_pair = [energy.compare(a, b) for a, b in zip(*core_curves[-2:])]
    _, core4096, core4096_provenance = energy.core(index, 4096, settings)
    same4096 = [energy.compare(a, b) for a, b in zip(core4096, native_curves[-1])]
    assert all(m['verdict'] == 'Pass' for m in core_pair + core[-1]['continuum'] + native[1]['continuum'])
    assert native[-1]['finiteInclusive'][1]['verdict'] == 'Close'
    assert native[-1]['finiteExclusive'][1]['verdict'] == 'Pass'
    assert all(m['verdict'] == 'Pass' for m in same4096 + native[1]['finiteInclusiveNativeSampling']
               + native[-1]['finiteInclusiveNativeSampling'])
    assert max(m['nrmse'] for m in native[-1]['finiteInclusiveNativeSampling']) < 1e-5
    assert max(abs(a - b) for a, b in zip(native[-1]['centroidMicrometers'][1],
               native[-1]['finiteInclusiveNativeSampling'][1]['centroidMicrometers'])) < 5e-7

    return dict(index=index, lens=lens, configuration=1, sourceSha256=source_hash,
        requestFingerprint='ffee500d939cb5e9c3764a05bea931c95cc5009e0b842ea950d37a53fc459d24',
        frozenVerdict='Incomparable', supplementalVerdict='SupplementalPass',
        frozenNative=dict(settingsSha256=energy.sha((e_dir/'captured-settings.json').read_bytes()),
            dataSha256=energy.sha((e_dir/'data.json').read_bytes()), diagnostics=energy.read(e_dir/'data.json')['messages']),
        independent=dict(workingFNumber=f_number, wavelengthMicrometers=wavelength, nativeImageRay=native_ray.tolist(),
            independentImageRay=independent_ray.tolist(), rayReportSha256=energy.sha(ray_raw),
            wavefrontSha256=energy.sha(wave_raw), wavefrontModelSha256=energy.sha((w_dir/'model.json').read_bytes()),
            wavefrontSettingsSha256=energy.sha((w_dir/'captured-settings.json').read_bytes()),
            phaseFitMaxWaves=errors, illuminatedNodes=int(mask.sum()), idealClosedFormMaxError=airy_error,
            quadrature=quadrature, alternateReference=alternate_reference,
            firstOrderReportSha256=energy.sha(first_order), exitPupilDistanceMillimeters=pupil_distance,
            tracedOpticalPathMaxPhaseErrorWaves=optical_path_error, tracedPhaseReference=traced_reference,
            ideal=continuum[0].tolist(), actual=continuum[1].tolist()),
        native=native, finestNativePair=[energy.compare(a, b) for a, b in zip(*native_curves[-2:])],
        core=core, finestCorePair=core_pair, same4096=dict(provenance=core4096_provenance, metrics=same4096))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    parser.add_argument('--repo', type=Path, default=HERE.parents[3])
    parser.add_argument('--corpus', type=Path)
    args = parser.parse_args()
    manifest = HERE / 'manifest.json'
    energy.configure(args.repo, args.corpus, manifest)
    result = run()
    args.output.write_text(json.dumps(dict(policy=dict(energy.policy, minimumCoverage=.95),
        policySha256=energy.sha(energy.POLICY_PATH.read_bytes()), originalRadiusMicrometers=energy.R.tolist(),
        manifestSha256=energy.sha(manifest.read_bytes()), archivedLogicalPaths=sorted(energy.roles), result=result),
        ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
