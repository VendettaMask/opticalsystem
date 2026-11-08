import argparse
import hashlib
import json
from pathlib import Path
import re

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j0, j1, roots_legendre


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


parser = argparse.ArgumentParser()
parser.add_argument('restored', type=Path, help='Directory containing the restored native/ and core/ logical paths')
parser.add_argument('--corpus', type=Path, default=Path('artifacts/zemax-sequential-corpus/20261021/full-v1/analyses'))
parser.add_argument('--source', type=Path, help='Original prescription if it has moved since native capture')
args = parser.parse_args()
corpus = args.corpus
wavefront = next(corpus.glob('0554-*')) / 'native'
frozen = next(corpus.glob('0562-*')) / 'native'
native_dirs = {n: args.restored / 'native' / f'562-{n}' for n in (512, 1024)}
raw = np.array(read(wavefront / 'data.json')['dataGrids'][0]['values'], dtype=float)
model = read(wavefront / 'model.json')
wave_settings = read(wavefront / 'captured-settings.json')
energy_settings = read(frozen / 'captured-settings.json')
assert wave_settings['request']['input'] == energy_settings['request']['input']
assert wave_settings['request']['useRayAiming'] == energy_settings['request']['useRayAiming'] is True
assert wave_settings['selectors']['Field'] == energy_settings['selectors']['Field'] == 1
assert wave_settings['selectors']['Wavelength'] == energy_settings['selectors']['Wavelength'] == 2
assert wave_settings['properties']['UseExitPupil'] and not wave_settings['properties']['RemoveTilt']
assert model['aperture']['ApodizationType'] == 'Uniform'
surfaces = model['surfaces']
stop = next(row for row in surfaces if row['IsStop'])
assert stop['SurfaceNumber'] == len(surfaces) - 2 and stop['Thickness'] < 0
f_number = abs(stop['Thickness']) / (2 * stop['SemiDiameter'])
wavelength = energy_settings['request']['primaryWavelengthMicrometers']
assert f_number == 200 and wavelength == 0.5875618
assert raw.shape == (64, 64) and raw[32, 32] == 0
axis = (np.arange(64) - 32) / 31
radius2 = axis[:, None] ** 2 + axis[None, :] ** 2
valid = np.isfinite(raw)
assert np.array_equal(valid, radius2 <= 1 + 1e-12)
fit = Chebyshev.fit(radius2[valid], raw[valid], 10, domain=[0, 1])
meridian = Chebyshev.fit(radius2[32, valid[32]], raw[32, valid[32]], 8, domain=[0, 1])
radius = np.linspace(0, 5, 401)


def legendre(n):
    x, w = roots_legendre(n)
    return (1 + x) / 2, w / 2


def continuous(phase, nodes):
    # Fraunhofer unit circular pupil. With t=r², amplitude is the integral of
    # P(sqrt(t))*J0(v*sqrt(t)) over [0,1]. Parseval gives integral |A(v)|² v dv=2.
    r, rw = legendre(nodes)
    t, tw = legendre(nodes)
    u = np.pi * radius / (wavelength * f_number)
    v = (u[:, None] * r).reshape(-1)
    field = j0(v[:, None] * np.sqrt(t)) @ (tw * np.exp(-2j * np.pi * phase(t)))
    fraction = 0.5 * u ** 2 * (abs(field.reshape(len(radius), nodes)) ** 2 @ (rw * r))
    return fraction


def compare(actual, expected):
    err = abs(actual - expected)
    nrmse = float(np.sqrt(np.mean(err**2)) / max(float(np.max(abs(expected))), 1e-5))
    pointwise = bool(np.all(err <= 1e-5 + .002 * abs(expected)))
    return dict(maxAbsolute=float(np.max(err)), nrmse=nrmse, pointwise=pointwise, coverage=1,
                verdict='Pass' if pointwise or nrmse <= .003 else ('Close' if nrmse <= .01 else 'Difference'))


