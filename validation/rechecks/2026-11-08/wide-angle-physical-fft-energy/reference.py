import argparse
import importlib.util
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.optimize import brentq, newton
from scipy.special import j0, j1


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('physical_energy', HERE.parent / 'independent-physical-fft-energy/reference.py')
energy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(energy)

SOURCE_SHA = 'fd8a4a96d1dea380462078281b2db5c39185b087b6c85f36f298abbfd74e5c49'
FINGERPRINT = '3356e0d78d7761c958f8dba6d3904fa6d1c5c111f897d3c0e948efe8f969c388'


def read_native(n, frozen_settings, readback):
    folder = f'native/1994-{n}'
    data, settings, model = (energy.archived(f'{folder}/{name}') for name in
                             ('data.json', 'captured-settings.json', 'model.json'))
    assert [energy.sha(a) for a in (data, settings, model)] == [readback[k] for k in ('data', 'capture', 'model')]
    data = json.loads(energy.decode(data))
    settings = json.loads(energy.decode(settings))
    assert not data['messages'] and settings['selectors'] == frozen_settings['selectors']
    assert settings['properties']['SampleSize'] == f'S_{n}x{n}'
    assert not settings['properties']['UseHuygensPSF']
    def active(prop): return {k: v for k, v in prop.items() if k not in ('SampleSize', 'HuygensSample')}
    assert active(settings['properties']) == active(frozen_settings['properties'])
    for key in ('configuration', 'field', 'wavelength', 'useRayAiming', 'primaryWavelengthMicrometers'):
        assert settings['request'][key] == frozen_settings['request'][key]
    assert Path(settings['request']['input'].replace('\\', '/')).name == 'Wide angle lens 200 degree field.zmx'
    series = data['dataSeries']
    assert len(series) == 2 and series[0]['description'] == '衍射极限'
    for s in series:
        assert len(s['x']) == len(energy.R) and np.max(abs(np.asarray(s['x']) - energy.R)) < 1e-10
    curves = [np.asarray(s['y'])[:, 0] for s in series]
    assert np.all(np.isfinite(curves))
    report = energy.decode(energy.archived(f'{folder}/data.txt'))
    centers = [[1000*float(x), 1000*float(y)] for x,y in re.findall(r'X\s*=\s*([+-]?[\d.E+-]+)\s+Y\s*=\s*([+-]?[\d.E+-]+)', report)]
    assert len(centers) == 2
    return curves, centers


