"""Verify the final MTF tolerancing implementation, without changing prior evidence."""
import hashlib, json, pathlib, re, struct, subprocess, xml.etree.ElementTree as ET, zlib
from datetime import datetime, timezone
from urllib.parse import unquote
ROOT = pathlib.Path(__file__).resolve().parents[3]
HERE = pathlib.Path(__file__).resolve().parent
PRIOR = ROOT / 'artifacts/validation/grin-material-editor-20261004'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def meta(path):
    p = pathlib.Path(path)
    if not p.is_absolute(): p = ROOT / p
    data = p.read_bytes()
    return {'path': str(p.relative_to(ROOT)), 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)}
def read(path):
    tree = ET.parse(path)
    names = {item.get('testName') for item in tree.findall('t:Results/t:UnitTestResult', NS)}
    counters = {key: int(value) for key, value in tree.find('t:ResultSummary/t:Counters', NS).attrib.items()}
    return tree, names, counters
sources = (HERE / 'source-files.txt').read_text().splitlines()
reports = {}; final_names = []
for config in ('debug', 'release'):
    tree, names, counters = read(HERE / (config + '.trx'))
    assert counters['total'] == counters['passed'] == counters['executed'] == len(names) == 3500, (config, counters)
    assert counters['failed'] == counters['error'] == counters['notExecuted'] == 0
    assert all(item.get('outcome') == 'Passed' for item in tree.findall('t:Results/t:UnitTestResult', NS))
    _, old_names, _ = read(PRIOR / (config + '.trx'))
    assert len(old_names) == 3426 and old_names <= names
    added = names - old_names
    assert len(added) == 74
    assert sum('.MtfTolerancingTests.' in name for name in added) == 33
    assert sum('.MtfTolerancingPanelTests.' in name for name in added) == 5
    assert sum('.TolerancingWorkflowTests.' in name for name in added) == 23
    assert sum('.TolerancingPanelRevisionTests.' in name for name in added) == 2
    assert sum('.PrimaryActionPresentationTests.' in name for name in added) == 11
    stamp = tree.find('t:Times', NS).get('start')
    stamp = re.sub(r'(\.\d{6})\d+', r'\1', stamp)
    start = datetime.fromisoformat(stamp).timestamp()
    assert all((ROOT / path).stat().st_mtime <= start for path in sources), (config, 'source changed after test start')
    build = (HERE / (config + '-build.log')).read_text()
    assert '0 个警告' in build and '0 个错误' in build
    assert not re.search(r'\b(?:warning|error) (?:CS|MSB|NU|AVLN)\d+', build, re.I)
    reports[config] = {'counters': counters, 'previousTestsPreserved': 3426, 'newFeatureTests': 38,
        'adjacentTestsAddedToCumulativeFilter': 36, 'trx': meta(HERE / (config + '.trx')),
        'log': meta(HERE / (config + '.log')), 'build': meta(HERE / (config + '-build.log'))}
    final_names.append(names)
assert final_names[0] == final_names[1]
_, render_names, render_counts = read(HERE / 'screenshots.trx')
assert render_counts['passed'] == render_counts['total'] == 3 and render_counts['failed'] == 0
assert render_names <= final_names[0]
pngs = sorted((HERE / 'screenshots').glob('*.png')); assert len(pngs) == 9
for p in pngs:
    data = p.read_bytes(); assert data[:8] == b'\x89PNG\r\n\x1a\n' and len(data) > 10000
    offset = 8; encoded = b''
    while offset < len(data):
        size = struct.unpack('>I', data[offset:offset+4])[0]
        if data[offset+4:offset+8] == b'IDAT': encoded += data[offset+8:offset+8+size]
        offset += size + 12
    assert len(set(zlib.decompress(encoded))) > 100
previous = json.loads((PRIOR / 'verification.json').read_text())
baseline = []
for row in previous['baselineIntegrity']:
    current = meta(row['path'])
    original = subprocess.check_output(['git', 'show', 'HEAD:' + row['path']], cwd=ROOT)
    assert current['sha256'] == row['sha256'] == hashlib.sha256(original).hexdigest()
    baseline.append({**current, 'byteEqualHead': True})
