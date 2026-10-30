// Offline evidence aggregation and primary-source comparison. Never used by the application.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const root=process.argv[2], referencePackage=process.argv[3];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const before=read(`${root}/final-matrix/before/summary.json`), after=read(`${root}/final-matrix/after/summary.json`);
const files=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).sort();
const pairs=files.map(file=>{
  const input=read(`${root}/inputs/${file}`), a=before.find(r=>r.Name===input.Name),b=after.find(r=>r.Name===input.Name);
  if(!a||!b) throw Error('Missing paired result: '+input.Name);
  const output=read(`${root}/final-matrix/after/${file}`);
  const sameInput=a.InputFingerprint===b.InputFingerprint&&JSON.stringify(a.Target)===JSON.stringify(b.Target)
    &&JSON.stringify(a.Settings)===JSON.stringify(b.Settings);
  const materialsUnchanged=JSON.stringify(input.Materials)===JSON.stringify(output.Materials);
  const targetUnchanged=JSON.stringify(input.Target)===JSON.stringify(output.Target)&&JSON.stringify(input.Settings)===JSON.stringify(output.Settings);
  const fixedPreserved=input.Layers.length===output.Layers.length&&input.Layers.every((l,i)=>l.Variable||JSON.stringify(l)===JSON.stringify(output.Layers[i]));
  const eligible=a.BeforePassed&&a.Target.Kind!=='NarrowBand', improved=b.AfterLoss<a.AfterLoss;
  if(!sameInput||!materialsUnchanged||!targetUnchanged||!fixedPreserved||!b.ProtectedLayers||!b.IdenticalMaterials
    ||b.Run.Iterations>input.Settings.MaximumIterations||(eligible&&(!b.AfterPassed||b.AfterLoss>a.AfterLoss))) throw Error('Pair guard failed: '+input.Name);
  const narrowBandUnchanged=a.Target.Kind!=='NarrowBand'||JSON.stringify(read(`${root}/final-matrix/before/${file}`).Layers)===JSON.stringify(output.Layers);
  if(!narrowBandUnchanged) throw Error('Unrelated narrowband control changed');
  return {name:input.Name,file,inputSha256:sha(`${root}/inputs/${file}`),sameInput,materialsUnchanged,targetUnchanged,fixedPreserved,
    eligible,improved,oldLoss:a.AfterLoss,newLoss:b.AfterLoss,oldPassed:a.AfterPassed,newPassed:b.AfterPassed,
    oldIterations:a.Run.Iterations,newIterations:b.Run.Iterations,oldEvaluations:a.Run.Evaluations,newEvaluations:b.Run.Evaluations,
    quality:b.Run.GeneralWork,oldMetrics:a.AfterMetrics,newMetrics:b.AfterMetrics,checks:b.Checks,narrowBandUnchanged};
});
if(before.length!==files.length||after.length!==files.length) throw Error('Unexpected or missing cases');
const allCases=['before','after'].flatMap(arm=>read(`${root}/final-matrix/${arm}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(allCases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',referencePackage,
  `${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const source='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs';
const normalized=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n');
const baseline=normalized(`${root}/baseline-source/DesignService.cs`),current=normalized(source);
const section=(text,start,end)=>text.slice(text.indexOf(start),end?text.indexOf(end,text.indexOf(start)):text.length);
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((s,i)=>({name:i?'Checks':'Converged',
  unchanged:section(baseline,s,i?null:'private static RequirementCheck[] Checks')===section(current,s,i?null:'private static RequirementCheck[] Checks')}));
const untouched=['src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs','src/OpticalSystemDesign.Core/Coatings/ThinFilmThicknessSensitivity.cs',
  'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Examples.cs','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json']
  .map(path=>({path,unchanged:execFileSync('git',['show',`HEAD:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')===normalized(path)}));
const runtime='.tmp/coating-quality-compare-20261030',test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,
  before:sha(`${runtime}/before-final/${name}.dll`),after:sha(`${runtime}/after-final/${name}.dll`),test:sha(`${test}/${name}.dll`)}));
const sameCore=binaries.filter(b=>b.name!=='OpticalSystemDesign.CoatingDesign.Engine').every(b=>b.before===b.after&&b.after===b.test);
const sameNewEngine=binaries.find(b=>b.name==='OpticalSystemDesign.CoatingDesign.Engine').after===binaries.find(b=>b.name==='OpticalSystemDesign.CoatingDesign.Engine').test;
const tmmArchive='third_party/coating-reference/tmmcore-0.4.2.tgz';
const referenceIntact=sha(tmmArchive)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'
  &&createHash('sha256').update(execFileSync('tar',['-xOf',tmmArchive,'package/src/tmm.js'])).digest('hex')===sha(`${referencePackage}/src/tmm.js`);
const integrity={gates,untouched,binaries,sameCore,sameNewEngine,referenceIntact};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const summary={pairs:pairs.length,completedRuns:before.length+after.length,previouslyFeasible:pairs.filter(p=>p.eligible).length,
  improvedFeasible:pairs.filter(p=>p.eligible&&p.improved).length,oldPassed:pairs.filter(p=>p.oldPassed).length,newPassed:pairs.filter(p=>p.newPassed).length,
  unchangedFailed:pairs.filter(p=>!p.oldPassed&&!p.newPassed).length,comparison:read(`${root}/final-matrix/tmmcore-comparison.json`).maximumErrors,
  integrityPassed:gates.every(g=>g.unchanged)&&untouched.every(g=>g.unchanged)&&sameCore&&sameNewEngine&&referenceIntact,results:pairs};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined}));
if(!summary.integrityPassed) process.exitCode=1;
