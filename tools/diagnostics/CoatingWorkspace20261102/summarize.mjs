// Metadata receipt only: no source/runtime duplication and no optical calculations.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const evidence = 'validation/rechecks/2026-11-02/coating-workspace';
const read = path => readFileSync(path, 'utf8').replace(/^\uFEFF/, '');
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const git = args => execFileSync('git', args, { encoding: 'utf8' }).trim();
const attr = tag => Object.fromEntries([...tag.matchAll(/(\w+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
const tests = ['final/final-workspace.trx', 'transport-final/transport-final.trx', 'wording/wording.trx'].map(path => {
    const xml = read(join(evidence, path));
    const cases = [...xml.matchAll(/<UnitTestResult\b[^>]*>/g)].map(m => attr(m[0]));
    return { path, total: cases.length, passed: cases.filter(c => c.outcome === 'Passed').length,
        failed: cases.filter(c => c.outcome === 'Failed').length, cases: cases.map(c => ({ name: c.testName, outcome: c.outcome })) };
});
const names = new Set(tests.flatMap(t => t.cases.map(c => c.name)));
const paths = [...git(['diff', '--name-only']).split('\n'), ...git(['ls-files', '--others', '--exclude-standard']).split('\n')]
    .filter(p => /\.(cs|csproj|mjs|py)$/.test(p) || p.endsWith('incoherent-substrate-20261102.json'));
const assemblies = ['OpticalSystemDesign.Core.dll', 'OpticalSystemDesign.CoatingDesign.Engine.dll'].map(name => {
    const binaries = ['src/OpticalSystemDesign.App/bin/Release/net10.0',
        'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0',
        'tests/OpticalSystemDesign.Tests/bin/Release/net10.0'].map(folder => ({ path: join(folder, name), sha256: hash(join(folder, name)) }));
    return { name, identical: binaries.every(b => b.sha256 === binaries[0].sha256), binaries };
});
const formalPath = 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs';
const normalize = text => text.replace(/\r\n/g, '\n').trim();
const formalUnchangedExceptDisplayMaterialSelection = normalize(read(formalPath)
    .split('\n').filter(line => !line.includes('displayBand is not null && experiment.StackGeometry')).join('\n'))
    === normalize(execFileSync('git', ['show', 'HEAD:' + formalPath], { encoding: 'utf8' }));
const fixture = JSON.parse(read('labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/incoherent-substrate-20261102.json'));
const receipt = { baseCommit: git(['rev-parse', 'HEAD']), sourceState: 'uncommitted working tree',
    independentReference: { source: fixture.source, commit: fixture.commit, sourceSha256: fixture.source_sha256, cases: fixture.cases.length },
    tests, distinctTests: names.size, allPassed: tests.every(t => t.total === t.passed),
    formalUnchangedExceptDisplayMaterialSelection, assemblies,
    sources: [...new Set(paths)].filter(Boolean).map(path => ({ path, sha256: hash(path) })),
    frozenReferences: ['validation/coating/tmmcore-0.4.2.json', 'third_party/coating-reference/TFStudio-8b942b3.tar.gz',
        'third_party/coating-reference/tmmcore-0.4.2.tgz'].map(path => ({ path, sha256: hash(path) })),
    previews: readdirSync(join(evidence, 'previews')).filter(p => p.endsWith('.png')).map(p => ({ path: 'previews/' + p, sha256: hash(join(evidence, 'previews', p)) })) };
writeFileSync(join(evidence, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
console.log(JSON.stringify({ tests: tests.map(t => ({ path: t.path, total: t.total, passed: t.passed })),
    distinctTests: receipt.distinctTests, independentCases: fixture.cases.length, formalUnchangedExceptDisplayMaterialSelection,
    matchingAssemblies: assemblies.every(a => a.identical) }));
if (!receipt.allPassed || !formalUnchangedExceptDisplayMaterialSelection || assemblies.some(a => !a.identical)) process.exitCode = 1;
