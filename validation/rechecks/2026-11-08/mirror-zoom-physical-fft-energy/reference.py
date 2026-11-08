import argparse
import csv
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j1


HERE = Path(__file__).resolve().parent
SHARED = HERE.parent / 'independent-physical-fft-energy/reference.py'
spec = importlib.util.spec_from_file_location('physical_energy', SHARED)
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)

SOURCE_HASH = '1e753355a9b8394cb29d87c65b4580e1c33280149eb88aeb23840fcda4532cae'
CASES = (
    (2772, 1, (256, 512), 'e194763c218efd4d905c3b72ac570c5784839eb946bd6557e7fe316d6872c5c5'),
    (2794, 2, (4096, 8192), 'caa439f92eab26d0e1abdebd528c014f30fd985857c79a3fce8a80e4166bc2ce'),
    (2816, 3, (2048, 4096), '5dc977bae59ef6ca5db4ab099401d029e3aa0d2358ebbb3498a369c901a62cee'),
    (2838, 4, (4096, 8192), 'a00a26b6154e3b35a83c9dd39c790429d7e4adcf82f2ce2c2c70e01e96373809'),
)
CORE_STAGES = (32768, 65536, 131072)


def annulus_limit(f_number, wavelength, inner):
    nodes, weights = energy.legendre(48)
    u = np.pi * energy.R / (wavelength * f_number)
    v = u[:, None] * nodes
    amplitude = np.divide(j1(v) - inner * j1(inner * v), v,
                          out=np.full_like(v, (1 - inner**2) / 2), where=v != 0)
    return 2 * u**2 * (amplitude**2 @ (weights * nodes)) / (1 - inner**2)


