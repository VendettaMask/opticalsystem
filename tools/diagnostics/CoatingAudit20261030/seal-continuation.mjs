import {readFileSync,writeFileSync,readdirSync,existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {dirname,resolve} from 'node:path';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const s=read(`${root}/paired-results.json`),r=read(`${root}/reference-comparison.json`),integrity=read(`${root}/integrity.json`);
const trx=`${root}/final-tests/coating-continuation-final.trx`,tag=readFileSync(trx,'utf8').match(/<Counters\b[^>]*>/)?.[0];
if(!tag)throw Error('No final TRX');
const tests=Object.fromEntries([...tag.matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],Number(m[2])]));
if(tests.total!==175||tests.passed!==175||tests.failed||tests.notExecuted||s.cases!==47||s.completedRuns!==141||s.worseThanBefore||s.guardFailures.length||!s.integrityPassed||r.runs!==54)throw Error('Incomplete/failed final verification');
const inputs=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).map(file=>{
 const old=`validation/rechecks/2026-10-31/coating-tradeoff-repair/inputs/${file}`,isOld=existsSync(old);
 return {file,sha256:sha(`${root}/inputs/${file}`),source:isOld?'previous-41':'fresh-six',unchanged:sha(`${root}/inputs/${file}`)===sha(isOld?old:`${root}/holdout-inputs/${file}`)};
});
if(inputs.some(x=>!x.unchanged)||inputs.filter(x=>x.source==='previous-41').length!==41)throw Error('Frozen inputs changed');
const engine='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine',test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests',tool='tools/diagnostics/CoatingAudit20261030';
const sources=[...readdirSync(engine).filter(f=>f.endsWith('.cs')).map(f=>`${engine}/${f}`),
 ...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 'src/OpticalSystemDesign.Core/Optimization/InequalityLeastSquaresStep.cs',`${test}/TargetContinuationTests.cs`,`${test}/TargetTradeoffTests.cs`,`${test}/TargetBalancingTests.cs`,`${test}/Fixtures/target-continuation-20261031.json`,`${test}/OpticalSystemDesign.CoatingDesign.Tests.csproj`,
 ...['ContinuationAudit.cs','ReflectanceQualityAudit.cs','Program.cs','reference-continuation.mjs','freeze-continuation-regressions.mjs','summarize-continuation.mjs','write-continuation-report.mjs','seal-continuation.mjs'].map(f=>`${tool}/${f}`)];
const docs=['docs/COATING_CONSTRAINED_CONTINUATION_2026-10-31.md','docs/COATING_DESIGN_LAB.md','docs/FILE_FORMATS_AND_PLUGINS.md',`${root}/README.md`],broken=[];
for(const path of docs)for(const m of readFileSync(path,'utf8').matchAll(/\[[^\]]*\]\(([^)]+)\)/g)){
 const link=m[1].replace(/^<|>$/g,'').split('#')[0];
 if(link&&!/^(https?:|mailto:|plugin:|app:)/.test(link)){
  const target=resolve(dirname(path),decodeURIComponent(link));
  if(target!==resolve(root,'final-manifest.json')&&!existsSync(target))broken.push({path,link});
 }
}
if(broken.length)throw Error(JSON.stringify(broken));
const files=[];
for(const dir of ['inputs','holdout-inputs','baseline-source','control-source','final-matrix/before','final-matrix/budget-only','final-matrix/after','reference-inputs','reference-native'])
 for(const file of readdirSync(`${root}/${dir}`).filter(f=>/\.(json|cs|csproj)$/.test(f)).sort())files.push({path:`${dir}/${file}`,sha256:sha(`${root}/${dir}/${file}`)});
for(const file of ['paired-results.json','integrity.json','reference-materials.json','reference-results.json','reference-comparison.json','physical-loss-witnesses.json','final-matrix/tmmcore-comparison.json','final-tests/coating-continuation-final.trx'])files.push({path:file,sha256:sha(`${root}/${file}`)});
const manifest={scope:'Coating contracting restoration, monotone constrained LS and severe reflectance preconditioning',date:'2026-10-31',baseCommit:'3b43bb7f87270e034218bf466b23f885330814fc',
 result:{...s,results:undefined},tests:{path:trx,counters:tests,sha256:sha(trx),assemblySha256:sha(`${test}/bin/Release/net10.0/OpticalSystemDesign.CoatingDesign.Tests.dll`)},
 reference:{runs:r.runs,passed:r.passed,targetRegressions:r.targetRegressions,scope:r.scope},inputs,binaries:integrity.binaries,
 budgets:'Reflectance preconditioning+quality <= N. NB additionally compares <= N, restores <= min(N,2ceil(sqrt(N))) including continuation, then constrained LS <= ceil(sqrt(N)); counts are separate. Budget-only old control permits 3ceil(sqrt(N)) restoration steps; not equal optical compute.',
 sources:sources.map(path=>({path,sha256:sha(path)})),documents:docs.map(path=>({path,sha256:sha(path)})),documentationLinks:{passed:true,broken},files};
writeFileSync(`${root}/final-manifest.json`,JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({tests:tests.passed,cases:s.cases,runs:s.completedRuns,reference:r.runs,integrityPassed:s.integrityPassed,linksPassed:true}));
