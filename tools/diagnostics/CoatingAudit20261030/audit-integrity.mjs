// Filesystem provenance only; no product computation or network access.
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const root = process.argv[2];
const bytes = path => readFileSync(path);
const read = path => JSON.parse(bytes(path).toString('utf8').replace(/^\uFEFF/, ''));
const hash = data => createHash('sha256').update(data).digest('hex').toUpperCase();
const previous = 'validation/rechecks/2026-10-30/coating-reference-review';
const oldManifest = read(`${previous}/source-hashes.json`);
const unchangedProduct = oldManifest.Files.filter(f => f.Path.startsWith('labs/CoatingDesign/')
  || f.Path === 'src/OpticalSystemDesign.App/Laboratories/CoatingDesign/MainWindow.cs')
  .map(f => ({ path: f.Path, priorSha256: f.Sha256, currentSha256: hash(bytes(f.Path)),
    unchanged: hash(bytes(f.Path)) === f.Sha256 }));
const frozen = read(`${previous}/reference-integrity.json`).map(f => ({ path: f.Path,
  priorSha256: f.OriginalSha256, currentSha256: hash(bytes(f.Path)), unchanged: hash(bytes(f.Path)) === f.OriginalSha256 }));
const tfPrefix = 'TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/';
const unpacked = `.tmp/coating-review-20261030/${tfPrefix}`;
const sourcePaths = ['src/tmmcore.js', 'src/utils/physics/optimizer/lsqEngine.js',
  'src/utils/physics/optimizer/operandModel.js', 'src/utils/physics/optimizer/filterCatalog.js',
  'src/utils/physics/optimizer/halfWaveSpan.js', 'src/utils/optimizers/newton.js',
  'src/utils/filter/filterDesign/prototypeFamily.js', 'src/utils/filter/filterDesign/coupledPrototype.js',
  'src/utils/filter/filterDesign/prototypeLayers.js', 'src/utils/filter/filterDesign/adjustToIncidentMedium.js',
  'src/utils/filter/filterDesign/spectrum.js', 'src/utils/filter/filterDesign/filterTarget.js',
  'src/utils/filter/filterDesign/globalSearch.js', 'src/utils/filter/filterDesign/nReal.js',
  'src/utils/filter/wdmDesigner/materials.js',
  'src/components/windows/optimization/filterDesignWizard/useFilterDesign.js',
  'src/utils/synthesis/seedGenerator/rank.js'];
const referenceSources = sourcePaths.map(path => {
  const archived = execFileSync('tar', ['-xOf', 'third_party/coating-reference/TFStudio-8b942b3.tar.gz', tfPrefix + path]);
  const diskHash = hash(bytes(unpacked + path));
  return { path: tfPrefix + path, archiveSha256: hash(archived), actualSha256: diskHash, unchanged: hash(archived) === diskHash };
});
for (const base of ['.tmp/coating-review-20261030/package/', `${unpacked}node_modules/tmmcore/`])
  for (const file of ['index.js','tmm.js','phase.js','taylorJet.js','tmmWasm.js']) {
    const path = 'src/' + file;
    const archived = execFileSync('tar', ['-xOf', 'third_party/coating-reference/tmmcore-0.4.2.tgz', 'package/' + path]);
    const current = hash(bytes(base + path));
    referenceSources.push({ path: base + path, archiveSha256: hash(archived), actualSha256: current, unchanged: hash(archived) === current });
  }
const files = [
  'third_party/coating-reference/SiO2-Rodriguez-de-Marcos.yml',
  'third_party/coating-reference/Ta2O5-Rodriguez-de-Marcos.yml',
  ...['HR','NBF1','NBF2'].map(n => `${previous}/defaults/${n}.coating.json`),
  'src/OpticalSystemDesign.Core/Optimization/NumericalOptimizers.cs',
  'src/OpticalSystemDesign.Core/Coatings/ThinFilmSpectrum.cs',
  ...['CoatingAudit.csproj','packages.lock.json','Program.cs','CoatingDeepAudit.cs','reference-deep.mjs',
    'summarize-deep.mjs','audit-integrity.mjs','compare-reference.mjs'].map(n=>`tools/diagnostics/CoatingAudit20261030/${n}`),
].map(path => ({ path, sha256: hash(bytes(path)) }));
const output = { scope: 'Final diagnostic source snapshot, not a claim that all unrelated product binaries were unchanged during this shared-workspace session.',
  head: execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(), uncommitted: true,
  unchangedProduct, frozen, referenceSources, files };
writeFileSync(`${root}/integrity.json`, JSON.stringify(output,null,2)+'\n');
if ([...unchangedProduct,...frozen,...referenceSources].some(f => !f.unchanged)) throw new Error('Provenance mismatch');
console.log(JSON.stringify({unchangedProduct:unchangedProduct.length, frozen:frozen.length,
  referenceSources:referenceSources.length, sourceFiles:files.length, allChecksPassed:true}));
