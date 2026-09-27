"""Audit completed evidence; no optical formulas, evaluation, or input updates."""

import hashlib
import json
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[4]
out = Path(__file__).resolve().parent
raw = root / 'artifacts/validation/dsearch-r2-20260926'


def read(path):
    return json.loads(path.read_text())


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


freeze = read(raw / 'source-freeze.json')
for path, expected in freeze['SourceSha256'].items():
    assert sha(root / path) == expected, path
for name, expected in freeze['BinarySha256'].items():
    assert sha(raw / 'frozen-v10-bin' / name) == expected, name
    runner = root / 'labs/InitialStructure/tools/OptilandWorkbench.InitialStructure.Benchmarks/bin/Release/net10.0'
    assert sha(runner / name) == expected, name

v8 = read(out.parent / 'dsearch-v8-20260926/source-freeze.json')
core_paths = [p for p in freeze['SourceSha256'] if p.startswith('src/OptilandWorkbench.Core/')]
assert len(core_paths) == 160
assert all(freeze['SourceSha256'][p] == v8['SourceSha256'][p] for p in core_paths)
assert freeze['BinarySha256']['OptilandWorkbench.Core.dll'] == v8['BinarySha256']['OptilandWorkbench.Core.dll']

initial = read(out / 'ablation-600.json')
final = read(out / 'ablation-600-final.json')
assert final['EngineSha256'] == freeze['BinarySha256']['OptilandWorkbench.InitialStructure.Engine.dll']
assert final['CoreSha256'] == freeze['BinarySha256']['OptilandWorkbench.Core.dll']
assert final['InputSha256'] == initial['InputSha256'] == sha(out.parent / 'dsearch-r1-20260926/inputs.json')
assert len(initial['Results']) == len(final['Results']) == 32
for before, after in zip(initial['Results'], final['Results']):
    assert all(after[k] == value for k, value in before.items()), (after['Focal'], after['Signs'], after['Mode'])
    assert after['BestRecalculationIdentical'] is True
legacy = [r for r in final['Results'] if r['Mode'] == 'dogleg']
assert len(legacy) == 8 and all(r['ExactLegacyMatch'] is True for r in legacy)
variants = defaultdict(list)
for row in final['Results']:
    variants[row['Mode']].append(row)
ablation = {mode: {
    'Runs': len(rows), 'FinalAccepted': sum(r['Final']['MeetsTargets'] for r in rows),
    'RetainedAccepted': sum(r['BestAccepted']['MeetsTargets'] for r in rows),
    'SolveEvaluations': sum(r['EvaluationCount'] for r in rows),
    'InvalidEvaluations': sum(r['InvalidEvaluations'] for r in rows),
} for mode, rows in variants.items()}

history = read(out / 'history-upgrade.json')
assert history['SourceVersion'] == '9' and history['CurrentVersion'] == '10'
assert history['ChargedEvaluations'] == 120 and history['SourceUnchanged']
assert history['ExportReloadIdentical'] and history['EngineSha256'] == final['EngineSha256']
assert sha(raw / 'history/v9.json') == history['SourceSha256']
assert sha(raw / 'history/v10.json') == history['CheckpointSha256']

tests = {}
for path in sorted(raw.glob('*.trx')):
    element = ET.parse(path).getroot().find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
    counters = element.attrib
    assert counters['failed'] == '0' and counters['notExecuted'] == '0'
    assert counters['total'] == counters['passed']
    tests[path.name] = {'Sha256': sha(path), 'Counters': counters}
assert tests['r2-full.trx']['Counters']['passed'] == '194'

profiles = {}
for name, expected in [('original', 60), ('holdout', 15)]:
    doc = read(out / f'{name}.json')
    original = read(raw / name / 'summary.json')
    assert doc['CompletedRuns'] == doc['ExpectedRuns'] == expected
    assert len(doc['Results']) == len({(r['Specification'], r['Seed']) for r in doc['Results']}) == expected
    assert doc['ArchiveProvenance']['RawSummarySha256'] == sha(raw / name / 'summary.json')
    assert doc['InputSha256'] == read(out.parent / f'dsearch-v8-20260926/{name}.json')['InputSha256']
    for binary, digest in doc['BinarySha256'].items():
        assert freeze['BinarySha256'][binary + '.dll'] == digest
    for archived_row, original_row in zip(doc['Results'], original['Results']):
        comparable = dict(original_row)
        comparable['SelectedValidation'] = dict(comparable['SelectedValidation'])
        comparable['SelectedValidation'].pop('Residuals', None)
        assert archived_row == comparable
    assert all(r.get('ReloadSnapshotIdentical', r.get('ReloadIdentical')) is True and not r['VerificationErrors'] and r['FailedTrials'] == 0 for r in doc['Results'])
    for row in doc['Results']:
        checkpoint = read(raw / name / Path(row['Specification']).stem / str(row['Seed']) / 'checkpoint.json')['checkpoint']
        assert checkpoint['chargedEvaluations'] <= checkpoint['specification']['budget']['maximumEvaluations'] == 10000
    profiles[name] = {'Completed': expected, 'SearchAcceptanceGatePassed': doc['SearchAcceptanceGatePassed'], **doc['Aggregate']}

native = read(out / 'native-smoke.json')
assert sha(root / native['ExportPath']) == native['ExportSha256']
assert native['BinarySha256']['OptilandWorkbench.InitialStructure.Engine.dll'] == final['EngineSha256']
app = root / Path(native['Executable']).parent
for name, expected in native['BinarySha256'].items():
    assert sha(app / name) == expected

(out / 'source-freeze.json').write_bytes((raw / 'source-freeze.json').read_bytes())
evidence = [p for p in out.rglob('*') if p.is_file()
            and not {'bin', 'obj'}.intersection(p.relative_to(out).parts)
            and p.name not in {'verification.json', 'README.md'}]
record = {
    'Algorithm': freeze['Algorithm'], 'VerifiedUtc': datetime.now(timezone.utc).isoformat(),
    'Scope': 'Mechanism, complete frozen search protocols, engineering regression and one native macOS smoke; no new external optical certification.',
    'RuntimeSourcesMatchFreeze': len(freeze['SourceSha256']),
    'FrozenAndDefaultReleaseBinariesMatch': True, 'CoreSourcesUnchangedAgainstV8': len(core_paths),
    'Tests': tests, 'FormalTestsCarriedForward': 1304, 'ComparisonToolTestsCarriedForward': 104,
    'BuildAndFormat': 'Default Debug/Release laboratory builds: zero warnings/errors; dotnet format verification passed, observed in this task. No alternate output used.',
    'Ablation': ablation, 'All32RetainedRecalculationsIdentical': True, 'All8LegacyResultsMatchR1': True,
    'HistoricalV9ContinuationVerified': True, 'NativeSmoke': 'native-smoke.json', 'Profiles': profiles,
    'TimingScope': 'Full regression and one native smoke overlapped part of the serial benchmark. Wall times are recorded, not a speed comparison.',
    'P5ReleaseAccepted': False,
    'EvidenceSha256': {str(p.relative_to(out)): sha(p) for p in sorted(evidence)},
}
(out / 'verification.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n')
print(json.dumps({'RuntimeSources': record['RuntimeSourcesMatchFreeze'], 'Tests': 194, 'Ablation': ablation,
                  'Profiles': profiles}, ensure_ascii=False, indent=2))
