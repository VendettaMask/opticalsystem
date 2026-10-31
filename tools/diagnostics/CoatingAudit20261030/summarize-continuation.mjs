import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],pkg=process.argv[3],arms=['before','budget-only','after'];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=b=>createHash('sha256').update(b).digest('hex'),sha=p=>hash(readFileSync(p)),same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n');
const summaries=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/summary.json`)]));
const deficits=(t,m,od)=>t.Kind==='Antireflection'?[Math.max(0,m.MaximumReflectance-t.Reflectance)/(t.Reflectance||1)]:t.Kind==='HighReflector'?[Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)]:[
 Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
 m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
 Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,...od.map(v=>Math.max(0,t.BlockingOd-v)/Math.max(1,t.BlockingOd))];
const inside=(a,b)=>a.length===b.length&&a.every((v,i)=>v<=b[i]);
const files=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).sort(),witnesses=[];
const results=files.map(file=>{
 const input=read(`${root}/inputs/${file}`),rows=Object.fromEntries(arms.map(a=>[a,summaries[a].find(x=>x.Name===input.Name)]));
 const docs=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/${file}`)]));
 if(arms.some(a=>!rows[a]))throw Error(`Missing run ${file}`);
 const current=rows.after,old=rows.before,control=rows['budget-only'],d=docs.after,work=current.Run.GeneralWork;
 const start=deficits(input.Target,current.BeforeMetrics,current.BeforeBlockingOd),v=deficits(input.Target,current.AfterMetrics,current.AfterBlockingOd),previous=deficits(input.Target,old.AfterMetrics,old.AfterBlockingOd);
 const window=Math.ceil(Math.sqrt(input.Settings.MaximumIterations));
 const guards={input:arms.every(a=>rows[a].InputFingerprint===current.InputFingerprint&&same(rows[a].Target,input.Target)&&same(rows[a].Settings,input.Settings)),
  materials:arms.every(a=>same(docs[a].Materials,input.Materials)),target:same(d.Target,input.Target),settings:same(d.Settings,input.Settings),
  structure:d.Layers.length===input.Layers.length&&d.Layers.every((l,i)=>l.MaterialId===input.Layers[i].MaterialId&&l.Variable===input.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||same(l,d.Layers[i])),bounds:d.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  mainBudget:current.Run.Iterations<=input.Settings.MaximumIterations,
  comparisonBudget:(work.TargetComparisonRuns??0)<=1&&(work.TargetComparisonIterations??0)<=input.Settings.MaximumIterations,
  restorationBudget:(work.TargetRepairIterations??0)<=Math.min(input.Settings.MaximumIterations,2*window)&&(work.TargetRepairContinuationIterations??0)<=window,
  polishBudget:(work.TargetParetoPolishIterations??0)<=window,
  reflectanceBudget:input.Target.Kind==='NarrowBand'||(work.ReflectancePreconditionerIterations??0)+(work.QualityIterations??0)===current.Run.Iterations,
  individualInputTargets:inside(v,start),individualPreviousTargets:inside(v,previous),
  certified:current.AfterPassed===(d.Result.SamplingConverged&&d.Result.Checks.every(c=>c.Passed)),
  passedRetained:!old.AfterPassed||current.AfterPassed,
  gates:arms.every(a=>same(docs[a].Result.Checks.map(c=>[c.Name,c.Requirement]),d.Result.Checks.map(c=>[c.Name,c.Requirement]))),
  trialEnvelopes:(current.Run.TargetRepairTrials??[]).filter(t=>t.Retained).every(t=>t.ViolationCeilings&&inside(t.Violations,t.ViolationCeilings))};
 const compact=a=>({loss:rows[a].AfterLoss,passed:rows[a].AfterPassed,metrics:rows[a].AfterMetrics,od:rows[a].AfterBlockingOd,run:rows[a].Run,seconds:rows[a].Seconds});
 const witness=input.Target.Kind==='NarrowBand'?d.Result.Spectrum.find(p=>p.WavelengthNanometers===d.Result.Metrics.PeakWavelengthNanometers)
  :input.Target.Kind==='HighReflector'?d.Result.Spectrum.reduce((a,b)=>a.Power.Reflectance<b.Power.Reflectance?a:b):null;
 if(witness)witnesses.push({name:input.Name,kind:input.Target.Kind,wavelength:witness.WavelengthNanometers,power:witness.Power,
  note:'Actual stack at its measured peak or worst reflectance. Loss decomposition is not a global absorption bound or infeasibility proof.'});
 return {name:input.Name,kind:input.Target.Kind,inputSha256:sha(`${root}/inputs/${file}`),fingerprint:current.InputFingerprint,guards,startDeficits:start,previousDeficits:previous,newDeficits:v,
  before:compact('before'),budgetOnly:compact('budget-only'),after:compact('after'),
  betterThanBefore:current.AfterLoss<old.AfterLoss,worseThanBefore:current.AfterLoss>old.AfterLoss,
  betterThanBudgetOnly:current.AfterLoss<control.AfterLoss,worseThanBudgetOnly:current.AfterLoss>control.AfterLoss};
});
if(arms.some(a=>summaries[a].length!==files.length))throw Error('Incomplete arm');
const cases=arms.flatMap(a=>read(`${root}/final-matrix/${a}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(cases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',pkg,`${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const baseline=norm(`${root}/baseline-source/DesignService.cs`),current=norm('labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs');
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((name,i)=>({name,unchanged:section(baseline,name,i?null:'private static RequirementCheck[] Checks')===section(current,name,i?null:'private static RequirementCheck[] Checks')}));
const physics=[...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/MaterialLibrary.cs'].map(path=>({path,unchanged:norm(path)===execFileSync('git',['show',`3b43bb7f:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const controlSource=readdirSync(`${root}/control-source`).filter(f=>f.endsWith('.cs')).map(file=>{
 const baseline=norm(`${root}/baseline-source/${file}`);
 const expected=file==='DesignService.FeasibleTargets.cs'?baseline.replace('Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations)))','Math.Max(1, 3 * (int)Math.Ceiling(Math.Sqrt(maximumIterations)))'):baseline;
 return {file,unchangedExceptAllowance:norm(`${root}/control-source/${file}`)===expected};
});
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,...Object.fromEntries(arms.map(a=>[a,sha(`${root}/final-matrix/${a}-runtime/${name}.dll`)])),test:sha(`${test}/${name}.dll`)}));
const binaryPassed=binaries.every(b=>b.after===b.test&&(b.name.endsWith('.Engine')?b.before!==b.after&&b['budget-only']!==b.after:arms.every(a=>b[a]===b.after)));
const prefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428',tf='third_party/coating-reference/TFStudio-8b942b3.tar.gz',tmm='third_party/coating-reference/tmmcore-0.4.2.tgz';
const referenceSources=['src/utils/physics/optimizer/lsqEngine.js','src/utils/physics/optimizer/boundedStep.js','src/utils/physics/optimizer/linalg.js','src/utils/optimizers/newton.js','src/utils/optimizers/sqp.js'].map(path=>({path,unchanged:hash(execFileSync('tar',['-xOf',tf,`${prefix}/${path}`]))===sha(`.tmp/coating-infeasible-repair/${prefix}/${path}`)}));
const referencePassed=sha(tf)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'&&sha(tmm)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'&&referenceSources.every(x=>x.unchanged)
 &&hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`${pkg}/src/tmm.js`)
 &&hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`.tmp/coating-infeasible-repair/${prefix}/node_modules/tmmcore/src/tmm.js`);
