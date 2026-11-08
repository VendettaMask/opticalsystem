import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

import numpy as np
from scipy.special import j0, roots_legendre


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(data):
    return hashlib.sha256(data).hexdigest()


report = Path(__file__).resolve().parent
repository = report.parents[3]
study = repository / 'artifacts/validation/precision-physical-fourier-20261108/full'
work = repository / 'artifacts/validation/streamed-physical-psf-20261108'
native_root = repository / 'artifacts/zemax-sequential-corpus/20261021/full-v1'
source = native_root / 'sources/Polarization/Fabry-Perot.ZMX'
native_wavefront = next((native_root / 'analyses').glob('2226-*/native/data.json'))
native_first_order = next((native_root / 'analyses').glob('2221-*/native/data.txt'))
native_ray = next((native_root / 'analyses').glob('2222-*/native/data.txt'))
snapshot = study / 'models/072-c1.optic.json'
policy = repository / 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
optic = read(snapshot)
assert optic['name'] == 'Fabry Perot Fringes' and not optic['imageSpaceAfocal']
assert optic['aperture']['kind'] == 'EntrancePupilDiameter'
radius = optic['aperture']['value'] / 2
surfaces = optic['surfaces']
assert all(surface['components']['geometryKind'] == 'plane' for surface in surfaces)
assert all(surface['radius'] == 0 and not surface['isReflective'] for surface in surfaces)
interaction = surfaces[4]['components']['interaction']
assert interaction['kind'] == 'paraxial_lens' and interaction['numbers']['powerX'] == interaction['numbers']['powerY']
power = interaction['numbers']['powerX']
wavelength = optic['wavelengths'][0]['nanometers'] / 1000
cone = power * radius
f_number = np.hypot(1, cone) / (2 * cone)

wave_grid = read(native_wavefront)['dataGrids'][0]
phase = np.array(wave_grid['values'], dtype=float)
row, col = np.indices(phase.shape)
integer_radius_squared = (row - 32)**2 + (col - 32)**2
mask = np.isfinite(phase)
assert phase.shape == (64, 64) and np.array_equal(mask, integer_radius_squared <= 31**2)
radial_knots = np.unique(integer_radius_squared[mask]) / 31**2
radii_squared = integer_radius_squared[mask] / 31**2
training = np.isin(radii_squared, radial_knots[::2])
coefficients = np.polynomial.polynomial.polyfit(radii_squared[training], phase[mask][training], 5)
coefficients_control = np.polynomial.polynomial.polyfit(radii_squared[~training], phase[mask][~training], 6)
holdout = np.abs(np.polynomial.polynomial.polyval(radii_squared[~training], coefficients) - phase[mask][~training])
assert max(holdout) < 2e-7


def field_intensity(x, order, model, ideal=False):
    nodes, weights = roots_legendre(order)
    radial = (nodes + 1) / 2
    coefficient = radial * weights
    if not ideal:
        waves = np.polynomial.polynomial.polyval(radial**2, model)
        coefficient = coefficient * np.exp(2j * np.pi * waves)
    argument = np.pi * np.asarray(x)[..., None] * radial / (wavelength * f_number)
    return np.abs(j0(argument) @ coefficient)**2


def compare(actual, reference, gate):
    error = np.abs(actual - reference)
    nrmse = np.sqrt(np.mean(error**2)) / np.max(np.abs(reference))
    pointwise = np.all(error <= gate['absolute'] + gate['relative'] * np.abs(reference))
    return dict(count=len(error.ravel()), coverage=1, nrmse=float(nrmse), maximumAbsolute=float(error.max()),
                pointwisePass=bool(pointwise), conclusion='Pass' if pointwise or nrmse <= gate['nrmse'] else 'Open')


profile_policy = read(policy)['analyses']['FFT PSF Cross Section']['quantities']['Irradiance']
energy_policy = read(policy)['analyses']['Diffraction Encircled Energy']['quantities']['EnergyFraction']
psf_policy = read(policy)['analyses']['PSF']['quantities']['Irradiance']
original_axis = np.linspace(-15 * wavelength * f_number, 15 * wavelength * f_number, 201)
results = {row['index']: row for row in read(study / 'results.json')}
files = {}


def load_product(index, tag, refinement):
    original = read(study / f'analyses/{index:04}/request.json')
    path = work / f'{tag}-request.json'
    request = read(path)
    expected = original['request']
    if index == 2234:
        expected['pupilSampling'] = refinement
        expected['workbenchSettings'].update(PupilSampling=str(refinement), Sampling=str(refinement))
    else:
        expected['workbenchSettings'].update(FourierSamplingMode='Adaptive', FourierMaximumSampling='4096',
                                             FourierRelativeAccuracy='0.001')
    assert request['request'] == expected and request['snapshotPath'] == original['snapshotPath']
    files.update({f'{tag}/{name}': work / tag / name for name in ('data.json', 'provenance.json')})
    files[f'{tag}/request.json'] = path
    provenance = read(work / tag / 'provenance.json')
    assert provenance['snapshotSha256'] == sha(snapshot.read_bytes())
    data = read(work / tag / 'data.json')
    assert data['outcome'] == 'Success'
    return data, provenance


