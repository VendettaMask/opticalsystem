"""Check a certified streamed EE replay against a sealed independent reference."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

import numpy as np


parser = argparse.ArgumentParser()
parser.add_argument('repo', type=Path)
parser.add_argument('reference', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--replay', action='append', nargs=2, required=True, type=Path, metavar=('DATA', 'REQUEST'))
args = parser.parse_args()
sha = lambda data: hashlib.sha256(data).hexdigest()
reference_bytes = args.reference.read_bytes()
reference = json.loads(reference_bytes)
archive_path = args.repo/'validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip'
with archive_path.open('rb') as stream:
    assert hashlib.file_digest(stream, 'sha256').hexdigest() == reference['frozenArchiveSha256']
with ZipFile(archive_path) as archive:
    original_bytes = archive.read(reference['frozenRequestMember'])
    assert sha(original_bytes) == reference['frozenRequestSha256']
original = json.loads(original_bytes)['request']
gate = reference['originalGate']
radii = np.array(reference['radiusMicrometers'])
components = reference['components']
assert [component['name'] for component in components] == ['DiffractionLimit', 'Actual']
assert len(radii) == 401 and radii[0] == 0 and radii[-1] == 5
controls = {'FourierSamplingMode', 'FourierMaximumSampling', 'FourierRelativeAccuracy'}
assert not original['workbenchSettings'].keys() & controls


def metric(candidate, expected):
    error = np.abs(candidate-expected)
    assert len(error) == len(radii) and np.all(np.isfinite(error))
    nrmse = float(np.sqrt(np.mean(error**2))/max(expected.max(), gate['absolute']))
    pointwise = bool(np.all(error <= gate['absolute']+gate['relative']*np.abs(expected)))
    return {'count': len(error), 'coverage': 1, 'nrmse': nrmse, 'maximumAbsolute': float(error.max()),
            'pointwisePass': pointwise, 'conclusion': 'Pass' if pointwise or nrmse <= gate['nrmse'] else 'Difference'}


replays = []
for data_path, request_path in args.replay:
    data_bytes, request_bytes = data_path.read_bytes(), request_path.read_bytes()
    data, request = json.loads(data_bytes), json.loads(request_bytes)['request']
    settings = request['workbenchSettings']
    assert {**request, 'workbenchSettings': {k: v for k, v in settings.items() if k not in controls}} == original
    adaptation = {key: settings[key] for key in sorted(controls)}
    assert adaptation['FourierSamplingMode'] == 'Adaptive'
    values = data['values']
    certificate = values['FourierSamplingValidation']
    assert certificate['requestedPupilSampling'] == original['pupilSampling']
    assert certificate['maximumPupilSampling'] == int(adaptation['FourierMaximumSampling'])
    assert certificate['relativeRmsAccuracy'] == float(adaptation['FourierRelativeAccuracy'])
    receipt = {'outcome': data['outcome'], 'reason': data.get('outcomeReason'), 'adaptation': adaptation,
               'dataSha256': sha(data_bytes), 'requestSha256': sha(request_bytes), 'certificate': certificate,
               'provenance': json.loads(data_path.with_name('provenance.json').read_bytes())}
    if data['outcome'] == 'Success':
        assert certificate['validated'] and certificate['protocol'] == 'FourierPhysicalDomainConvergence/v3'
        assert len(certificate['steps']) >= 2
        low, high = certificate['steps'][-2:]
        assert low['pupilSampling'] < high['pupilSampling'] == certificate['returnedPupilSampling']
        for step in (low, high):
            assert step['phaseResolved'] and step['usesStreamedPhysicalPupil']
            assert 0 < step['maximumPupilPhaseStepWaves'] <= .5
        assert (high['relativeRmsChange'] <= certificate['relativeRmsAccuracy']
                or high['maximumAbsoluteChange'] <= certificate['absoluteAccuracy'])
        series = data['seriesList']
        assert len(series) == 2 and series[0]['name'] == '衍射极限' and series[1]['name'] != series[0]['name']
        comparisons = {}
        for curve, component in zip(series, components):
            assert (curve['xUnit'], curve['xQuantity']) == ('Micrometer', 'Radius')
            assert (curve['yUnit'], curve['yQuantity']) == ('Dimensionless', 'EnergyFraction')
            points = np.array([(point['x'], point['y']) for point in curve['points']])
            assert len(points) == len(radii) and np.max(np.abs(points[:, 0]-radii)) < 1e-13
            assert points[0, 1] == 0 and np.all(np.diff(points[:, 1]) >= 0) and points[-1, 1] <= 1
            comparisons[component['name']] = metric(points[:, 1], np.array(component['referenceEnergyFraction']))
        receipt['productCentroidMicrometers'] = [values['FourierEnergyCentroidXMicrometers'],
                                               values['FourierEnergyCentroidYMicrometers']]
        receipt['components'] = comparisons
        receipt['independentOutcome'] = ('SupplementalPass' if all(m['conclusion'] == 'Pass'
                                        for m in comparisons.values()) else 'Open')
    else:
        assert not certificate['validated']
        receipt['independentOutcome'] = 'Open'
    replays.append(receipt)

report = {'index': reference['index'], 'frozenVerdict': reference['frozenVerdict'], 'originalGate': gate,
          'independentReferenceSha256': sha(reference_bytes), 'frozenRequestSha256': sha(original_bytes),
          'formalReplays': replays, 'independentOutcome': ('SupplementalPass' if any(
              receipt['independentOutcome'] == 'SupplementalPass' for receipt in replays) else 'Open')}
args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
