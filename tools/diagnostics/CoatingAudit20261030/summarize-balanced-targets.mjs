// Offline comparison of start-relative target directions against sealed commit 3b43bb7f.
// The same physical input/acceptance guards are checked independently of product ranking.
import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],pkg=process.argv[3];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=b=>createHash('sha256').update(b).digest('hex'),sha=p=>hash(readFileSync(p));
const same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const before=read(`${root}/final-matrix/before/summary.json`),after=read(`${root}/final-matrix/after/summary.json`);
function deficits(t,m,od){
 if(t.Kind==='Antireflection')return [Math.max(0,m.MaximumReflectance-t.Reflectance)/(t.Reflectance||1)];
 if(t.Kind==='HighReflector')return [Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)];
 return [Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
  m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
  Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,
  ...od.map(x=>Math.max(0,t.BlockingOd-x)/Math.max(1,t.BlockingOd))];
}
const files=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).sort();
const pairs=files.map(file=>{
 const input=read(`${root}/inputs/${file}`),a=before.find(x=>x.Name===input.Name),b=after.find(x=>x.Name===input.Name);
 if(!a||!b)throw Error(`Missing pair: ${input.Name}`);
 const old=read(`${root}/final-matrix/before/${file}`),output=read(`${root}/final-matrix/after/${file}`);
 const start=deficits(input.Target,b.BeforeMetrics,b.BeforeBlockingOd),previous=deficits(input.Target,a.AfterMetrics,a.AfterBlockingOd),current=deficits(input.Target,b.AfterMetrics,b.AfterBlockingOd);
 const preserved=(x)=>x.every((v,i)=>v<=start[i]);
 const guards={sameInput:a.InputFingerprint===b.InputFingerprint&&same(a.Target,b.Target)&&same(a.Settings,b.Settings),
  material:same(input.Materials,output.Materials)&&same(input.Materials,old.Materials),target:same(input.Target,output.Target),settings:same(input.Settings,output.Settings),
  structure:input.Layers.length===output.Layers.length&&input.Layers.every((l,i)=>l.MaterialId===output.Layers[i].MaterialId&&l.Variable===output.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||same(l,output.Layers[i])),
  bounds:output.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  budget:b.Run.Iterations<=input.Settings.MaximumIterations,individualTargets:preserved(current),
  directionRestarts:(b.Run.GeneralWork.TargetDirectionRestarts??0)>=0&&(b.Run.GeneralWork.TargetDirectionRestarts??0)<=1,
  certified:b.AfterPassed===(output.Result.SamplingConverged&&output.Result.Checks.every(c=>c.Passed)),
  requirements:same(old.Result.Checks.map(c=>[c.Name,c.Requirement]),output.Result.Checks.map(c=>[c.Name,c.Requirement])),oldPassedRetained:!a.AfterPassed||b.AfterPassed};
 return {name:input.Name,inputSha256:sha(`${root}/inputs/${file}`),fingerprint:b.InputFingerprint,guards,
  startDeficits:start,oldDeficits:previous,newDeficits:current,oldTargetsPreserved:preserved(previous),
  oldLoss:a.AfterLoss,newLoss:b.AfterLoss,improvedFromOld:b.AfterLoss<a.AfterLoss,degradedFromOld:b.AfterLoss>a.AfterLoss,improvedFromStart:b.AfterLoss<b.BeforeLoss,
  oldPassed:a.AfterPassed,newPassed:b.AfterPassed,oldMetrics:a.AfterMetrics,newMetrics:b.AfterMetrics,
  startOd:b.BeforeBlockingOd,oldOd:a.AfterBlockingOd,newOd:b.AfterBlockingOd,checks:b.Checks,oldRun:a.Run,newRun:b.Run,oldSeconds:a.Seconds,newSeconds:b.Seconds};
});
if(before.length!==files.length||after.length!==files.length)throw Error('Unexpected case count');
const cases=['before','after'].flatMap(arm=>read(`${root}/final-matrix/${arm}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(cases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',pkg,`${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n');
const original=norm(`${root}/baseline-source/DesignService.cs`),current=norm('labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs');
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((s,i)=>({name:i?'Checks':'Converged',unchanged:section(original,s,i?null:'private static RequirementCheck[] Checks')===section(current,s,i?null:'private static RequirementCheck[] Checks')}));
const untouched=[...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(p=>p.endsWith('.cs')).map(p=>`src/OpticalSystemDesign.Core/Coatings/${p}`),'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Examples.cs','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.ReflectanceQuality.cs'].map(path=>({path,unchanged:norm(path)===execFileSync('git',['show',`3b43bb7f:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,before:sha(`${root}/final-matrix/before-runtime/${name}.dll`),after:sha(`${root}/final-matrix/after-runtime/${name}.dll`),test:sha(`${test}/${name}.dll`)}));
const binaryPassed=binaries.every(x=>x.after===x.test&&(x.name.endsWith('.Engine')?x.before!==x.after:x.before===x.after));
const tf='third_party/coating-reference/TFStudio-8b942b3.tar.gz',tmm='third_party/coating-reference/tmmcore-0.4.2.tgz',prefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
const referenceSources=['src/utils/physics/optimizer/lsqEngine.js','src/utils/optimizers/newton.js','src/utils/physics/optimizer/operandModel.js','src/utils/synthesis/structuralOptimizer.js'].map(path=>({path,unchanged:hash(execFileSync('tar',['-xOf',tf,`${prefix}/${path}`]))===sha(`.tmp/coating-infeasible-repair/${prefix}/${path}`)}));
const referencePassed=sha(tf)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'&&sha(tmm)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'
 &&referenceSources.every(x=>x.unchanged)&&hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`${pkg}/src/tmm.js`);
const priorManifest=read('validation/rechecks/2026-10-30/coating-infeasible-repair/final-manifest.json');
const unchangedMeasurements=['src/OpticalSystemDesign.Core/Coatings/ThinFilmSpectrum.cs','src/OpticalSystemDesign.Core/Coatings/ThinFilmBandSensitivity.cs'].map(path=>({path,unchanged:sha(path)===priorManifest.sources.find(s=>s.path===path).sha256}));
const referenceDependencyIntact=hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`.tmp/coating-infeasible-repair/${prefix}/node_modules/tmmcore/src/tmm.js`);
const integrity={gates,untouched,unchangedMeasurements,binaries,binaryPassed,referenceSources,referencePassed,referenceDependencyIntact};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const summary={pairs:pairs.length,completedRuns:before.length+after.length,oldPassed:pairs.filter(x=>x.oldPassed).length,newPassed:pairs.filter(x=>x.newPassed).length,
 oldTargetRegressions:pairs.filter(x=>!x.oldTargetsPreserved).length,newTargetRegressions:pairs.filter(x=>!x.guards.individualTargets).length,
 improvedFromStart:pairs.filter(x=>x.improvedFromStart).length,oldImprovedFromStart:before.filter(x=>x.AfterLoss<x.BeforeLoss).length,
 improvedFromOld:pairs.filter(x=>x.improvedFromOld).length,degradedFromOld:pairs.filter(x=>x.degradedFromOld).length,
 unchangedFromOld:pairs.filter(x=>x.oldLoss===x.newLoss).length,
 guardFailures:pairs.filter(x=>!Object.values(x.guards).every(Boolean)).map(x=>({name:x.name,guards:x.guards})),
 integrityPassed:gates.every(x=>x.unchanged)&&untouched.every(x=>x.unchanged)&&unchangedMeasurements.every(x=>x.unchanged)&&binaryPassed&&referencePassed&&referenceDependencyIntact,
 comparison:read(`${root}/final-matrix/tmmcore-comparison.json`),results:pairs};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
const refs=read(`${root}/reference-final-native/summary.json`).map(r=>{
 const p=pairs.find(p=>p.name===r.c.Source),input=read(`${root}/inputs/${r.c.Source}.coating.json`);
 const result=read(`${root}/reference-final-native/${r.Name}.coating.json`).Result;
 const od=(input.Target.StopBands??[]).map(b=>Math.min(...result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 const d=deficits(input.Target,r.Metrics,od);
 return {name:r.Name,source:r.c.Source,passed:r.Passed,targetsPreserved:d.every((x,i)=>x<=p.startDeficits[i]),deficits:d,startDeficits:p.startDeficits,metrics:r.Metrics,od,
  iterations:r.c.Iterations,reason:r.c.Reason,initialMerit:r.c.InitialMerit,finalMerit:r.c.FinalMerit,seconds:r.c.Seconds};
});
writeFileSync(`${root}/reference-comparison.json`,JSON.stringify({runs:refs.length,passed:refs.filter(r=>r.passed).length,targetRegressions:refs.filter(r=>!r.targetsPreserved).length,
 scope:'Unmodified pinned engines with their point squared targets, not identical to the native measured-band objective; original gates reapplied in shared Core',results:refs},null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined,comparison:summary.comparison.maximumErrors}));
if(!summary.integrityPassed||summary.guardFailures.length)process.exitCode=1;