profile, profile_provenance = load_product(2231, '2231-axis-final', None)
certificate = profile['values']['FourierSamplingValidation']
assert certificate['validated'] and certificate['relativeRmsAccuracy'] == .001
assert [step['pupilSampling'] for step in certificate['steps'][-2:]] == [1024, 2048]
assert all(step['phaseResolved'] for step in certificate['steps'][-2:])
points = profile['plotSeries'][0]['points']
x, y = np.array([(point['x'], point['y']) for point in points]).T
axis_error = float(max(abs(x - original_axis)))
assert axis_error < 2e-12
profile_reference = field_intensity(x, 256, coefficients)
profile_independent = compare(field_intensity(x, 192, coefficients_control), profile_reference, profile_policy)
profile_cross = compare(y, profile_reference, profile_policy)

# Use a separately captured and converged native 2D FFT as an operator control.
prior = report.parent / 'diffraction-psf-followup'
manifest = read(prior / 'manifest.json')
payload = next(x for x in manifest['logicalPayloads'] if x['logicalPath'] == 'native/2227-2048/data.json')
with ZipFile(prior / payload['archive']) as archive:
    native_psf_data = archive.read(payload['member'])
assert sha(native_psf_data) == payload['sha256']
native_psf = json.loads(native_psf_data)['dataGrids'][0]
assert native_psf['nx'] == native_psf['ny'] == 128 and native_psf['dx'] == native_psf['dy'] == .25
gx = native_psf['minX'] + native_psf['dx'] * np.arange(native_psf['nx'])
gy = native_psf['minY'] + native_psf['dy'] * np.arange(native_psf['ny'])
gr = np.hypot(gx[None, :], gy[:, None])
psf_reference = field_intensity(gr, 256, coefficients)
psf_control = compare(np.array(native_psf['values']), psf_reference, psf_policy)

radius_axis = np.linspace(0, 5, 401)
total_power = 4 * (wavelength * f_number)**2 / np.pi


def energy_curves(image_order, pupil_order, model):
    nodes, weights = roots_legendre(image_order)
    disk_radii = radius_axis[:, None] * (nodes + 1) / 2
    return [np.pi * radius_axis * ((field_intensity(disk_radii, pupil_order, model, ideal) * disk_radii) @ weights) / total_power
            for ideal in (True, False)]


reference_curves = energy_curves(96, 256, coefficients)
cross_check = energy_curves(80, 192, coefficients_control)
energy_independent = [compare(a, b, energy_policy) for a, b in zip(cross_check, reference_curves, strict=True)]
core = []
previous = None
for n in (32768, 65536, 131072):
    data, provenance = load_product(2234, f'2234-energy-{n}', n)
    assert len(data['plotSeries']) == 2
    measured = []
    for series in data['plotSeries']:
        assert series['xUnit'] == 'Micrometer' and series['xQuantity'] == 'Radius'
        axis, values = np.array([(point['x'], point['y']) for point in series['points']]).T
        assert np.max(np.abs(axis - radius_axis)) < 2e-15
        measured.append(values)
    cross = [compare(a, b, energy_policy) for a, b in zip(measured, reference_curves, strict=True)]
    pair = [compare(a, b, energy_policy) for a, b in zip(previous, measured, strict=True)] if previous is not None else None
    core.append(dict(n=n, cross=cross, versusPrevious=pair, provenance=provenance))
    previous = measured

assert all(x['conclusion'] == 'Pass' for x in [profile_independent, profile_cross, psf_control, *energy_independent, *core[-1]['cross'], *core[-1]['versusPrevious']])
numeric_archive = report / 'product-evidence.zip'
with ZipFile(numeric_archive, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
    for name, path in files.items():
        archive.writestr(ZipInfo(name), path.read_bytes(), compress_type=ZIP_DEFLATED, compresslevel=9)
with ZipFile(numeric_archive) as archive:
    assert all(sha(archive.read(name)) == sha(path.read_bytes()) for name, path in files.items())

receipt = dict(sourceSha256=sha(source.read_bytes()), snapshotSha256=sha(snapshot.read_bytes()),
               originalPolicySha256=sha(policy.read_bytes()), nativeWavefrontSha256=sha(native_wavefront.read_bytes()),
               nativeFirstOrderSha256=sha(native_first_order.read_bytes()), nativeRaySha256=sha(native_ray.read_bytes()),
               pupil=dict(radiusMillimeters=radius, nativeMaskNodes=int(mask.sum()), heldOutNodes=int((~training).sum()),
                          maximumHoldoutWaves=float(holdout.max()), radialPhaseCoefficients=coefficients.tolist(), workingFNumber=f_number),
               nativePsf2048OperatorControl=dict(payload=payload, comparison=psf_control),
               productArchiveSha256=sha(numeric_archive.read_bytes()), productPayloads={name: sha(path.read_bytes()) for name, path in files.items()},
               profile=dict(index=2231, requestFingerprint=results[2231]['requestFingerprint'], frozenVerdict=results[2231]['conclusion'],
                            originalAxisMaximumResidual=axis_error, independentConvergence=profile_independent,
                            cross=profile_cross, certificate=certificate, provenance=profile_provenance,
                            axis=x.tolist(), referenceIrradiance=profile_reference.tolist()),
               energy=dict(index=2234, requestFingerprint=results[2234]['requestFingerprint'], frozenVerdict=results[2234]['conclusion'],
                           fullParsevalPowerMicrometerSquared=total_power, independentConvergence=energy_independent,
                           radius=radius_axis.tolist(), idealAndActual=[curve.tolist() for curve in reference_curves], core=core))
(report / 'verification.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(profile_cross, psf_control, core[-1]['cross'], core[-1]['versusPrevious'])
