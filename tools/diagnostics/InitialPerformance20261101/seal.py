"""Seal existing measurements and source identities; no optical computation."""
from pathlib import Path
import hashlib, json, sys, zipfile, xml.etree.ElementTree as ET

repo = Path(sys.argv[1]).resolve()
root = repo / 'validation/rechecks/2026-11-01/initial-performance'
def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def trx(name):
    tree = ET.parse(root / 'tests' / name)
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    rows = tree.findall('.//t:UnitTestResult', ns)
    return {value: sum(x.get('outcome') == value for x in rows) for value in ('Passed', 'Failed', 'NotExecuted')}

if (root / 'final-verification.json').exists():
    raise SystemExit('Never overwrite a sealed verification.')
comparison = read(root / 'comparison.json')
assert comparison['errors'] == [] and comparison['allOriginalQualified']
module = read(root / 'tests/module-combined.json')
assert module['passed'] == module['uniqueTests'] == 184 and module['failed'] == 0
assert trx('core-equivalence-v2.trx') == dict(Passed=70, Failed=0, NotExecuted=4)
assert trx('product-session.trx') == dict(Passed=1, Failed=0, NotExecuted=0)
audits = {}
for name in ('baseline', 'candidate', 'product'):
    audit = read(root / f'audit-{name}/audit.json')
    assert audit['errors'] == [] and audit['replayCount'] == 76 and len(audit['runs']) == 10
    audits[name] = dict(replayCount=76, runs=10, errors=[])
resume = read(root / 'resume/comparison.json')
assert resume['errors'] == [] and resume['exactPhysicalAndDiscreteState']
f4 = read(root / 'f4/segment.json')
assert f4['actualSegmentRays'] == 0 and f4['consumed']['functionEvaluations'] == 0
f4_checkpoint = read(root / 'f4/run.preparation.json')['checkpoint']
assert f4_checkpoint['stopReason'] == 'OriginalExternalStopApertureConflict'
product = read(root / 'product-session/summary.json')
assert product['preparationStoppedAndLoaded'] and product['loadAndRepeatedHandoffRays'] == 0
assert product['physicalAndDiscreteWorkSettled'] and product['checkedAgain']['meetsTargets']
documentation = read(root / 'documentation-audit-final.json')
assert not documentation['BrokenLinks'] and not documentation['MarkdownFenceIssues'] and not documentation['GuideRepeatedParagraphs']
build = (root / 'default-app-build.log').read_text(encoding='utf-8-sig')
assert '0 个警告' in build and '0 个错误' in build
runtime_checks = {}
for prefix in ('module', 'product'):
    before = read(root / f'tests/{prefix}-runtime-before.json')
    after = read(root / f'tests/{prefix}-runtime-after.json')
    a = {Path(x['Path']).name: x['Hash'] for x in before}
    b = {Path(x['Path']).name: x['Hash'] for x in after}
    assert a == b, prefix + ' runtime changed'
    if prefix == 'module':
        retry = {Path(x['Path']).name: x['Hash'] for x in read(root / 'tests/module-runtime-after-retry.json')}
        assert a == retry, 'Module runtime changed during fixture retry'
    runtime_checks[prefix] = dict(files=len(a), changed=False)

folders = ['src/OpticalSystemDesign.Core', 'labs/InitialStructure/src',
           'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests',
           'src/OpticalSystemDesign.App/Laboratories/InitialStructure']
folders += ['tools/diagnostics/' + name for name in ('InitialPerformance20261101',
            'InitialPerformanceAudit20261101', 'InitialPerformanceProbes20261101',
            'InitialPerformanceResume20261101', 'F8RealPipeline')]
files = {p for folder in folders for p in (repo / folder).rglob('*')
         if p.is_file() and not {'bin', 'obj', '__pycache__'} & set(p.relative_to(repo).parts)}