def run(index, configuration, native_stages, fingerprint):
    path, lens, header, surfaces = energy.source(index)
    raw = path.read_bytes()
    assert energy.sha(raw) == SOURCE_HASH and 'UNIT CM' in header
    source_text = energy.decode(raw)
    energy_dir = energy.frozen(index)
    wavefront_dir = energy.frozen(index - 8)
    ray_dir = energy.frozen(index - 12)
    settings = energy.read(energy_dir / 'captured-settings.json')
    wave_settings = energy.read(wavefront_dir / 'captured-settings.json')
    ray_settings = energy.read(ray_dir / 'captured-settings.json')
    assert settings['request']['configuration'] == configuration
    assert settings['selectors'] == wave_settings['selectors']
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for control in (wave_settings, ray_settings):
        for key in ('configuration', 'input', 'useRayAiming', 'field', 'wavelength', 'primaryWavelengthMicrometers'):
            assert control['request'][key] == settings['request'][key]
    assert [ray_settings['properties'][k] for k in ('Hx', 'Hy', 'Px', 'Py')] == [0, 0, 0, .8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(wavefront_dir / 'model.json')
    assert model['aperture']['ApodizationType'] == 'Uniform'

    # The obscuration is a front-plane annulus at the fixed 150 mm entrance pupil.
    unit = 10
    pupil_radius = energy.setting(header, 'ENPD') * unit / 2
    min_radius = float(re.search(rf'^APMN\s+1\s+{configuration}\s+([^\s]+)', source_text, re.M).group(1)) * unit
    inner = min_radius / pupil_radius
    assert pupil_radius == 75 and model['aperture']['ApertureValue'] == 2 * pupil_radius
    config_gaps = {int(i): float(gap) * unit for i, gap in re.findall(
        rf'^THIC\s+(\d+)\s+{configuration}\s+([^\s]+)', source_text, re.M)}
    assert set(config_gaps) == {1, 3, 4, 5}
    for surface in model['surfaces'][1:-1]:
        i = surface['SurfaceNumber']
        curvature = energy.setting(surfaces[i], 'CURV') / unit
        if curvature:
            assert abs(1 / curvature - surface['Radius']) < 1e-8 and surface['Material'] == 'MIRROR'
        gap = surface['Thickness']
        if i in config_gaps:
            assert gap == config_gaps[i]
        # Surface six's final image gap is the solved native prescription value.
        surfaces[i] = re.sub(r'^\s*DISZ\s+[^\n]+', f'  DISZ {gap / unit}', surfaces[i], flags=re.M)

    def trace(p):
        return energy.trace(surfaces, set(range(1, 8)), np.array([p * pupil_radius, 0.]),
                            np.array([0., 1.]), wavelength, unit)[7]

    ray_raw = (ray_dir / 'data.txt').read_bytes()
    row = re.search(r'^\s*7\s+([^\r\n]+)', energy.decode(ray_raw), re.M)
    native_ray = np.array(list(map(float, row.group(1).split()[:6])))
    position, direction = trace(.8)
    independent_ray = np.array([0, position[0], 0, 0, *direction])
    assert np.max(abs(independent_ray[:3] - native_ray[:3])) < 1e-8
    assert np.max(abs(independent_ray[3:] - native_ray[3:])) < 1e-10
    f_number = .5 / abs(trace(1)[1][0])

    wave_raw = (wavefront_dir / 'data.json').read_bytes()
    wavefront = json.loads(energy.decode(wave_raw))
    assert not wavefront['messages']
    z = np.asarray(wavefront['dataGrids'][0]['values'], float)
    assert z.shape == (64, 64)
    axis = (np.arange(64) - 32) / 31
    x, y = np.meshgrid(axis, axis)
    r2 = x*x + y*y
    valid = np.isfinite(z)
    assert np.array_equal(valid, (r2 >= inner**2) & (r2 <= 1 + 1e-12))
    domain = [inner**2, 1]
    phase = Chebyshev.fit(r2[valid], z[valid], 10, domain=domain)
    quadrant = valid & (x >= 0) & (y >= 0)
    alternate = Chebyshev.fit(r2[quadrant], z[quadrant], 8, domain=domain)
    fit_error = float(np.max(abs(phase(r2[valid]) - z[valid])))
    alternate_error = float(np.max(abs(alternate(r2[valid]) - z[valid])))
    assert fit_error < 1e-7 and alternate_error < 1e-7

    def ideal(x): return np.zeros_like(x)
    def integrate(model, n):
        return energy.curve(model, f_number, wavelength, n, [0], inner, ideal)
    def transmission(x, y):
        r2 = x*x + y*y
        return (r2 >= inner**2) & (r2 <= 1)

    continuum = [integrate(p, 64) for p in (ideal, phase)]
    quadrature = [energy.compare(integrate(p, 32), y) for p, y in zip((ideal, phase), continuum)]
    alternate_curve = energy.compare(integrate(alternate, 64), continuum[1])
    limit_error = float(np.max(abs(continuum[0] - annulus_limit(f_number, wavelength, inner))))
    assert limit_error < 1e-13 and all(x['verdict'] == 'Pass' for x in quadrature + [alternate_curve])
    native_stages_results = []
    native_curves = []
    for n in native_stages:
        curves, centers = energy.native(index, n, settings)
        exact = [energy.finite(p, n, f_number, wavelength, transmission) for p in (ideal, phase)]
        sampled = [energy.finite(p, n, f_number, wavelength, transmission, interpolate_phase=True, native_knots=True)
                   for p in (ideal, phase)]
        native_stages_results.append(dict(n=n, centroidMicrometers=centers,
            continuum=[energy.compare(a, b) for a, b in zip(curves, continuum)],
            finite=[dict(energy.compare(a, b), centroidMicrometers=center) for (a, center), b in zip(exact, curves)],
            finiteNativeSampling=[dict(energy.compare(a, b), centroidMicrometers=center) for (a, center), b in zip(sampled, curves)]))
        native_curves.append(curves)

    if index == 2772:
        # The native 512 ideal centroid independently identifies the omitted
        # four outer diagonal cells. Compare this exact mask before and after
        # applying the same phase rule as in the other finite grids.
        def strict_outer(x, y):
            r2 = x*x + y*y
            return (r2 >= inner**2) & (r2 < 1 - 1e-13)
        extra = [energy.finite(p, 512, f_number, wavelength, strict_outer) for p in (ideal, phase)]
        native_stages_results[-1]['strictOuterDiagnostic'] = [dict(energy.compare(a, b), centroidMicrometers=center)
            for (a, center), b in zip(extra, native_curves[-1])]
        sampled = [energy.finite(p, 512, f_number, wavelength, strict_outer, interpolate_phase=True, native_knots=True)
                   for p in (ideal, phase)]
        native_stages_results[-1]['strictOuterNativeSampling'] = [dict(energy.compare(a, b), centroidMicrometers=center)
            for (a, center), b in zip(sampled, native_curves[-1])]
        assert all(x['verdict'] == 'Pass' for x in native_stages_results[0]['finiteNativeSampling'])
        assert abs(sampled[0][1][0] - native_stages_results[-1]['centroidMicrometers'][0][0]) < 1e-7
        assert abs(sampled[1][1][0] - native_stages_results[-1]['centroidMicrometers'][1][0]) < 5e-6

    core_stages_results = []
    core_curves = []
    for n in CORE_STAGES:
        pitch, curves, provenance = energy.core(index, n, settings)
        assert len(pitch) == 1 and abs(pitch[0] * np.sqrt(n) / wavelength - f_number) < 1e-9
        core_stages_results.append(dict(n=n, pitchMicrometers=pitch[0], provenance=provenance,
            continuum=[energy.compare(a, b) for a, b in zip(curves, continuum)]))
        core_curves.append(curves)

    core_pair = [energy.compare(a, b) for a, b in zip(*core_curves[-2:])]
    native_pair = [energy.compare(a, b) for a, b in zip(*native_curves)]
    assert all(x['verdict'] == 'Pass' for x in core_pair + core_stages_results[-1]['continuum'])
    method = {2772: 'strictOuterNativeSampling', 2816: 'finiteNativeSampling'}.get(index, 'continuum')
    passed = all(x['verdict'] == 'Pass' for x in native_stages_results[-1][method])
    assert passed
    return dict(index=index, lens=lens, configuration=configuration, requestFingerprint=fingerprint,
        sourceSha256=SOURCE_HASH, frozenVerdict='Incomparable', supplementalVerdict='SupplementalPass' if passed else 'Open',
        originalNative=dict(settingsSha256=energy.sha((energy_dir/'captured-settings.json').read_bytes()),
            dataSha256=energy.sha((energy_dir/'data.json').read_bytes()), diagnostics=energy.read(energy_dir/'data.json')['messages']),
        reference=dict(workingFNumber=f_number, wavelengthMicrometers=wavelength, innerRadiusMillimeters=min_radius,
            outerRadiusMillimeters=pupil_radius, nativeImageRay=native_ray.tolist(), independentImageRay=independent_ray.tolist(),
            rayReportSha256=energy.sha(ray_raw), raySettingsSha256=energy.sha((ray_dir/'captured-settings.json').read_bytes()),
            wavefrontSha256=energy.sha(wave_raw), wavefrontSettingsSha256=energy.sha((wavefront_dir/'captured-settings.json').read_bytes()),
            wavefrontModelSha256=energy.sha((wavefront_dir/'model.json').read_bytes()),
            illuminatedNodes=int(valid.sum()), phaseFitMaxWaves=fit_error, alternateFitMaxWaves=alternate_error,
            quadrature=quadrature, alternatePhase=alternate_curve, idealClosedFormMaxError=limit_error,
            ideal=continuum[0].tolist(), actual=continuum[1].tolist()),
        nativeControlMethod=method, native=native_stages_results, finestNativePair=native_pair,
        core=core_stages_results, finestCorePair=core_pair)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    parser.add_argument('--repo', type=Path, default=HERE.parents[3])
    parser.add_argument('--corpus', type=Path)
    args = parser.parse_args()
    manifest = HERE / 'manifest.json'
    energy.configure(args.repo, args.corpus, manifest)
    results = [run(*case) for case in CASES]
    output = dict(policy=dict(energy.policy, minimumCoverage=.95), policySha256=energy.sha(energy.POLICY_PATH.read_bytes()),
        originalRadiusMicrometers=energy.R.tolist(), manifestSha256=energy.sha(manifest.read_bytes()),
        archivedLogicalPaths=sorted(energy.roles), results=results)
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    with args.output.with_name('by-request.csv').open('w', encoding='utf-8', newline='') as file:
        rows = csv.writer(file)
        rows.writerow('index lens configuration requestFingerprint coreReferenceMaxNrmse corePairMaxNrmse nativeControl nativeReferenceMaxNrmse supplementalVerdict evidence'.split())
        def max_nrmse(metrics): return max(m['nrmse'] for m in metrics)
        for result in results:
            last = result['native'][-1]
            method = result['nativeControlMethod']
            rows.writerow([result[k] for k in ('index', 'lens', 'configuration', 'requestFingerprint')] + [
                max_nrmse(result['core'][-1]['continuum']), max_nrmse(result['finestCorePair']),
                f"native{last['n']}-{method}", max_nrmse(last[method]), result['supplementalVerdict'], args.output.name])


if __name__ == '__main__':
    main()
