import {readFileSync,writeFileSync,readdirSync,existsSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {createHash} from 'node:crypto';
const root=process.argv[2];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const paired=read(`${root}/paired-results.json`),reference=read(`${root}/reference-comparison.json`),integrity=read(`${root}/integrity.json`);
const trx=`${root}/final-tests/coating-tradeoffs-final.trx`;
const tag=readFileSync(trx,'utf8').match(/<Counters\b[^>]*>/)?.[0];
if(!tag)throw Error('Missing final counters');
const counters=Object.fromEntries([...tag.matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],Number(m[2])]));
if(counters.total!==168||counters.passed!==168||counters.failed!==0||counters.notExecuted!==0)throw Error('Incomplete final coating tests');
if(paired.pairs!==41||paired.completedRuns!==123||!paired.fiveRepaired||paired.guardFailures.length||!paired.integrityPassed||paired.inputTargetRegressions!==0||paired.comparison.cases!==3321||reference.runs!==24)
 throw Error('Incomplete or failed physical comparison');
const frozen='validation/rechecks/2026-10-31/coating-balanced-targets/inputs';
const inputs=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).map(file=>({file,sha256:sha(`${root}/inputs/${file}`),unchanged:sha(`${root}/inputs/${file}`)===sha(`${frozen}/${file}`)}));
if(inputs.length!==41||inputs.some(x=>!x.unchanged))throw Error('Input snapshot changed');
const engine='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine';
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests';
const paths=[...readdirSync(engine).filter(f=>f.endsWith('.cs')).map(f=>`${engine}/${f}`),
 'src/OpticalSystemDesign.Core/Optimization/InequalityLeastSquaresStep.cs',
 ...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 `${test}/InequalityStepTests.cs`,`${test}/TargetTradeoffTests.cs`,`${test}/TargetBalancingTests.cs`,`${test}/TargetPreservationTests.cs`,`${test}/OpticalSystemDesign.CoatingDesign.Tests.csproj`,
 `${test}/Fixtures/target-tradeoffs-20261031.json`,`${test}/Fixtures/target-balancing-20261031.json`,
 ...['Program.cs','ReflectanceQualityAudit.cs','RepairGuideAudit.cs','freeze-tradeoff-regressions.mjs','summarize-tradeoff-repair.mjs','seal-tradeoff-repair.mjs','write-tradeoff-report.mjs','reference-infeasible.mjs'].map(f=>`tools/diagnostics/CoatingAudit20261030/${f}`)];
const docs=['docs/COATING_TRADEOFF_REPAIR_2026-10-31.md','docs/COATING_DESIGN_LAB.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/COATING_TARGET_BALANCING_2026-10-31.md',
 'docs/COATING_TARGET_PRESERVATION_2026-10-30.md','docs/COATING_INFEASIBLE_REPAIR_2026-10-30.md','docs/COATING_REFLECTANCE_QUALITY_2026-10-30.md',`${root}/README.md`];
const broken=[];
for(const path of docs)for(const m of readFileSync(path,'utf8').matchAll(/\[[^\]]*\]\(([^)]+)\)/g)){
 const link=m[1].replace(/^<|>$/g,'').split('#')[0];
 // The manifest itself is written after verification; its exact output path is
 // a valid forward link, not a requirement to pre-create an empty manifest.
 if(link&&!/^(https?:|mailto:|plugin:|app:)/.test(link)){
  const target=resolve(dirname(path),decodeURIComponent(link));
  if(target!==resolve(root,'final-manifest.json')&&!existsSync(target))broken.push({path,link});
 }
}
if(broken.length)throw Error(JSON.stringify(broken));
const files=[];
for(const folder of ['inputs','final-matrix/legacy-v1','final-matrix/previous-v2','final-matrix/after','reference-inputs','reference-final-native'])
 for(const file of readdirSync(`${root}/${folder}`).filter(f=>f.endsWith('.json')).sort())files.push({path:`${folder}/${file}`,sha256:sha(`${root}/${folder}/${file}`)});
for(const file of ['paired-results.json','integrity.json','reference-materials.json','reference-results.json','reference-comparison.json','final-matrix/tmmcore-comparison.json','final-tests/coating-tradeoffs-final.trx'])
 files.push({path:file,sha256:sha(`${root}/${file}`)});
const manifest={scope:'General coating tradeoff repair, cutoff extrema, constrained reflectance steps',date:'2026-10-31',baseCommit:'3b43bb7f87270e034218bf466b23f885330814fc',
 result:{...paired,results:undefined,comparison:undefined},
 tests:{path:trx,sha256:sha(trx),counters,testAssemblySha256:sha(`${test}/bin/Release/net10.0/OpticalSystemDesign.CoatingDesign.Tests.dll`)},
 comparison:{cases:paired.comparison.cases,maximumErrors:paired.comparison.maximumErrors},
 reference:{runs:reference.runs,passed:reference.passed,targetRegressions:reference.targetRegressions,scope:reference.scope},
 budgets:'Main optimizer remains bounded by MaximumIterations; NB additionally compares one direction with at most MaximumIterations iterations, then at most ceil(sqrt(MaximumIterations)) repair iterations. Extra solves, trials, dense calls and times are disclosed; no equal-total-work claim.',
 binaries:integrity.binaries,inputs,sources:paths.map(path=>({path,sha256:sha(path)})),documents:docs.map(path=>({path,sha256:sha(path)})),documentationLinks:{passed:true,broken},files};
writeFileSync(`${root}/final-manifest.json`,JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({tests:counters.passed,pairs:paired.pairs,runs:paired.completedRuns,fiveRepaired:paired.fiveRepaired,integrityPassed:paired.integrityPassed,linksPassed:true}));
