// Frozen, offline comparison. Never imported by product projects.
import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],referencePackage=process.argv[3];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=b=>createHash('sha256').update(b).digest('hex'); const sha=p=>hash(readFileSync(p));
const before=read(`${root}/final-matrix/before/summary.json`),after=read(`${root}/final-matrix/after/summary.json`);
const files=readdirSync(`${root}/all-inputs`).filter(f=>f.endsWith('.coating.json')).sort();
const pairs=files.map(file=>{
 const input=read(`${root}/all-inputs/${file}`),a=before.find(x=>x.Name===input.Name),b=after.find(x=>x.Name===input.Name);
 if(!a||!b)throw Error(`Missing ${input.Name}`);
 const output=read(`${root}/final-matrix/after/${file}`),old=read(`${root}/final-matrix/before/${file}`);
 const guards={sameInput:a.InputFingerprint===b.InputFingerprint&&JSON.stringify(a.Target)===JSON.stringify(b.Target)&&JSON.stringify(a.Settings)===JSON.stringify(b.Settings),
  materials:JSON.stringify(input.Materials)===JSON.stringify(output.Materials)&&JSON.stringify(input.Materials)===JSON.stringify(old.Materials),
  target:JSON.stringify(input.Target)===JSON.stringify(output.Target),settings:JSON.stringify(input.Settings)===JSON.stringify(output.Settings),
  layers:input.Layers.length===output.Layers.length&&input.Layers.every((l,i)=>l.MaterialId===output.Layers[i].MaterialId&&l.Variable===output.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||JSON.stringify(l)===JSON.stringify(output.Layers[i])),
  bounds:output.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  iterations:b.Run.Iterations<=input.Settings.MaximumIterations,
  passedRetained:!a.AfterPassed||b.AfterPassed,
  acceptance:b.AfterPassed===(output.Result.SamplingConverged&&output.Result.Checks.every(c=>c.Passed)),
  checkRequirements:JSON.stringify(old.Result.Checks.map(c=>[c.Name,c.Requirement]))===JSON.stringify(output.Result.Checks.map(c=>[c.Name,c.Requirement]))};
 if(!Object.values(guards).every(Boolean))throw Error(`Guard failed ${input.Name}: ${JSON.stringify(guards)}`);
 return {name:input.Name,file,inputSha256:sha(`${root}/all-inputs/${file}`),inputFingerprint:a.InputFingerprint,guards,
  oldPassed:a.AfterPassed,newPassed:b.AfterPassed,oldLoss:a.AfterLoss,newLoss:b.AfterLoss,improved:b.AfterLoss<a.AfterLoss,
  oldMetrics:a.AfterMetrics,newMetrics:b.AfterMetrics,checks:b.Checks,oldRun:a.Run,newRun:b.Run,oldSeconds:a.Seconds,newSeconds:b.Seconds};
});
if(before.length!==files.length||after.length!==files.length)throw Error('Unexpected case count');
const allCases=['before','after'].flatMap(arm=>read(`${root}/final-matrix/${arm}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(allCases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',referencePackage,`${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n');
const source='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs';
const a=norm(`${root}/baseline-source/DesignService.cs`),b=norm(source);
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((s,i)=>({name:i?'Checks':'Converged',unchanged:section(a,s,i?null:'private static RequirementCheck[] Checks')===section(b,s,i?null:'private static RequirementCheck[] Checks')}));
const unchanged=['src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs','src/OpticalSystemDesign.Core/Coatings/ThinFilmThicknessSensitivity.cs','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Examples.cs','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json'].map(path=>({path,unchanged:norm(path)===execFileSync('git',['show',`HEAD:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,before:sha(`${root}/final-matrix/before-runtime/${name}.dll`),after:sha(`${root}/final-matrix/after-runtime/${name}.dll`),test:sha(`${test}/${name}.dll`)}));
const binaryPassed=binaries.every(x=>x.after===x.test&&(x.name==='OpticalSystemDesign.CoatingDesign.Engine'?x.before!==x.after:x.before===x.after));
const tf='third_party/coating-reference/TFStudio-8b942b3.tar.gz',tmm='third_party/coating-reference/tmmcore-0.4.2.tgz',prefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
const referenceSources=['src/utils/physics/optimizer/lsqEngine.js','src/utils/optimizers/newton.js','src/utils/physics/optimizer/operandModel.js','src/utils/filter/filterDesign/meritFunction.js','src/utils/synthesis/structuralOptimizer.js'].map(path=>({path,unchanged:hash(execFileSync('tar',['-xOf',tf,`${prefix}/${path}`]))===sha(`.tmp/coating-infeasible-repair/${prefix}/${path}`)}));
const referencePassed=sha(tf)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'&&sha(tmm)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'
 &&referenceSources.every(x=>x.unchanged)&&hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`${referencePackage}/src/tmm.js`);
const integrity={gates,unchanged,binaries,binaryPassed,referenceSources,referencePassed};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const summary={pairs:pairs.length,completedRuns:before.length+after.length,oldPassed:pairs.filter(p=>p.oldPassed).length,newPassed:pairs.filter(p=>p.newPassed).length,
 failedPairs:pairs.filter(p=>!p.oldPassed).length,improvedFailedPairs:pairs.filter(p=>!p.oldPassed&&p.improved).length,
 retainedPassedPairs:pairs.filter(p=>p.oldPassed&&p.newPassed).length,comparison:read(`${root}/final-matrix/tmmcore-comparison.json`),
 integrityPassed:gates.every(g=>g.unchanged)&&unchanged.every(g=>g.unchanged)&&binaryPassed&&referencePassed,results:pairs};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined,comparison:summary.comparison.maximumErrors}));
if(!summary.integrityPassed)process.exitCode=1;
