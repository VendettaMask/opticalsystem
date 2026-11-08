import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from numpy.polynomial import Chebyshev
from scipy.special import j0, roots_legendre


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent
corpus = Path('artifacts/zemax-sequential-corpus/20261021/full-v1/analyses')
native_wavefront = next(corpus.glob('0554-*')) / 'native'
native_psf = next(corpus.glob('0555-*')) / 'native'
native_ray = next(corpus.glob('0550-*')) / 'native'
wavefront = read(native_wavefront / 'data.json')['dataGrids'][0]
raw = np.array(wavefront['values'], dtype=float)
native_settings = read(native_wavefront / 'captured-settings.json')
psf_settings = read(native_psf / 'captured-settings.json')
assert native_settings['request']['input'] == psf_settings['request']['input']
assert native_settings['request']['useRayAiming'] == psf_settings['request']['useRayAiming'] is True
assert native_settings['selectors'] == psf_settings['selectors']
assert native_settings['properties']['UseExitPupil'] and not native_settings['properties']['RemoveTilt']
native_model = read(native_ray / 'model.json')
surfaces = native_model['surfaces']
stop = next(row for row in surfaces if row['IsStop'])
assert stop['SurfaceNumber'] == len(surfaces) - 2
f_number = abs(stop['Thickness']) / (2 * stop['SemiDiameter'])
assert f_number == 200
assert native_model['aperture']['ApodizationType'] == 'Uniform'
lambda_um = psf_settings['request']['primaryWavelengthMicrometers']

# OpticStudio's even 64-node scalar wavefront puts the zero OPD chief at
# index 32. Native circular edge is at +/-31 indices. Verify the entire mask.
assert raw.shape == (64, 64) and raw[32, 32] == 0
axis = (np.arange(64) - 32) / 31
radius2 = axis[:, None] ** 2 + axis[None, :] ** 2
valid = np.isfinite(raw)
assert np.array_equal(valid, radius2 <= 1 + 1e-12)
t, phase = radius2[valid], raw[valid]
fit = Chebyshev.fit(t, phase, 10, domain=[0, 1])
fit_line = Chebyshev.fit(radius2[32, valid[32]], raw[32, valid[32]], 8, domain=[0, 1])
assert np.max(np.abs(fit_line(t) - phase)) < 1e-6

image_axis = np.arange(-63, 65) * psf_settings['properties']['ImageDelta']
pixel_radius = np.sqrt(image_axis[:, None] ** 2 + image_axis[None, :] ** 2)
beta = np.pi * pixel_radius / (lambda_um * f_number)


def integrate(model, nodes):
    # With t = r², 2r dr = dt on the uniformly illuminated unit pupil.
    x, w = roots_legendre(nodes)
    x, w = (1 + x) / 2, w / 2
    amplitude = np.einsum('xyr,r->xy', j0(beta[:, :, None] * np.sqrt(x)),
                          w * np.exp(-2j * np.pi * model(x)))
    return np.abs(amplitude) ** 2


def compare(actual, reference):
    err = actual - reference
    absolute = float(np.max(np.abs(err)))
    nrmse = float(np.sqrt(np.mean(err ** 2)) / np.max(np.abs(reference)))
    pointwise = bool(np.all(np.abs(err) <= 1e-5 + 0.001 * np.abs(reference)))
    return dict(nodes=err.size, maxAbsolute=absolute, nrmse=nrmse, pointwise=pointwise,
                verdict='Pass' if pointwise or nrmse <= .01 else ('Close' if nrmse <= .03 else 'Difference'))


fine = integrate(fit, 128)
original_source = Path(psf_settings['request']['input'])
results = dict(nativeWavefrontSha256=sha(native_wavefront / 'data.json'),
               nativeWavefrontSettingsSha256=sha(native_wavefront / 'captured-settings.json'),
               nativePsfSettingsSha256=sha(native_psf / 'captured-settings.json'),
               nativePrescriptionSha256=sha(native_ray / 'model.json'), sourceSha256=sha(original_source),
               finiteNativePupilNodes=int(valid.sum()), nativeWavefrontMaximumFitErrorWaves=float(np.max(abs(fit(t)-phase))),
               nativeMeridianMaximumHoldoutErrorWaves=float(np.max(abs(fit_line(t)-phase))),
               nativeWorkingFNumber=f_number, axisMicrometers=image_axis.tolist(),
               nativePupilPhaseFitDegree=10, coarseReferenceFitDegree=8, wavelengthMicrometers=lambda_um,
               pupilReferenceConvergence=compare(integrate(fit_line, 64), fine),
               quadratureConvergence=compare(integrate(fit, 64), fine),
               originalTolerance=dict(absolute=1e-5, relative=.001, nrmse=.01, closeNrmse=.03, minimumCoverage=.95),
               core=[], coreConvergence=[])

prior = None
probes = sorted(root.glob('probe-*.json'), key=lambda p: int(p.stem.split('-')[1]))
for path in probes:
    probe = read(path)
    core = np.array(probe['image'])
    assert np.array_equal(image_axis, probe['imageXMicrometers']) and core.shape == (128, 128)
    n = probe['pupilSampling']
    results['core'].append(dict(n=n, sha256=sha(path), phase=probe['PupilPhaseSampling'],
                                comparison=compare(core, fine)))
    if prior is not None:
        results['coreConvergence'].append(dict(n=[prior[0], n], comparison=compare(prior[1], core)))
    prior = n, core

formal_path = root / 'formal-final' / 'data.json'
if formal_path.exists():
    formal = read(formal_path)
    assert formal['outcome'] == 'Success' and formal['values']['PhysicalFourierWindow'] is True
    certificate = formal['values']['FourierSamplingValidation']
    assert certificate['validated'] and certificate['returnedPupilSampling'] == 1024
    assert [step['pupilSampling'] for step in certificate['steps'][-2:]] == [512, 1024]
    assert all(step['phaseResolved'] and step['usesPhysicalPsfWindow'] for step in certificate['steps'][-2:])
    points = formal['plotSeries'][0]['points']
    assert len(points) == 16384
    assert np.array_equal(np.array([p['x'] for p in points]).reshape(128,128), np.tile(image_axis,(128,1)))
    assert np.array_equal(np.array([p['y'] for p in points]).reshape(128,128), np.tile(image_axis[:,None],(1,128)))
    image = np.array([p['value'] for p in points]).reshape(128,128)
    provenance = read(formal_path.parent / 'provenance.json')
    baseline_request = read(Path('artifacts/validation/precision-fft-continuation-20261108/full/analyses/0555/request.json'))
    formal_request = read(root / 'formal-final-request.json')
    changed = {key: formal_request['request']['workbenchSettings'].pop(key) for key in
               ('FourierSamplingMode', 'FourierMaximumSampling', 'FourierRelativeAccuracy')}
    assert formal_request['request'] == baseline_request['request']
    assert sha(Path(baseline_request['snapshotPath'])) == provenance['snapshotSha256']
    results['formal'] = dict(sha256=sha(formal_path), provenance=provenance, onlyChangedWorkbenchSettings=changed,
                             certificate=certificate, comparison=compare(image, fine),
                             sameTierControl=compare(image, prior[1]))

(root / 'reference-results.json').write_text(json.dumps(results, indent=2) + '\n', encoding='utf-8')
for row in results['core'] + results['coreConvergence']:
    print(row['n'], row['comparison'])
if 'formal' in results:
    print('formal', results['formal']['comparison'], results['formal']['sameTierControl'])