def run():
    index = 1994
    path, lens, header, surfaces = energy.source(index)
    assert energy.sha(path.read_bytes()) == SOURCE_SHA and 'UNIT IN' in header and 'FLOA' in header
    e_dir, w_dir, r_dir = (energy.frozen(n) for n in (1994, 1986, 1982))
    settings = energy.read(e_dir / 'captured-settings.json')
    wave_settings = energy.read(w_dir / 'captured-settings.json')
    ray_settings = energy.read(r_dir / 'captured-settings.json')
    assert settings['selectors'] == wave_settings['selectors']
    assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
    for control in (wave_settings, ray_settings):
        for key in ('configuration', 'input', 'useRayAiming', 'field', 'wavelength', 'primaryWavelengthMicrometers'):
            assert control['request'][key] == settings['request'][key]
    assert settings['request']['useRayAiming'] and settings['request']['configuration'] == 1
    assert [ray_settings['properties'][k] for k in ('Hx', 'Hy', 'Px', 'Py')] == [0, 0, 0, .8]
    wavelength = settings['request']['primaryWavelengthMicrometers']
    model = energy.read(w_dir / 'model.json')
    assert model['aperture']['ApodizationType'] == 'Uniform'
    stop = next(s for s in model['surfaces'] if s['IsStop'])
    assert stop['SurfaceNumber'] == 12 and model['aperture']['ApertureValue'] == stop['SemiDiameter']

    # Recover one index per glass from the signed tangential Snell invariant
    # of the archived native ray. Larger angles carry greater precision in
    # the printed direction cosines. Re-trace all physical intersections.
    end = max(surfaces)
    ray_raw = (r_dir / 'data.txt').read_bytes()
    report = energy.decode(ray_raw).split('近轴光线追迹数据')[0]
    rays = {int(i): np.array(list(map(float, row.split()[:9])))
            for i,row in re.findall(r'^\s*(\d+)\s+([^\r\n]+)', report, re.M)}
    assert sorted(rays) == list(range(1, end + 1))
    materials = {0: 'Air'}
    for i in range(1, end + 1):
        match = re.search(r'GLAS\s+([^\s]+)', surfaces[i])
        materials[i] = match.group(1) if match else 'Air'
    names = sorted(set(materials.values()) - {'Air'})
    matrix, rhs = [], []
    for i in range(2, end + 1):
        if materials[i] == materials[i-1]:
            continue
        current = rays[i]
        tangent = np.array([-current[8], current[7]])
        sine_in = np.dot(rays[i-1][4:6], tangent)
        sine_out = np.dot(current[4:6], tangent)
        weight = min(abs(sine_in), abs(sine_out))
        matrix.append([weight*((materials[i] == glass) - (materials[i-1] == glass)) for glass in names])
        rhs.append(weight*np.log(sine_in/sine_out))
    assert np.linalg.matrix_rank(matrix) == len(names)
    glass = dict(zip(names, map(float, np.exp(np.linalg.lstsq(matrix, rhs, rcond=None)[0]))), Air=1.)
    indices = {i: glass[material] for i, material in materials.items()}
    unit = 25.4

    def trace(height):
        direction = np.array([0., 1.])
        position = np.array([height, 0.])
        z = 0.
        states = {}
        for i in range(1, end + 1):
            text = surfaces[i]
            c = energy.setting(text, 'CURV') / unit
            k = energy.setting(text, 'CONI')
            def radical(r): return np.sqrt(1-(1+k)*(c*r)**2)
            def sag(r): return c*r*r/(1+radical(r))
            def slope(r): return c*r/radical(r)
            gap = (z-position[1])/direction[1]
            if c:
                def hit(t):
                    r, actual_z = position + t*direction
                    return actual_z - z - sag(r)
                def derivative(t): return direction[1] - slope(position[0] + t*direction[0])*direction[0]
                gap = newton(hit, gap, fprime=derivative, tol=2e-13, maxiter=24)
            position = position + gap*direction
            normal = np.array([slope(position[0]), -1.])
            normal /= np.linalg.norm(normal)
            cosine = -np.dot(direction, normal)
            mu = indices[i-1]/indices[i]
            direction = mu*direction + (mu*cosine - np.sqrt(1-mu*mu*(1-cosine*cosine)))*normal
            states[i] = np.array([position[0], position[1]-z, *direction])
            z += energy.setting(text, 'DISZ')*unit
        return states

    r08 = rays[1][1]
    ray_fit = trace(r08)
    errors = np.abs(np.array([ray_fit[i] - rays[i][[1, 2, 4, 5]] for i in range(1, end + 1)]))
    assert errors[:, :2].max() < 3e-8 and errors[:, 2:].max() < 2e-10
    def origin(pupil): return brentq(lambda r: trace(r)[12][0] - stop['SemiDiameter']*pupil, 3, 7, xtol=1e-12)
    assert abs(origin(.8) - r08) < 1e-8
    f_number = .5 / abs(trace(origin(1))[end][2])

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
    phase = Chebyshev.fit(r2[mask], z[mask], 14, domain=[0, 1])
    alternate = Chebyshev.fit(r2[quadrant], z[quadrant], 12, domain=[0, 1])
    fit_errors = [float(np.max(abs(p(r2[mask]) - z[mask]))) for p in (phase, alternate)]
    assert max(fit_errors) < 3e-7
    def ideal(t): return np.zeros_like(t)
    def integrate(p, n): return energy.curve(p, f_number, wavelength, n, [0], 0, ideal)
    continuum = [integrate(p, 96) for p in (ideal, phase)]
    q = np.pi * energy.R / (wavelength*f_number)
    airy_error = float(np.max(abs(continuum[0] - (1 - j0(q)**2 - j1(q)**2))))
    assert airy_error < 1e-13
    quadrature = [energy.compare(integrate(p, 64), b) for p, b in zip((ideal, phase), continuum)]
    alternate_curve = energy.compare(integrate(alternate, 96), continuum[1])
    assert all(m['verdict'] == 'Pass' for m in quadrature + [alternate_curve])

    authority = HERE.parent / 'diffraction-energy-third-cohort/comparisons.json'
    previous = next(r for r in energy.read(authority)['reports'] if r['id'] == index)
    assert previous['sourceSha256'] == SOURCE_SHA and previous['requestFingerprint'] == FINGERPRINT
    readback = {r['n']: r for r in previous['readbacks']}
    native, native_curves = [], []
    def inclusive(x, y): return x*x + y*y <= 1
    def exclusive(x, y): return x*x + y*y < 1 - 1e-13
    for n in (2048, 4096):
        curves, centers = read_native(n, settings, readback[n])
        stage = dict(n=n, centroidMicrometers=centers, continuum=[energy.compare(a, b) for a,b in zip(curves, continuum)])
        for method, boundary in [('inclusive', inclusive), ('exclusive', exclusive)]:
            for interpolate in (False, True):
                finite = [energy.finite(p, n, f_number, wavelength, boundary, interpolate_phase=interpolate,
                                       native_knots=interpolate) for p in (ideal, phase)]
                stage[method + ('NativeSampling' if interpolate else 'DirectPhase')] = [
                    dict(energy.compare(a, b), centroidMicrometers=center) for (a,center),b in zip(finite,curves)]
        assert all(m['verdict'] == 'Pass' for m in stage['inclusiveNativeSampling'])
        assert max(m['nrmse'] for m in stage['inclusiveNativeSampling']) < 1e-5
        assert np.max(abs(np.asarray(centers) - np.asarray([m['centroidMicrometers'] for m in stage['inclusiveNativeSampling']]))) < 5e-7
        native.append(stage)
        native_curves.append(curves)

    core, core_curves = [], []
    for n in (32768, 65536, 131072):
        pitch, curves, provenance = energy.core(index, n, settings)
        values = energy.archive_json(f'core/{index}-{n}/data.json')['values']
        bin_width = values['DistanceBinWidth'][0]
        assert abs(bin_width*np.sqrt(n)/wavelength - f_number) < 2e-9
        assert abs(pitch[0] / bin_width - 2*n/values['BoundedFourierSampling'][0]) < 1e-12
        core.append(dict(n=n, provenance=provenance, pitchMicrometers=pitch[0], distanceBinMicrometers=bin_width,
            continuum=[energy.compare(a,b) for a,b in zip(curves,continuum)]))
        core_curves.append(curves)
    core_pair = [energy.compare(a,b) for a,b in zip(*core_curves[-2:])]
    assert all(m['verdict'] == 'Pass' for m in core_pair + core[-1]['continuum'])
    assert native[-1]['inclusiveDirectPhase'][1]['verdict'] == 'Close'
    return dict(index=index, lens=lens, configuration=1, sourceSha256=SOURCE_SHA, requestFingerprint=FINGERPRINT,
        frozenVerdict='Incomparable', supplementalVerdict='SupplementalPass',
        frozenNative=dict(dataSha256=energy.sha((e_dir/'data.json').read_bytes()), diagnostics=energy.read(e_dir/'data.json')['messages']),
        reference=dict(workingFNumber=f_number, wavelengthMicrometers=wavelength, glassIndices=glass,
            rayReportSha256=energy.sha(ray_raw), maximumRayCoordinateErrorMillimeters=float(errors[:,:2].max()),
            maximumRayDirectionError=float(errors[:,2:].max()), floatingStopRadiusMillimeters=stop['SemiDiameter'],
            wavefrontSha256=energy.sha(wave_raw), wavefrontSettingsSha256=energy.sha((w_dir/'captured-settings.json').read_bytes()),
            wavefrontModelSha256=energy.sha((w_dir/'model.json').read_bytes()), phaseFitMaxWaves=fit_errors,
            illuminatedNodes=int(mask.sum()), quadrature=quadrature, alternatePhase=alternate_curve, idealClosedFormMaxError=airy_error,
            ideal=continuum[0].tolist(), actual=continuum[1].tolist()), native=native,
        nativePair=[energy.compare(a,b) for a,b in zip(*native_curves)], core=core, finestCorePair=core_pair,
        priorReadbackSha256=energy.sha(authority.read_bytes()))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    parser.add_argument('--repo', type=Path, default=HERE.parents[3])
    parser.add_argument('--corpus', type=Path)
    args = parser.parse_args()
    manifest = HERE/'manifest.json'
    energy.configure(args.repo, args.corpus, manifest)
    result = run()
    args.output.write_text(json.dumps(dict(policy=dict(energy.policy,minimumCoverage=.95),
        policySha256=energy.sha(energy.POLICY_PATH.read_bytes()), originalRadiusMicrometers=energy.R.tolist(),
        manifestSha256=energy.sha(manifest.read_bytes()), archivedLogicalPaths=sorted(energy.roles), result=result),
        ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


if __name__ == '__main__':
    main()
