"""Package completed scalar verification and preserve nonpassing native controls."""
from collections import Counter
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]).resolve()
run = root / 'artifacts/validation/huygens-extensions-20261011'
saved = root / 'artifacts/validation/huygens-spherical-repair-20261011'
fixture = root / 'validation/zemax/2026-r1/huygens-extensions-2026-10-11'
prior = json.loads((root / 'docs/HUYGENS_OFFAXIS_REPAIR_2026-10-11.json').read_text(encoding='utf-8-sig'))

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def relative(path):
    return path.relative_to(root).as_posix()

def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def test_run(name):
    path = run / 'tests' / name
    tree = ET.parse(path).getroot()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    summary = tree.find('t:ResultSummary', ns)
    counts = {key: int(value) for key, value in summary.find('t:Counters', ns).attrib.items()}
    assert summary.attrib['outcome'] == 'Completed', (name, summary.attrib)
    assert counts['total'] > 0 and counts['total'] == counts['passed'] == counts['executed'], (name, counts)
    assert counts['failed'] == counts['notExecuted'] == counts['error'] == counts['aborted'] == 0
    identities = Counter(row.attrib['testName'] for row in tree.findall('t:Results/t:UnitTestResult', ns))
    assert sum(identities.values()) == counts['total']
    packed = saved / (name + '.gz')
    packed.write_bytes(gzip.compress(path.read_bytes(), mtime=0))
    assert gzip.decompress(packed.read_bytes()) == path.read_bytes()
    return dict(counts=counts, identities=dict(identities), originalPath=relative(path),
                originalSha256=digest(path), savedPath=relative(packed), savedSha256=digest(packed))

# All four TRX must exist and be completed; a connection-timeout run is never substituted.
for name in ['product-release-complete.trx', 'comparison-release-complete.trx',
             'product-debug-related-complete.trx', 'spherical-targeted-local.trx']:
    assert (run / 'tests' / name).exists(), 'Incomplete verification: ' + name
saved.mkdir(parents=True, exist_ok=True)
tests = dict(productRelease=test_run('product-release-complete.trx'),
             comparisonRelease=test_run('comparison-release-complete.trx'),
             productDebugRelated=test_run('product-debug-related-complete.trx'),
             targetedRelease=test_run('spherical-targeted-local.trx'))
baseline = {}
for key, added_count in [('productRelease', 12), ('comparisonRelease', 0), ('productDebugRelated', 263)]:
    before = Counter(prior['tests'][key]['identities'])
    after = Counter(tests[key]['identities'])
    assert not before - after, (key, before - after)
    added = after - before
    assert sum(added.values()) == added_count, (key, added)
    baseline[key] = dict(preserved=sum(before.values()), removed={}, added=dict(added))
assert tests['productRelease']['counts']['total'] == 4667
assert tests['comparisonRelease']['counts']['total'] == 182
assert tests['productDebugRelated']['counts']['total'] == 649
new_product_cases = set(baseline['productRelease']['added'])
debug_added = baseline['productDebugRelated']['added']
assert sum(v for k,v in debug_added.items() if k in new_product_cases) == 12
assert sum(v for k,v in debug_added.items() if k not in new_product_cases) == 251
baseline['productDebugRelated']['selectionExpansion'] = dict(newProductCases=12, newlySelectedExistingCases=251,
    semantics='The earlier 386 identities are preserved; complete related classes additionally include 251 existing cases.')

after = read(run / 'verified/comparison.json')
accepted = [row for row in after if row['method'] == 'Auto' and not row['pol']]
assert len(accepted) == 10
assert all(row['metric']['Maximum'] < 1e-5 and row['metric']['RelativeL2'] < 5e-5 for row in accepted)
polarization = [row for row in after if row['method'] == 'Auto' and row['pol']]
assert len(polarization) == 2 and all(row['metric']['RelativeL2'] > .01 for row in polarization)
formal = read(run / 'formal-original/formal.json')
assert len(formal) == 15 and all(row['metric']['conclusion'] == 'Pass' for row in formal)
assert formal == prior['formalMetrics'], 'Original N01/N02/MTF metrics changed.'
before_provenance = read(run / 'before-fast/provenance.json')
base_commit = 'f8dbcdefdccb9f531315da8f7aeb993d33b39239'
base_core_path = 'src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs'
base_core_bytes = subprocess.check_output(['git', 'show', base_commit + ':' + base_core_path], cwd=root)
base_core_sha = hashlib.sha256(base_core_bytes).hexdigest()
assert base_core_sha == next(row['sha256'] for row in prior['sourceHashes'] if row['path'] == base_core_path)

for directory, name, destination in [
    ('before-fast', 'comparison.json', 'comparison-before.json'),
    ('before-fast', 'provenance.json', 'provenance-before.json'),
    ('verified', 'comparison.json', 'comparison-after.json'),
    ('verified', 'hypotheses.json', 'hypotheses.json'),
    ('verified', 'provenance.json', 'provenance-after.json'),
    ('formal-original', 'formal.json', 'formal-original.json'),
    ('formal-original', 'provenance.json', 'formal-original-provenance.json'),
    ('formal-original', 'huygens-mtf-vs-field-workbench.json', 'original-mtf-workbench.json')]:
    shutil.copyfile(run / directory / name, saved / destination)
for path in (run / 'verified').glob('*-workbench.json'):
    shutil.copyfile(path, saved / path.name)
for name in ['build-debug.log', 'build-release.log', 'debug-filter.txt', 'frozen-head.json']:
    shutil.copyfile(run / name, saved / name)
