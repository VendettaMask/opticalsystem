// Seal the completed coating-only verification; no product code imports this utility.
import {readFileSync,writeFileSync,existsSync,readdirSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {createHash} from 'node:crypto';
const root=process.argv[2];
if(!root)throw Error('Expected evidence root');
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const paired=read(`${root}/paired-results.json`),integrity=read(`${root}/integrity.json`),reference=read(`${root}/reference-comparison.json`);
const trx=`${root}/final-tests/coating-guidance-final.trx`,xml=readFileSync(trx,'utf8');
const counterTag=xml.match(/<Counters\b[^>]*>/)?.[0];
if(!counterTag)throw Error('No final TRX counters');
const counters=Object.fromEntries([...counterTag.matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],Number(m[2])]));
if(counters.total!==156||counters.passed!==156||counters.failed!==0||counters.notExecuted!==0)throw Error('Unexpected final test result');
if(paired.pairs!==41||paired.completedRuns!==82||paired.oldPassed!==22||paired.newPassed!==22||paired.newTargetRegressions!==0
 ||paired.guardFailures.length||!paired.integrityPassed||paired.comparison.cases!==2214||reference.runs!==24)throw Error('Incomplete/failed paired verification');
const prior='validation/rechecks/2026-10-30/coating-target-preservation/inputs';
const unchangedInputs=readdirSync(prior).filter(f=>f.endsWith('.coating.json')).map(file=>({file,unchanged:sha(`${prior}/${file}`)===sha(`${root}/inputs/${file}`)}));
if(unchangedInputs.length!==35||unchangedInputs.some(x=>!x.unchanged))throw Error('Frozen input mutated');
const sources=[
 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.TargetProtection.cs',
 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.GeneralObjective.cs',
 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/TargetBalancingTests.cs',
 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/target-balancing-20261031.json',
 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj',
 'tools/diagnostics/CoatingAudit20261030/BalancedTargetAudit.cs',
 'tools/diagnostics/CoatingAudit20261030/Program.cs',
 'tools/diagnostics/CoatingAudit20261030/summarize-balanced-targets.mjs',
 'tools/diagnostics/CoatingAudit20261030/seal-balanced-targets.mjs',
 ...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(p=>p.endsWith('.cs')).map(p=>`src/OpticalSystemDesign.Core/Coatings/${p}`)
].map(path=>({path,sha256:sha(path)}));
const docs=['docs/COATING_TARGET_BALANCING_2026-10-31.md','docs/COATING_DESIGN_LAB.md','docs/FILE_FORMATS_AND_PLUGINS.md',
 'docs/COATING_TARGET_PRESERVATION_2026-10-30.md','docs/COATING_INFEASIBLE_REPAIR_2026-10-30.md','docs/COATING_REFLECTANCE_QUALITY_2026-10-30.md',`${root}/README.md`];
const broken=[];
for(const doc of docs){
 const body=readFileSync(doc,'utf8');
 for(const m of body.matchAll(/\[[^\]]*\]\(([^)]+)\)/g)){
  const link=m[1].replace(/^<|>$/g,'').split('#')[0];
  if(!link||/^(?:https?:|mailto:|plugin:|app:)/.test(link))continue;
  if(!existsSync(resolve(dirname(doc),decodeURIComponent(link))))broken.push({doc,link});
 }
}
if(broken.length)throw Error(JSON.stringify(broken));
const files=[];
for(const folder of ['inputs','final-matrix/before','final-matrix/after','reference-inputs','reference-final-native'])
 for(const file of readdirSync(`${root}/${folder}`).filter(f=>f.endsWith('.json')).sort())files.push({path:`${folder}/${file}`,sha256:sha(`${root}/${folder}/${file}`)});
for(const file of ['paired-results.json','integrity.json','reference-results.json','reference-materials.json','reference-comparison.json',
 'final-matrix/tmmcore-comparison.json','final-tests/coating-guidance-final.trx'])files.push({path:file,sha256:sha(`${root}/${file}`)});
const manifest={scope:'Coating target balancing and guidance peak consistency only',date:'2026-10-31',baseCommit:'3b43bb7f87270e034218bf466b23f885330814fc',
 result:{pairs:paired.pairs,completedRuns:paired.completedRuns,oldPassed:paired.oldPassed,newPassed:paired.newPassed,
  newTargetRegressions:paired.newTargetRegressions,improvedFromStart:paired.improvedFromStart,oldImprovedFromStart:paired.oldImprovedFromStart,
  improvedFromOld:paired.improvedFromOld,degradedFromOld:paired.degradedFromOld,unchangedFromOld:paired.unchangedFromOld},
 tests:{path:trx,sha256:sha(trx),counters,testAssemblySha256:sha('labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.CoatingDesign.Tests.dll')},
 comparison:paired.comparison,reference:{runs:reference.runs,passed:reference.passed,targetRegressions:reference.targetRegressions,scope:reference.scope},
 binaries:integrity.binaries,integrityPassed:paired.integrityPassed,unchangedInputs,documentationLinks:{passed:true,broken},
 sources,documents:docs.map(path=>({path,sha256:sha(path)})),files};
writeFileSync(`${root}/final-manifest.json`,JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({tests:counters.passed,pairs:paired.pairs,runs:paired.completedRuns,referenceRuns:reference.runs,integrityPassed:paired.integrityPassed,linksPassed:true}));