for name in ('AGENTS.md', 'global.json', 'Directory.Build.props', 'Directory.Packages.props',
             'tests/OpticalSystemDesign.Tests/DifferentiableGeometryWorkspaceTests.cs',
             'tests/OpticalSystemDesign.Tests/VectorTangentEquivalenceTests.cs',
             'docs/INITIAL_STRUCTURE_PERFORMANCE_2026-11-01.md',
             'docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md',
             'docs/CURRENT_STATUS.md', 'docs/README.md', 'labs/InitialStructure/README.md',
             'validation/rechecks/2026-11-01/initial-performance/README.md'):
    path = repo / name
    if path.is_file(): files.add(path)
manifest = []
with zipfile.ZipFile(root / 'final-source.zip', 'x', zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(files):
        data = path.read_bytes(); name = path.relative_to(repo).as_posix()
        archive.writestr(name, data)
        manifest.append(dict(path=name, sha256=hashlib.sha256(data).hexdigest()))
(root / 'final-source-sha256.json').write_text(json.dumps(manifest, indent=2), encoding='utf8')

candidate = read(root / 'candidate/source-sha256.json')
current = {x['path']: x['sha256'] for x in manifest}
drift = [dict(path=x['path'], candidate=x['sha256'], final=current[x['path']])
         for x in candidate if x['path'] in current and current[x['path']] != x['sha256']]
own_core_engine = ('DifferentiableGeometryWorkspace.cs', 'DifferentiableOpticalMetrics.cs',
                  'DifferentialScalar.cs', 'OpticalDifferentialContext.cs', 'SearchPerformanceMeasurement.cs',
                  'FixedTopologyObjective.cs', 'TopologyEvaluationPlan.cs',
                  'TopologyGateCompensation.cs', 'TopologyGateCompensation.Constrained.cs')
assert not [x for x in drift if Path(x['path']).name in own_core_engine], 'Performance source drift'

old = repo / 'validation/rechecks/2026-10-31/preparation-checkpoint/final-verification.json'
previous = read(old)
for artifact in previous['artifacts']:
    assert digest(old.parent / artifact['path']) == artifact['sha256'], 'Historical preparation evidence changed: ' + artifact['path']
evidence = [dict(path=p.relative_to(root).as_posix(), bytes=p.stat().st_size, sha256=digest(p))
            for p in sorted(root.rglob('*')) if p.is_file() and p.name != 'final-verification.json']
result = dict(date='2026-11-01', milestone='M3.4', complete=True,
    scope='Fixed original F8 performance; background/headless only; no full-repository or native-GUI certification.',
    gatesAndCapsChanged=False, originalColdQualified='5/5 in each arm', comparison='comparison.json',
    coreTests=trx('core-equivalence-v2.trx'), moduleTests=dict(uniquePassed=184, initialPassed=153, unchangedFixtureRetryPassed=31),
    productSessionTests=trx('product-session.trx'), independentOriginalAudits=audits,
    runtimeBeforeAfter=runtime_checks, resumeExact=True, f4ZeroWorkConflict=True,
    modelProbes='model-probes.json', defaultsForScalesDampingAndGlobalParallelUnchanged=True,
    observedTotalActiveSpeedup=comparison['totalActiveSpeedup'],
    sourceArmDifference='source-arm-delta.json', candidateToFinalSourceDrift=drift,
    previousPreparationSealSha256=digest(old), previousPreparationArtifactsVerified=len(previous['artifacts']), finalSourceFiles=len(manifest),
    documentationAudit=dict(path='documentation-audit-final.json', documents=documentation['Documents'], brokenLinks=0, fences=0, guideDuplicateGroups=0), defaultApplicationBuild='default-app-build.log',
    preservedFailures=['baseline/interrupted-disk-full', 'audit-candidate.log', 'tests/core-equivalence.trx', 'tests/module-regression.trx'],
    limitations=['One observation per seed/arm, timing is not a general causal speed guarantee.',
                 'Concurrent unrelated Core source changes are preserved and disclosed.',
                 'No F8 GPU performance study, no global parallel-search rollout.',
                 'Original kernel/numerical-environment guard still applies to exact continuation.'],
    artifacts=evidence)
(root / 'final-verification.json').write_text(json.dumps(result, indent=2), encoding='utf8')
print(f'Sealed {len(evidence)} artifacts and {len(manifest)} source files; fixed gates, exact state, all scoped checks passed.')