frozen = read(run / 'frozen-head.json')
assert frozen['matched'] == frozen['count'] == frozen['workingTree']['matched'] == 2668
assert frozen['unchanged'] and frozen['workingTree']['unchanged']

complex_grids = []
for path in sorted(fixture.glob('spherical-field*-*/**/data.json')):
    if '-linear' in str(path):
        continue
    values = [v for grid in read(path)['dataGrids'] for row in grid['values'] for v in row]
    nulls = sum(v is None for v in values)
    complex_grids.append(dict(path=relative(path), sha256=digest(path), count=len(values), nullCells=nulls,
                              certified=False, reason='Paired wide-window real/imaginary grids have nonfinite cells; no whole complex-field pass.'))
assert complex_grids and any(row['nullCells'] for row in complex_grids)
write(saved / 'nonpassing-controls.json', dict(polarization=polarization, wideComplexGrids=complex_grids,
      semantics='Complete linear intensity comparison remains independent of unavailable complex cells. Experimental scalar polarization residuals remain open.'))
capture_files = [dict(path=p.relative_to(fixture).as_posix(), sha256=digest(p))
                 for p in sorted(fixture.rglob('*')) if p.is_file() and p.name != 'capture-file-hashes.json']
write(fixture / 'capture-file-hashes.json', capture_files)
environments = [read(p) for p in fixture.rglob('environment.json')]
assert len(environments) == 16 and all(e['major'] == 26 and e['minor'] == 1 and e['validLicense'] for e in environments)

binaries = {}
for configuration in ['Debug', 'Release']:
    paths = [root / f'{project}/bin/{configuration}/net10.0/OptilandWorkbench.Core.dll' for project in
             ['src/OptilandWorkbench.Core', 'src/OptilandWorkbench.App', 'tests/OptilandWorkbench.Tests',
              'tests/OptilandWorkbench.ZemaxComparison.Tests']]
    hashes = [digest(p) for p in paths]
    assert len(set(hashes)) == 1, (configuration, hashes)
    binaries[configuration] = [dict(path=relative(p), sha256=h) for p, h in zip(paths, hashes)]
for configuration in ['debug', 'release']:
    log = (run / f'build-{configuration}.log').read_text(encoding='utf-8-sig')
    assert '0 个警告' in log and '0 个错误' in log
sources = ['src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs',
           'tests/OptilandWorkbench.Tests/HuygensSphericalNativeTests.cs',
           'tools/diagnostics/HuygensExtensions20261011/Program.cs',
           'tools/diagnostics/HuygensExtensions20261011/CaptureControls.ps1']
report = dict(date='2026-10-11', generatedUtc=datetime.now(timezone.utc).isoformat(),
              baseCommit=base_commit, status='VerifiedScalarSubset',
              referenceAndTolerancesChanged=False, acceptedScalarCases=accepted,
              beforeCases=read(run / 'before-fast/comparison.json'), allMethodDiagnostics=after,
              beforeBuild=dict(actualCoreSha256=before_provenance['coreSha256'],
                  precedingSavedCoreSha256=prior['binaries']['Release'][0]['sha256'],
                  sourceCommit=base_commit, diffractionSourceSha256=base_core_sha,
                  diffractionSourceMatchesPrecedingReport=True,
                  semantics='Before controls used a fresh build of the unchanged preceding source. Binary bytes differ from the earlier saved build and are not claimed identical.'),
              originalFormalMetrics=formal, tests=tests, baseline=baseline,
              nativeEnvironment=environments[0], nativeCaptureCount=len(environments),
              capturedFiles=capture_files, nonpassingPolarization=polarization, complexGrids=complex_grids,
              binaries=binaries, frozen=frozen,
              sourceHashes=[dict(path=p, sha256=digest(root / p)) for p in sources],
              sourceHashSemantics='Working bytes compiled locally; normalized Git source blob bytes may differ in line endings.',
              nativeHost=dict(path='artifacts/validation/huygens-offaxis-20261011/host-pupil/NativeEnergyControl.exe',
                              sha256=digest(root / 'artifacts/validation/huygens-offaxis-20261011/host-pupil/NativeEnergyControl.exe')),
              builds={c: dict(defaultOutput=True, warnings=0, errors=0) for c in ['Debug', 'Release']},
              failedRuns='Initial native IPC access denial, restricted testhost timeout, and superseded diagnostics remain local records; never counted as passes.',
              scope='MS-L7 focal scalar intensity only; independent Cooke and Tessar follow-up remains excluded.',
              remaining=['Experimental polarization residual and complete vector/absorbing-interface transport',
                         'Spherical polychromatic, complex phase, tilted/folded/complex pupil and multi-configuration certification',
                         'DEE apodization, polychromatic and multi-field expansion',
                         'Full Debug, laboratories, installer, manual desktop and full six-file matrix'])
write(root / 'docs/HUYGENS_SPHERICAL_REPAIR_2026-10-11.json', report)
write(saved / 'capture-and-report-hashes.json', [dict(path=relative(p), sha256=digest(p))
      for p in sorted(saved.rglob('*')) if p.is_file() and p.name != 'capture-and-report-hashes.json'])
print(json.dumps(dict(tests={k: v['counts']['total'] for k,v in tests.items()},
                      preserved={k:v['preserved'] for k,v in baseline.items()}, acceptedCases=len(accepted),
                      maximumScalarError=max(r['metric']['Maximum'] for r in accepted),
                      captureFiles=len(capture_files), nativeCases=len(environments)), ensure_ascii=False))