def finite(phase, n, f):
    # These are the actual illuminated cells of the expanded nominal lattice.
    count = int(np.ceil(np.sqrt(n)))
    index = np.arange(-count, count + 1)
    t = (index[:, None] ** 2 + index[None, :] ** 2) / n
    disk = t <= 1
    pupil = np.zeros(t.shape, dtype=complex)
    pupil[disk] = np.exp(-2j * np.pi * phase(t[disk]))
    m = 2 * n
    pitch = wavelength * f / np.sqrt(n)
    power = float(np.sum(np.abs(pupil) ** 2))
    period = np.arange(-m // 2 + 1, m // 2 + 1)
    def image_center(rows):
        # Sum of row spectra is an exact full-grid marginal by Parseval on
        # the other axis, so no cropped-image normalization or centroid.
        marginal = np.zeros(m)
        for start in range(0, len(rows), 32):
            fft = np.fft.fft(rows[start:start+32], n=m, axis=1)
            marginal += np.sum(abs(fft)**2, axis=0)
        assert np.isclose(marginal.sum(), m * power, rtol=2e-14)
        return pitch * float(np.dot(period, marginal[period % m]) / (m * power))
    cx, cy = image_center(pupil), image_center(pupil.T)
    half = int(np.ceil((max(abs(cx), abs(cy)) + radius[-1]) / pitch + .5))
    k = np.arange(-half, half + 1)
    e = np.exp(-2j * np.pi * k[:, None] * index[None, :] / m)
    # Exact local DFT with full 2D Parseval power denominator.
    image = e @ pupil @ e.T
    fractions = abs(image) ** 2 / (m*m*power)
    distances = np.hypot(k[:, None] * pitch - cy, k[None, :] * pitch - cx)
    weights = np.clip((radius[:, None, None] - distances + pitch / 2) / pitch, 0, 1)
    curve = np.einsum('rxy,xy->r', weights, fractions)
    curve[0] = 0
    return curve, dict(center=[cx, cy], pitch=float(pitch), pupilNodes=int(disk.sum()),
                       fullPower=m*m*power, centerPixelWeight=float(fractions[half, half]))


ideal_phase = lambda t: np.zeros_like(t)
ideal = continuous(ideal_phase, 64)
actual = continuous(fit, 64)
independent_limit = 1 - j0(np.pi*radius/(wavelength*f_number))**2 - j1(np.pi*radius/(wavelength*f_number))**2
assert np.max(abs(independent_limit-ideal)) < 1e-14
source = args.source or Path(energy_settings['request']['input'])
out = dict(sourceSha256=sha(source), modelSha256=sha(wavefront/'model.json'),
           wavefrontSha256=sha(wavefront/'data.json'), wavefrontSettingsSha256=sha(wavefront/'captured-settings.json'),
           energySettingsSha256=sha(frozen/'captured-settings.json'), wavelengthMicrometers=wavelength,
           workingFNumber=f_number, nativeFitError=float(np.max(abs(fit(radius2[valid])-raw[valid]))),
           originalRadius=radius.tolist(), continuous=dict(ideal=ideal.tolist(), actual=actual.tolist(),
               quad=compare(continuous(fit, 32), actual), alternatePupil=compare(continuous(meridian, 64), actual),
               closedFormError=float(np.max(abs(ideal-independent_limit)))),
           finite=[], core=[])
for n in (512, 1024, 2048, 4096, 8192, 16384):
    finite_ideal, ideal_meta = finite(ideal_phase, n, f_number)
    finite_actual, actual_meta = finite(fit, n, f_number)
    row=dict(n=n, ideal=ideal_meta, actual=actual_meta,
             continuous=dict(ideal=compare(finite_ideal, ideal),actual=compare(finite_actual, actual)),
             r5=[float(finite_ideal[-1]),float(finite_actual[-1])])
    if n in native_dirs:
        path = native_dirs[n] / 'data.json'
        cap = read(native_dirs[n]/'captured-settings.json')
        settings = cap['properties']
        assert settings['SampleSize'] == f'S_{n}x{n}'
        assert {k: v for k, v in settings.items() if k != 'SampleSize'} == {
            k: v for k, v in energy_settings['properties'].items() if k != 'SampleSize'}
        assert cap['selectors'] == energy_settings['selectors']
        assert cap['request']['input'] == energy_settings['request']['input']
        assert cap['request']['useRayAiming'] == energy_settings['request']['useRayAiming']
        groups = read(path)['dataSeries']
        assert len(groups)==2 and all(np.allclose(g['x'], radius, atol=1e-10, rtol=0) for g in groups)
        ni, na = (np.array(g['y'])[:,0] for g in groups)
        report = (native_dirs[n]/'data.txt').read_bytes()
        text = report.decode('utf-16' if report.startswith(b'\xff\xfe') else 'utf-8-sig')
        published = [[float(x),float(y)] for x,y in re.findall(r'X\s*=\s*([+-]?[\d.E+-]+)\s+Y\s*=\s*([+-]?[\d.E+-]+)',text)]
        row['native']=dict(sha256=sha(path), r5=[float(ni[-1]),float(na[-1])], textCentersMillimeters=published,
            finite=dict(ideal=compare(finite_ideal,ni),actual=compare(finite_actual,na)),
            continuous=dict(ideal=compare(ideal,ni),actual=compare(actual,na)))
    out['finite'].append(row)
for n in (1024,2048,4096,8192,16384):
    path=args.restored/'core'/f'562-{n}'/'data.json'
    core=read(path)
    values=core['values']
    curves=core['seriesList']
    vals=[np.interp(radius,[point['x'] for point in c['points']], [point['y'] for point in c['points']]) for c in curves]
    out['core'].append(dict(n=n, sha256=sha(path), imagePitch=values.get('BoundedImagePitch'),r5=[float(z[-1]) for z in vals],
        continuous=dict(ideal=compare(vals[0],ideal),actual=compare(vals[1],actual))))
(args.restored/'analysis.json').write_text(json.dumps(out,indent=2)+'\n',encoding='utf-8')
print('continuum r5', ideal[-1], actual[-1])
for row in out['finite']:
    print('finite',row['n'],row['r5'],row.get('native'))
for row in out['core']:
    print('core',row)
