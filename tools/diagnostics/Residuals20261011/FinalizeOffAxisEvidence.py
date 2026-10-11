"""Package completed verification and untouched native evidence; no optical engine."""
import collections
import datetime
import gzip
import hashlib
import json
import pathlib
import shutil
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1]).resolve()
run = root / 'artifacts/validation/huygens-offaxis-20261011'
saved = root / 'artifacts/validation/huygens-offaxis-repair-20261011'
fixture = root / 'validation/zemax/2026-r1/huygens-offaxis-2026-10-11'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def relative(path):
    return path.relative_to(root).as_posix()

def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def trx(name):
    path = run / 'tests' / name
    xml = ET.parse(path).getroot()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    summary = xml.find('t:ResultSummary', ns)
    counts = {key: int(value) for key, value in summary.find('t:Counters', ns).attrib.items()}
    assert summary.attrib['outcome'] == 'Completed', (name, summary.attrib, counts)
    assert counts['total'] > 0 and counts['passed'] == counts['total'] == counts['executed'], (name, counts)
    assert counts['failed'] == counts['notExecuted'] == counts['error'] == counts['aborted'] == 0
    identities = collections.Counter(row.attrib['testName'] for row in xml.findall('t:Results/t:UnitTestResult', ns))
    assert sum(identities.values()) == counts['total']
    packed = saved / (name + '.gz')
    packed.write_bytes(gzip.compress(path.read_bytes(), mtime=0))
    assert gzip.decompress(packed.read_bytes()) == path.read_bytes()
    return dict(path=relative(path), sha256=digest(path), counts=counts, identities=dict(identities),
                savedPath=relative(packed), savedSha256=digest(packed))

# Validate completion before creating any final report.
for name in ['product-release-verified.trx', 'comparison-release-complete.trx', 'product-debug-local-complete.trx']:
    if not (run / 'tests' / name).exists():
        raise RuntimeError('Final run is incomplete: ' + name)
saved.mkdir(parents=True, exist_ok=True)
tests = dict(productRelease=trx('product-release-verified.trx'), comparisonRelease=trx('comparison-release-complete.trx'),
             productDebugRelated=trx('product-debug-local-complete.trx'))
assert tests['productRelease']['counts']['total'] == 4655
assert tests['comparisonRelease']['counts']['total'] == 182
old = read(root / 'docs/HUYGENS_FFT_ENERGY_REPAIR_2026-10-11.json')
baseline = {}
for key, added_count in [('productRelease', 16), ('comparisonRelease', 1)]:
    before = collections.Counter(old['tests'][key]['identities'])
    after = collections.Counter(tests[key]['identities'])
    assert not before - after, (key, before - after)
    added = after - before
    assert sum(added.values()) == added_count, (key, added)
    baseline[key] = dict(preserved=sum(before.values()), removed={}, added=dict(added))
debug_names = tests['productDebugRelated']['identities']
assert sum(v for k, v in debug_names.items() if '.CapturedOffAxisScalarPsfMatchesNativeTangentGrid(' in k) == 6
assert sum(v for k, v in debug_names.items() if '.CapturedMfeControlsRequireTheSameGeometricCentroidWindow(' in k) == 10

formal = read(run / 'formal-verified/formal.json')
assert len(formal) == 15 and all(row['metric']['conclusion'] == 'Pass' for row in formal)
assert max(row['metric']['maxAbsolute'] for row in formal if row['key'] == 'Huygens MTF vs Field') < 1e-6
for name in ['formal.json', 'provenance.json', 'huygens-mtf-vs-field-workbench.json']:
    shutil.copyfile(run / 'formal-verified' / name, saved / name)
for directory, name in [('offaxis-audited-final', 'offaxis.json'), ('mtf-verified', 'mtf-offaxis.json')]:
    shutil.copyfile(run / directory / name, saved / name)
shutil.copyfile(run / 'formal-centroid-final/huygens-mtf-vs-field-workbench.json', saved / 'field-spline-before.json')
shutil.copyfile(run / 'formal-centroid-final/provenance.json', saved / 'field-spline-before-provenance.json')
for name in ['frozen-head.json', 'frozen-index.json']:
    data = read(run / name)
    assert data['unchanged'] and data['workingTree']['unchanged']
    assert data['matched'] == data['workingTree']['matched'] == data['count'] == 2668
    shutil.copyfile(run / name, saved / name)

