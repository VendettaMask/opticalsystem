import {readFileSync,writeFileSync,readdirSync,mkdirSync,copyFileSync,existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],engine='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine';
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),text=p=>readFileSync(p,'utf8').replace(/^\uFEFF/,'').replace(/\r\n/g,'\n');
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex'),hash=b=>createHash('sha256').update(b).digest('hex');
const source=readdirSync(`${root}/baseline-source`).filter(f=>f.endsWith('.cs')).map(file=>({file,unchanged:text(`${root}/baseline-source/${file}`)===text(`${engine}/${file}`)}));
let search=text(`${engine}/DesignService.GeneralSearch.cs`);
search=search.replace(/        var validations = new List<SearchSeedValidation>\(\);\n        var finalists = fullSearch \? GeneralPool\(refinedSeeds.Concat\(pool\), ShortCandidateLimit\).ToList\(\)\n            : RankGeneralFinalists\(d, refinedSeeds.Concat\(pool\), materials, work, validations, progress, token\);/,
 '        var finalists = GeneralPool(refinedSeeds.Concat(pool), ShortCandidateLimit).ToList();');
search=search.replace('\n            + validations.Sum(v => v.RefinementRun?.Evaluations ?? 0)','').replace('\n            + validations.Sum(v => v.RefinementRun?.Iterations ?? 0)','');
search=search.replace('candidates.AddRange(finalists.Select(s => new Candidate(s.Name, s.Layers, s.Assessment.Merit,\n                validations.FirstOrDefault(v => LayerIdentity(v.Layers) == LayerIdentity(s.Layers))?.Passed ?? false) { OriginPrototype = s.Origin }));',
 'candidates.AddRange(finalists.Select(s => new Candidate(s.Name, s.Layers, s.Assessment.Merit, false) { OriginPrototype = s.Origin }));');
search=search.replace('            SeedRankingProtocol = fullSearch ? null : SeedRankingProtocol,\n','').replace('            SearchSeedValidations = fullSearch ? null : validations.ToArray(),\n','');
const fullSearchUnchanged=search===text(`${root}/baseline-source/DesignService.GeneralSearch.cs`);
const changed=source.filter(x=>!x.unchanged).map(x=>x.file);
const additions=readdirSync(engine).filter(f=>f.endsWith('.cs')&&!existsSync(`${root}/baseline-source/${f}`));
const trx=text(`${root}/tests/seed-ranking-complete-final.trx`),attrs=s=>Object.fromEntries([...s.matchAll(/([\w]+)="([^"]*)"/g)].map(m=>[m[1],m[2]]));
const c=attrs(trx.match(/<Counters\b([^>]*)/)[1]);
const tests={scope:'Isolated final test assembly with pinned optical Core; not a full module/repository or final default-output test run',total:+c.total,passed:+c.passed,failed:+c.failed,notExecuted:+c.notExecuted,trx:'tests/seed-ranking-complete-final.trx',
 earlierFullRun:'seed-ranking-final.trx: 6 passed; final full Search guard was added later',
 rejectedVerification:'unchanged-search-final.trx: shared test output dependency overwritten (MissingMethod); seed-ranking-isolated-final.trx: older test DLL discovered only 2; neither is the final 6-test result'};
const tmmArchive='third_party/coating-reference/tmmcore-0.4.2.tgz',tfArchive='third_party/coating-reference/TFStudio-8b942b3.tar.gz';
const tmmEntry=execFileSync('tar',['-xOf',tmmArchive,'package/src/tmm.js']);
const referenceSources=[{path:'.tmp/coating-edge-audit-20261030/package/src/tmm.js',unchanged:sha('.tmp/coating-edge-audit-20261030/package/src/tmm.js')===hash(tmmEntry)}];
const tfPrefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
for(const file of ['src/utils/physics/optimizer/lsqEngine.js','src/utils/optimizers/newton.js','src/utils/optimizers/sqp.js','src/utils/physics/optimizer/operandModel.js']){
 const path=`.tmp/coating-infeasible-repair/${tfPrefix}/${file}`;
 referenceSources.push({path,unchanged:sha(path)===hash(execFileSync('tar',['-xOf',tfArchive,`${tfPrefix}/${file}`]))});
}
const binaries=['OpticalSystemDesign.Core.dll','OpticalSystemDesign.Application.dll','OpticalSystemDesign.CoatingDesign.Engine.dll'].map(file=>({file,final:sha(`${root}/final-runtime/${file}`),test:sha(`${root}/test-final-runtime/${file}`),same:sha(`${root}/final-runtime/${file}`)===sha(`${root}/test-final-runtime/${file}`)}));
const p=read(`${root}/paired-generation.json`),physics=read(`${root}/physics-comparison.json`),curvature=read(`${root}/curvature-reference-comparison.json`);
const archiveIntegrity=sha(tmmArchive)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'&&sha(tfArchive)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6';
const passed=fullSearchUnchanged&&changed.length===2&&changed.includes('DesignService.GeneralSearch.cs')&&changed.includes('Experiment.cs')
 &&additions.length===1&&additions[0]==='DesignService.SeedRanking.cs'&&archiveIntegrity&&referenceSources.every(x=>x.unchanged)&&binaries.every(x=>x.same)
 &&tests.total===6&&tests.passed===6&&tests.failed===0&&tests.notExecuted===0&&!p.guardFailures.length&&p.highReflector.unchanged
 &&Object.values(physics.maximumErrors).every(x=>Number.isFinite(x)&&x<=2e-11);
const verification={integrityPassed:passed,sourceComparison:source,addedProductSources:additions,fullSearchUnchanged,archiveIntegrity,referenceSources,binaries,tests,
 physics:{cases:physics.cases,maximumErrors:physics.maximumErrors},curvature:{points:curvature.points,maximumError:curvature.maximumError},
 sourceIdentity:'Shared dirty checkout at HEAD 3b43bb7f87270e034218bf466b23f885330814fc; no claim that HEAD alone identifies final sources',
 buildScope:{defaultEngine:'tests/final-engine-build.log: normal Release dependency graph, zero warnings/errors',tests:'tests/final-isolated-test-build.log: BuildProjectReferences=false to isolated output, zero warnings/errors'},
 incompletePrototypes:['superseded-prototypes.json','rejected-full-search.json']};
writeFileSync(`${root}/verification.json`,JSON.stringify(verification,null,2)+'\n');
if(!passed)throw Error('Final generation verification failed: inspect verification.json');
mkdirSync(`${root}/final-source/Data`,{recursive:true});mkdirSync(`${root}/final-tests/Fixtures`,{recursive:true});
for(const file of readdirSync(engine).filter(f=>f.endsWith('.cs')))copyFileSync(`${engine}/${file}`,`${root}/final-source/${file}`);
for(const file of readdirSync(`${engine}/Data`))copyFileSync(`${engine}/Data/${file}`,`${root}/final-source/Data/${file}`);
for(const file of ['SearchSeedRankingTests.cs','TargetPreservationTests.cs','OpticalSystemDesign.CoatingDesign.Tests.csproj'])copyFileSync(`labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/${file}`,`${root}/final-tests/${file}`);
copyFileSync('labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/search-seed-ranking-20261031.json',`${root}/final-tests/Fixtures/search-seed-ranking-20261031.json`);
console.log(JSON.stringify({integrityPassed:passed,fullSearchUnchanged,tests,physics:verification.physics}));