const integrity={gates,physics,controlSource,binaries,binaryPassed,referenceSources,referencePassed};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const count=f=>results.filter(f).length;
const comparison=read(`${root}/final-matrix/tmmcore-comparison.json`);
const summary={cases:files.length,completedRuns:files.length*arms.length,passed:Object.fromEntries(arms.map(a=>[a,summaries[a].filter(x=>x.AfterPassed).length])),
 betterThanBefore:count(x=>x.betterThanBefore),worseThanBefore:count(x=>x.worseThanBefore),sameAsBefore:count(x=>x.before.loss===x.after.loss),
 betterThanBudgetOnly:count(x=>x.betterThanBudgetOnly),worseThanBudgetOnly:count(x=>x.worseThanBudgetOnly),sameAsBudgetOnly:count(x=>x.budgetOnly.loss===x.after.loss),
 continuationCases:count(x=>(x.after.run.GeneralWork.TargetRepairContinuationIterations??0)>0),paretoGainCases:count(x=>(x.after.run.GeneralWork.TargetParetoAcceptedCandidates??0)>0),
 guardFailures:results.filter(x=>!Object.values(x.guards).every(Boolean)).map(x=>({name:x.name,guards:x.guards})),
 integrityPassed:gates.every(x=>x.unchanged)&&physics.every(x=>x.unchanged)&&controlSource.every(x=>x.unchangedExceptAllowance)&&binaryPassed&&referencePassed,
 comparison:{cases:comparison.cases,maximumErrors:comparison.maximumErrors},results};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
writeFileSync(`${root}/physical-loss-witnesses.json`,JSON.stringify(witnesses,null,2)+'\n');
const refs=read(`${root}/reference-native/summary.json`).map(r=>{
 const input=read(`${root}/reference-inputs/${r.c.Source}.coating.json`),p=results.find(p=>p.name===r.c.Source),d=read(`${root}/reference-native/${r.Name}.coating.json`);
 const od=(input.Target.StopBands??[]).map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 const v=deficits(input.Target,r.Metrics,od);
 return {name:r.Name,source:r.c.Source,passed:r.Passed,targetsPreserved:inside(v,p.startDeficits),metrics:r.Metrics,od,deficits:v,iterations:r.c.Iterations,reason:r.c.Reason,seconds:r.c.Seconds};
});
writeFileSync(`${root}/reference-comparison.json`,JSON.stringify({runs:refs.length,passed:refs.filter(r=>r.passed).length,targetRegressions:refs.filter(r=>!r.targetsPreserved).length,
 scope:'Pinned DLS/Newton/SQP point-squared guide, shared Core original gates reapplied. SQP hard-box constraints are not native measured-OD/FWHM constraints; not same-objective or same-total-work ranking.',results:refs},null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined}));
if(!summary.integrityPassed||summary.guardFailures.length||summary.worseThanBefore)process.exitCode=1;