excluded = []
for name in ['field3-gaussian-linear', 'field3-gaussian-real', 'field3-gaussian-imaginary']:
    source = run / 'native' / name
    destination = saved / 'excluded-native' / name
    shutil.copytree(source, destination, dirs_exist_ok=True)
    data = read(source / 'Planar/data.json')
    nulls = sum(v is None for grid in data['dataGrids'] for row in grid['values'] for v in row)
    excluded.append(dict(case=name, reason='Wide Gaussian control is not certified; complex grids contain nonfinite cells.', nullCells=nulls,
                         dataSha256=digest(source / 'Planar/data.json'), savedPath=relative(destination)))
source = run / 'mtf-native/scan-minus'
destination = saved / 'excluded-native/scan-minus'
shutil.copytree(source, destination, dirs_exist_ok=True)
for method in ['Auto', 'Planar', 'Spherical']:
    data = read(source / method / 'data.json')
    assert not data['dataSeries']
excluded.append(dict(case='scan-minus', reason='Native API returned empty data series; never counted as a pass.', savedPath=relative(destination)))
write(saved / 'excluded-native.json', excluded)

files = [dict(path=path.relative_to(fixture).as_posix(), sha256=digest(path)) for path in sorted(fixture.rglob('*'))
         if path.is_file() and path.name != 'capture-file-hashes.json']
write(fixture / 'capture-file-hashes.json', files)
environments = [read(path) for path in fixture.rglob('environment.json')]
assert environments and all(e['major'] == 26 and e['minor'] == 1 and e['validLicense'] for e in environments)
sources = ['src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs', 'src/OptilandWorkbench.Core/Analysis/MtfScanAnalysis.cs',
           'src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs',
           'tools/OptilandWorkbench.ZemaxComparison/ExtendedAnalysisContracts.Diffraction.cs',
           'tools/diagnostics/N02Layers20261009/NativeEnergyControl.cs']
binaries = {}
for config in ['Debug', 'Release']:
    paths = [root / f'src/OptilandWorkbench.Core/bin/{config}/net10.0/OptilandWorkbench.Core.dll',
             root / f'src/OptilandWorkbench.App/bin/{config}/net10.0/OptilandWorkbench.Core.dll',
             root / f'tests/OptilandWorkbench.Tests/bin/{config}/net10.0/OptilandWorkbench.Core.dll',
             root / f'tests/OptilandWorkbench.ZemaxComparison.Tests/bin/{config}/net10.0/OptilandWorkbench.Core.dll']
    hashes = [digest(path) for path in paths]
    assert len(set(hashes)) == 1, (config, hashes)
    binaries[config] = [dict(path=relative(path), sha256=sha) for path, sha in zip(paths, hashes)]
hosts = [run / 'host/NativeEnergyControl.exe', run / 'host-mfe/NativeEnergyControl.exe',
         run / 'host-pupil/NativeEnergyControl.exe',
         root / 'artifacts/validation/residuals-20261011/followup/host/NativeEnergyControl.exe']
report = dict(date='2026-10-11', generatedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
              baseCommit='7a5279313e2308f0005fd05bbe1cfd77ef19b617', status='Verified', referenceAndTolerancesChanged=False,
              nativeEnvironment=environments[0], nativeCaptureCount=len(environments), formalMetrics=formal, tests=tests,
              baseline=baseline, binaries=binaries, nativeHosts=[dict(path=relative(p), sha256=digest(p)) for p in hosts],
              sourceHashes=[dict(path=p, sha256=digest(root/p)) for p in sources],
              sourceHashSemantics='Working bytes compiled locally; source hashes are not claims about normalized Git blobs.',
              frozen=dict(count=2668, aggregateSha256='3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10',
                          head=read(run/'frozen-head.json'), index=read(run/'frozen-index.json')),
              capturedFiles=files, excluded=excluded,
              builds={c: dict(defaultOutput=True, warnings=0, errors=0) for c in ['Debug','Release']},
              supersededRuns='Earlier failed, timed-out and explicitly stopped TRX files remain diagnostic records; none count as completed verification.',
              remaining=['Spherical, polarized, polychromatic off-axis and complex/tilted pupil certification',
                         'DEE apodization, polychromatic and multi-field expansion',
                         'Full Debug, laboratories, installer, manual desktop and full six-file numerical matrix'],
              scope='Independent Cooke and Tessar follow-up is excluded by the user; existing captures and regressions remain preserved.')
write(root / 'docs/HUYGENS_OFFAXIS_REPAIR_2026-10-11.json', report)
write(saved / 'capture-and-report-hashes.json', [dict(path=relative(p), sha256=digest(p))
      for p in sorted(saved.rglob('*')) if p.is_file() and p.name != 'capture-and-report-hashes.json'])
print(json.dumps(dict(product=tests['productRelease']['counts'], comparison=tests['comparisonRelease']['counts'],
                      debug=tests['productDebugRelated']['counts'], capturedFiles=len(files), baseline=baseline), ensure_ascii=False)[:3500])