assert len(baseline) == 29
registry = (ROOT / 'src/OptilandWorkbench.Core/Optimization/ZemaxOperandRegistry.cs').read_text()
registered = set(re.search(r'RequiredSequentialCodes = """(.*?)"""', registry, re.S)[1].split())
executable = set(re.findall(r'"([A-Z0-9]{4})"', registry.split('ExecutableCodes =', 1)[1].split('StringComparer.Ordinal);', 1)[0]))
assert len(registered) == 383 and len(executable) == 341 and 'TOLR' not in executable
format_log = (HERE / 'format-final.log').read_text(); assert not format_log.strip()
assert subprocess.run(['git', 'diff', '--check'], cwd=ROOT, capture_output=True).returncode == 0
docs = (HERE / 'documentation-files.txt').read_text().splitlines(); links = 0
for name in docs:
    p = ROOT / name; content = p.read_text()
    assert '累计验证结果正在记录' not in content
    if name in (PRIOR / 'documentation-files.txt').read_text().splitlines():
        assert '3500/3500' in content.split('\n\n', 2)[1], name
    content = re.sub(r'```[\s\S]*?```|`[^`\n]*`', '', content)
    for match in re.finditer(r'\]\(([^)]+)\)', content):
        target = match[1].split('#', 1)[0]
        if not target or '://' in target or target.startswith('mailto:'): continue
        destination = (p.parent / unquote(target)).resolve()
        assert destination.exists() or destination == HERE / 'verification.json', (name, target)
        links += 1
binary_paths = [ROOT / f'src/OptilandWorkbench.{project}/bin/{config}/net10.0/OptilandWorkbench.{project}.dll'
    for config in ('Debug', 'Release') for project in ('Core', 'Application', 'App')]
initial = {config: {'trx': meta(HERE / (config + '-initial.trx')), 'note': 'Passed 3500 before the final probability metadata and study-setting stale-result fixes.'} for config in ('debug', 'release')}
result = {'recordedAt': datetime.now(timezone.utc).isoformat(), 'scope': 'Formal single-frequency FFT/geometric MTF tolerance; separate-field inverse/acceptance, joint yield, bounded spacing/decenter/tilt compensation; strict v3 persistence, stale settings protection, typed plots and actual desktop rendering.',
    'operandStatus': previous['operandStatus'], 'verification': reports, 'initialPassingRuns': initial,
    'sourceFiles': [meta(path) for path in sources], 'changedSourceFiles': meta(HERE / 'changed-source-files.txt'),
    'testFilter': meta(HERE / 'test-filter.txt'), 'baselineIntegrity': baseline,
    'numericEvidence': {'formalMtfParity': 'All four directions and both methods, same frequency/sampling/wavelength/field; formal FFT compensation comparison; above-cutoff zero versus missing data.', 'newNativeZemaxToleranceCapture': False},
    'visualEvidence': {'tests': render_counts, 'images': [meta(path) for path in pngs], 'review': 'All 9 Light/Dark/narrow real Avalonia/Skia screenshots visually inspected; screenshot-only UI review.'},
    'defaultDesktopBinaries': [meta(path) for path in binary_paths], 'format': meta(HERE / 'format-final.log'),
    'documentation': [meta(path) for path in docs], 'checkedDocumentationLinks': links,
    'diagnostics': [meta(path) for path in sorted((HERE / 'diagnostics').iterdir())],
    'limitations': ['No native Zemax tolerance/compensator/yield numerical comparison; no .TOL import, cross-configuration joint tolerancing, automatic symmetric fields, Huygens MTF criterion, paraxial focus, group rigid-body compensators, CPAR/CEDV/CMCO or polynomial/cache feature.']}
(HERE / 'verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
print(json.dumps({'Debug': 3500, 'Release': 3500, 'featureTests': 38, 'adjacentTests': 36, 'screenshots': 9, 'baselineFilesUnchanged': 29, 'links': links}))
