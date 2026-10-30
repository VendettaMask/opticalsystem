// Evidence assembly only; no runtime design logic or regenerated reference data.
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { join, relative, dirname } from 'node:path';
import { createHash } from 'node:crypto';
const root = process.cwd(), evidence = process.argv[2];
const read = p => readFileSync(p, 'utf8').replace(/^\uFEFF/, '');
const json = p => JSON.parse(read(p));
const sha = p => createHash('sha256').update(readFileSync(p)).digest('hex').toUpperCase();
const save = (p, o) => writeFileSync(join(evidence, p), JSON.stringify(o, null, 2) + '\n');
function files(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(d => d.isDirectory() ? files(join(directory, d.name)) : [join(directory, d.name)]);
}
const summaries = files(join(evidence, 'final-runtime')).filter(p => p.endsWith('summary.json'));
const rows = summaries.map(p => {
  const s = json(p), r = s.Result;
  return { name: s.Name, path: relative(evidence, p), protocol: s.SourceInput.Settings.DesignProtocolVersion,
    targets: s.SourceInput.Target, settings: s.SourceInput.Settings, metrics: r.Metrics, passed: r.Passed,
    failed: r.Checks.filter(c => !c.Passed), samplingConverged: r.SamplingConverged, seconds: s.Seconds,
    generationWork: s.GenerationRun?.GeneralWork, finalWork: r.Run.GeneralWork };
});
save('paired-results.json', rows);
const pairs = [...new Set(rows.map(r => r.name))].map(name => {
  const inputs = summaries.map(p => json(p)).filter(s => s.Name === name).map(s => {
    const input = structuredClone(s.SourceInput); delete input.Settings.DesignProtocolVersion; return input;
  });
  return { name, runs: inputs.length, identicalInputsExceptProtocol: inputs.length === 2 && JSON.stringify(inputs[0]) === JSON.stringify(inputs[1]) };
});
save('paired-input-integrity.json', pairs);
const cases = summaries.flatMap(p => json(join(p.slice(0, -'summary.json'.length), 'core-cases.json')));
save('core-cases-all.json', cases);
const attr = tag => Object.fromEntries([...tag.matchAll(/(\w+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
const runs = ['final-tests/coating-all.trx', 'guard-tests/final-affected.trx', 'delivery-tests/delivery-general.trx'];
const latest = new Map();
const runSummary = runs.map(p => {
  const xml = read(join(evidence, p));
  const results = [...xml.matchAll(/<UnitTestResult\b[^>]*>/g)].map(m => attr(m[0]));
  for (const r of results) latest.set(r.testName, { name: r.testName, outcome: r.outcome, run: p });
  return { path: p, total: results.length, passed: results.filter(r => r.outcome === 'Passed').length, failed: results.filter(r => r.outcome === 'Failed').length };
});
save('test-ledger.json', { runs: runSummary, unique: latest.size, latest: [...latest.values()],
  allLatestPassed: [...latest.values()].every(r => r.outcome === 'Passed') });
const prior = json('validation/rechecks/2026-10-30/coating-competition/integrity.json');
const frozen = prior.frozen.filter(p => !p.path.endsWith('CoherentThinFilmSolver.cs')).map(p => ({ path: p.path, prior: p.currentSha256, current: sha(p.path), unchanged: p.currentSha256 === sha(p.path) }));
const design = 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs';
const tail = p => read(p).replace(/\r\n/g, '\n').slice(read(p).replace(/\r\n/g, '\n').indexOf('    private static bool Converged('));
const baseline = p => join(evidence, 'baseline-source', p);
const forward = 'src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs';
const unchangedAcceptance = tail(design) === tail(baseline(design));
const unchangedForward = read(forward).replace('sealed partial class CoherentThinFilmSolver', 'sealed class CoherentThinFilmSolver').replace(/\r\n/g, '\n') === read(baseline(forward)).replace(/\r\n/g, '\n');
const sourceFiles = [...files('src/OpticalSystemDesign.Core/Coatings'), ...files('labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine')].filter(p => p.endsWith('.cs') && !/[\\/]obj[\\/]/.test(p));
const extra = ['src/OpticalSystemDesign.Core/Optimization/OptimizationFramework.cs', 'src/OpticalSystemDesign.Core/Optimization/NumericalOptimizers.cs', 'src/OpticalSystemDesign.App/Laboratories/CoatingDesign/MainWindow.cs'];
const binaries = ['OpticalSystemDesign.Core.dll', 'OpticalSystemDesign.CoatingDesign.Engine.dll'].map(name => {
  const paths = ['src/OpticalSystemDesign.App/bin/Debug/net10.0', 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Debug/net10.0', 'tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0'].map(p => join(p, name));
  const hashes = paths.map(path => ({ path, sha256: sha(path) }));
  return { name, identical: hashes.every(h => h.sha256 === hashes[0].sha256), hashes };
});
const documents = ['docs/COATING_GENERAL_FIXES_2026-10-30.md', join(evidence, 'README.md')];
const brokenLinks = documents.flatMap(document => [...read(document).matchAll(/\]\(([^)\s]+)\)/g)].map(m => m[1])
  .filter(target => !target.startsWith('http') && !target.startsWith('#') && !existsSync(join(dirname(document), target.split('#')[0])))
  .map(target => ({ document, target })));
save('integrity.json', { acceptanceBodiesUnchanged: unchangedAcceptance, forwardSourceUnchangedExceptPartialDeclaration: unchangedForward, frozen,
  binaries, brokenLinks, sources: [...sourceFiles, ...extra].map(path => ({ path, sha256: sha(path) })) });
if (!unchangedAcceptance || !unchangedForward || brokenLinks.length || frozen.some(f => !f.unchanged) || binaries.some(b => !b.identical) || pairs.some(p => !p.identicalInputsExceptProtocol) || [...latest.values()].some(r => r.outcome !== 'Passed')) process.exitCode = 1;
console.log(JSON.stringify({ runs: runSummary, unique: latest.size, pairedRuns: rows.length, referenceCases: cases.length, unchangedAcceptance, unchangedForward }));
