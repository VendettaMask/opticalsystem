"""Seal saved default-output verification only; performs no optical computation."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile
import xml.etree.ElementTree as ET

repo = Path(sys.argv[1]).resolve()
root = repo / 'validation/rechecks/2026-11-01/spc-default-output'
final = root / 'final-verified'

def read(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))

def sha(p):
    h = hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda: f.read(1024 * 1024), b''):
            h.update(b)
    return h.hexdigest()

def write(p, data):
    with p.open('x', encoding='utf8') as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

def seconds(t):
    h, m, s = t.split(':')
    return int(h) * 3600 + int(m) * 60 + float(s)

def strip_elapsed(value):
    if isinstance(value, dict):
        return {k: strip_elapsed(v) for k, v in value.items() if k != 'elapsed'}
    if isinstance(value, list):
        return [strip_elapsed(v) for v in value]
    return value

assert not (root / 'final-verification.json').exists(), 'Never overwrite a sealed scope.'
summary = read(final / 'verification-summary.json')
assert summary['defaultFullBuildPassed'] and summary['unchangedBeforeAfter']
assert summary['readOnlyLeaseHeldDuringVerification'] and summary['defaultEqualsExecutedTestCopies']
assert not summary['foregroundApplicationStarted'] and not summary['nativeUserProcessesClosed']
build = (final / 'default-app-build.log').read_text(encoding='utf-8-sig')
assert '0 个警告' in build and '0 个错误' in build
before = {r['name']: r['sha256'] for r in read(final / 'default-runtime-before.json')}
after = {r['name']: r['sha256'] for r in read(final / 'default-runtime-after.json')}
assert len(before) == 8 and before == after
audits = list((final / 'runtime-audits').glob('*.json'))
assert len(audits) == 47, len(audits)
driver_hashes = set()
for p in audits:
    audit = read(p)
    assert not audit['ForegroundWindowStarted']
    assert {r['Name']: r['Sha256'] for r in audit['ProductAssemblies']} == before
    for r in audit['ProductAssemblies']:
        assert Path(r['Path']).resolve() == (repo / 'src/OpticalSystemDesign.App/bin/Release/net10.0' / r['Name']).resolve()
    driver_hashes.add(audit['ScenarioDriverSha256'])
assert len(driver_hashes) == 1

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tests = ET.parse(final / 'tests/spc-default-final.trx').findall('.//t:UnitTestResult', ns)
assert len(tests) == 109 and all(t.get('outcome') == 'Passed' for t in tests)
archives = {}
for kind in ('runtime', 'source'):
    rows = read(final / f'default-{kind}-sha256.json')
    with zipfile.ZipFile(final / f'default-{kind}.zip') as z:
        assert len(z.namelist()) == len(rows)
        for r in rows:
            assert hashlib.sha256(z.read(r['path'])).hexdigest() == r['sha256'], r['path']
    archives[kind] = len(rows)
runtime = {r['path']: r['sha256'] for r in read(final / 'default-runtime-sha256.json')}
assert {k.removeprefix('app/'): v for k, v in runtime.items() if k.startswith('app/') and k.endswith('.dll')} == before
pdb = read(final / 'default-pdb-sources.json')
assert all(r['matched'] for r in pdb)
with zipfile.ZipFile(final / 'default-source.zip') as z:
    for r in pdb:
        assert hashlib.sha256(z.read(r['path'])).hexdigest() == r['sha256'], r['path']

comparison = read(final / 'formal/comparison.json')
assert comparison['status'] == 'verified' and comparison['freshProcessResume'] and len(comparison['rows']) == 5
cost = {'functions': 0, 'gradients': 0, 'rays': 0, 'activitySeconds': 0, 'persistenceSeconds': 0}
external_checks = 0
for seed in range(42, 47):
    states = {}
    for arm in ('continuous', 'resumed'):
        folder = final / f'formal/{arm}/seed{seed}'
        state = read(folder / 'run.preparation.json')['checkpoint']
        states[arm] = state
        w = state['consumed']
        assert state['status'] == 'completed' and state['stage'] == 'Finished' and state['lostReservations'] == 0
        assert (w['functionEvaluations'], w['gradientEvaluations'], w['tracedRays'], w['differenceEvaluations'], w['validationEvaluations']) == (161, 151, 503394, 56, 10)
        assert seconds(w['elapsed']) <= 120 and len(state['spc']['pool']) == 8 and len(state['result']['seeds']) == 2
        assert state['options']['maximumFunctionEvaluations'] == 600 and state['options']['spc']['maximumGradientEvaluations'] == 600
        assert state['options']['maximumTracedRays'] == 2_000_000 and seconds(state['options']['timeLimit']) == 120
        segments = [read(p) for p in (folder / 'segments').glob('from-*.json')]
        assert len(segments) == (1 if arm == 'continuous' else 7)
        assert sum(r['actualNewRays'] for r in segments) == 503394
        for r in segments:
            assert r['consumed']['tracedRays'] - r['previous']['tracedRays'] == r['actualNewRays']
        audit = read(folder / 'independent-audit.json')
        assert not audit['searchBudgetInput'] and len(audit['candidates']) == 8 and len(audit['nativeReadbacks']) == 2
        for r in audit['candidates']:
            assert r['assessment']['completed'] and r['assessment']['meetsTargets']
        for r in audit['nativeReadbacks']:
            assert r['readback']['completed'] and r['readback']['meetsTargets']
        external_checks += 10
        cost['functions'] += w['functionEvaluations']
        cost['gradients'] += w['gradientEvaluations']
        cost['rays'] += w['tracedRays']
        cost['activitySeconds'] += seconds(w['elapsed'])
        cost['persistenceSeconds'] += seconds(state['persistenceElapsed'])
    a, b = states['continuous'], states['resumed']
    for key in ('plan', 'randomState', 'spc', 'consumed'):
        assert strip_elapsed(a[key]) == strip_elapsed(b[key]), (seed, key)
    assert a['result']['seeds'] == b['result']['seeds']
assert external_checks == 100
assert len({tuple(r['endpoints']) for r in comparison['rows']}) == 1
reopen = read(final / 'new-process-reopen.json')
assert reopen['studies'] == 10 and reopen['rays'] == 0
guards = read(final / 'original-tasks/summary.json')
assert len(guards['guards']) == 15 and guards['originalF8ExactRechecks'] == 5
for r in guards['guards']:
    assert r['consumed']['functionEvaluations'] == r['consumed']['tracedRays'] == 0
for seed in range(42, 47):
    assert read(final / f'original-tasks/F8-old-qualified-{seed}.json')['exact']
product = read(final / 'product-session/completed.json')
repeat = read(final / 'product-session/reopened.json')
assert product['mainStatus'] == 'completed' and product['exactlyOneHandoff'] and product['noGateChanges'] and product['selected'] == 2
assert product['loadRays'] == product['reportRays'] == 0
assert repeat['loadRays'] == repeat['repeatedHandoffRays'] == repeat['reportRays'] == 0
assert len(list((final / 'product-session').glob('*.autonomous.json'))) == 1
for r in product['audits']:
    assert r['independent']['meetsTargets'] and r['nativeReadback']['meetsTargets']
docs = read(root / 'documentation-audit.json')
assert not docs['BrokenLinks'] and not docs['MarkdownFenceIssues'] and not docs['GuideRepeatedParagraphs']

previous = {}
for folder in ('2026-11-01/initial-performance', '2026-10-31/preparation-checkpoint', '2026-11-01/initial-product-m36', '2026-11-01/spc-branches', '2026-11-01/spc-automatic-resume'):
    seal = repo / 'validation/rechecks' / folder / 'final-verification.json'
    data = read(seal)
    for r in data['artifacts']:
        assert sha(seal.parent / r['path']) == r['sha256'], folder + '/' + r['path']
    previous[folder] = {'sealSha256': sha(seal), 'verifiedArtifacts': len(data['artifacts']), 'originalStatus': data.get('status', 'not-recorded')}

paths = [repo / p for p in ('docs/INITIAL_STRUCTURE_SPC_DEFAULT_OUTPUT_2026-11-01.md', 'docs/INITIAL_STRUCTURE_SPC_AUTOMATIC_RESUME_2026-11-01.md', 'docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md', 'docs/CURRENT_STATUS.md', 'docs/USER_GUIDE.md', 'docs/README.md', 'labs/InitialStructure/README.md')]
paths += [p for p in (repo / 'tools/diagnostics/SpcDefaultOutput20261101').rglob('*') if p.is_file() and not {'bin', 'obj', '__pycache__'} & set(p.parts)]
delivery = []
with zipfile.ZipFile(root / 'delivery-source.zip', 'x', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(paths):
        name = p.relative_to(repo).as_posix()
        data = p.read_bytes()
        z.writestr(name, data)
        delivery.append({'path': name, 'sha256': hashlib.sha256(data).hexdigest()})
write(root / 'delivery-source-sha256.json', delivery)
artifacts = [{'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted(root.rglob('*')) if p.is_file() and p.name != 'final-verification.json']
write(root / 'final-verification.json', {
    'status': 'complete', 'date': '2026-11-01', 'authority': 'final-verified/',
    'scope': 'Full normal default Release App build and actual-default-runtime SPC background revalidation; original gates unchanged.',
    'defaultOutput': summary, 'build': {'warnings': 0, 'errors': 0, 'builtInModules': 2},
    'tests': {'passed': 109, 'failed': 0, 'skipped': 0},
    'runtimeAudits': len(audits), 'actualDefaultAssemblies': before, 'scenarioDriverSha256': next(iter(driver_hashes)),
    'archiveEntryCounts': archives, 'matchedPdbDocuments': len(pdb),
    'fiveSeedsSixFreshProcessPausePointsExact': True, 'formalCosts': cost,
    'independentCandidateAndNativeChecks': external_checks, 'originalZeroWorkGuards': 15, 'originalF8ExactRechecks': 5,
    'distinctSelectedDesigns': 2, 'independentFiveDesignSuccessClaim': False,
    'product': {'preparation': product['preparation'], 'main': product['main'], 'exactlyOneHandoff': True, 'repeatedWorkAndReportRays': 0, 'externalHandoffChecks': 4},
    'normalResumeReplayedRays': 0, 'gatesAperturesAndManufacturingRelaxed': False, 'f4ConflictRetained': True,
    'supersededAttempts': ['Initial root batch passed on earlier default assemblies; all eight were subsequently replaced by a concurrent build. It is not current runtime acceptance.', 'final/ retained the failed concurrent file-lock build. final-verified/ is the completed authority.'],
    'historicalEvidenceIntegrity': previous,
    'documentation': {'documents': docs['Documents'], 'brokenLinks': 0, 'fences': 0, 'guideDuplicates': 0},
    'nativeGui': False, 'wholeRepositoryTests': False, 'gpuCertification': False, 'crossPlatformCertification': False,
    'deepSeekUsed': False, 'commitPushInstallPublish': False, 'remainingRequiredChecksForThisScope': [],
    'limitations': ['One-level air-separated SPC; autonomous cemented/recursive scheduling and automatic aperture remain separate open work.', 'Five seeds repeat two selected prescriptions, and added-lens RMS improvement is not a reduction claim.', 'Windows/.NET numeric identity is retained; safe normal-stop resume does not claim exact forced-crash recovery.', 'Default product binary/source archives omit unchanged external runtime dependencies and are not standalone installers.', 'No foreground application startup or native interaction certification.'],
    'artifacts': artifacts})
for r in artifacts:
    assert sha(root / r['path']) == r['sha256']
print(f'Sealed and read back {len(artifacts)} artifacts; normal default build, 109 tests and five-seed six-point actual-default continuation passed.')
