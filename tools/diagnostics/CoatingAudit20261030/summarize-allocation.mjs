// Offline evidence only. Cold comparisons share the final sampler; raw optimizer records remain intact.
import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2], pkg=process.argv[3], arms=['before','prior-budget-only','budget-only','after'];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n'), same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const summaries=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/summary.json`)]));
const previousRoot='validation/rechecks/2026-10-31/coating-constrained-continuation/final-matrix';
const optimizationSummaries={before:read(`${previousRoot}/after/summary.json`),'prior-budget-only':read(`${previousRoot}/budget-only/summary.json`),
 'budget-only':read(`${root}/final-matrix/budget-only-raw/summary.json`),after:read(`${root}/final-matrix/after-raw/summary.json`)};
const deficits=(t,m,od)=>t.Kind==='Antireflection'?[Math.max(0,m.MaximumReflectance-t.Reflectance)/(t.Reflectance||1)]:t.Kind==='HighReflector'?[Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)]:[
 Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
 m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
 Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,...od.map(v=>Math.max(0,t.BlockingOd-v)/Math.max(1,t.BlockingOd))];
const inside=(a,b)=>a.length===b.length&&a.every((v,i)=>v<=b[i]);
// Historical unfinished quantities have the original Converged resolution; passed
// physical checks remain exact. Raw last-bit regressions are retained separately.
const resolution=t=>t.Kind==='Antireflection'?[1e-5/(t.Reflectance||1)]:t.Kind==='HighReflector'?[1e-5/(1-t.Reflectance||1)]:[
 1e-5/t.PeakTransmittance,Math.min(.02,t.FwhmToleranceNm/10)/t.FwhmNm,
 Math.min(.02,t.CenterToleranceNm/10)/t.FwhmNm,...(t.StopBands??[]).map(()=>.005/Math.max(1,t.BlockingOd))];
const files=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).sort();
const results=files.map(file=>{
 const input=read(`${root}/inputs/${file}`), rows=Object.fromEntries(arms.map(a=>[a,summaries[a].find(x=>x.Name===input.Name)]));
 const docs=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/${file}`)]));
 if(arms.some(a=>!rows[a]))throw Error(`Missing case ${file}`);
 const r=rows.after,d=docs.after,w=r.Run.GeneralWork,n=input.Settings.MaximumIterations,window=Math.ceil(Math.sqrt(n));
 const start=deficits(input.Target,r.BeforeMetrics,r.BeforeBlockingOd), previous=deficits(input.Target,rows.before.AfterMetrics,rows.before.AfterBlockingOd), current=deficits(input.Target,r.AfterMetrics,r.AfterBlockingOd);
 const compact=a=>({loss:rows[a].AfterLoss,passed:rows[a].AfterPassed,metrics:rows[a].AfterMetrics,od:rows[a].AfterBlockingOd,run:rows[a].Run,
   optimizationSeconds:optimizationSummaries[a].find(x=>x.Name===input.Name)?.Seconds??null,coldSeconds:rows[a].Seconds,
   originalReportedPassed:rows[a].OriginalReportedPassed,originalReportedMetrics:rows[a].OriginalReportedMetrics,originalReportedMerit:rows[a].OriginalReportedMerit});
 const guards={sameInput:arms.every(a=>rows[a].InputFingerprint===r.InputFingerprint&&same(rows[a].Target,input.Target)&&same(rows[a].Settings,input.Settings)),
  sameMaterials:arms.every(a=>same(docs[a].Materials,input.Materials)),sameTarget:same(d.Target,input.Target),sameSettings:same(d.Settings,input.Settings),
  sameStructure:d.Layers.length===input.Layers.length&&d.Layers.every((l,i)=>l.MaterialId===input.Layers[i].MaterialId&&l.Variable===input.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||same(l,d.Layers[i])),bounds:d.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  mainBudget:r.Run.Iterations<=n,comparisonBudget:(w.TargetComparisonRuns??0)<=1&&(w.TargetComparisonIterations??0)<=n,
  sharedRecoveryBudget:(w.TargetRepairIterations??0)+(w.TargetParetoPolishIterations??0)<=n&&(w.TargetRepairContinuationIterations??0)<=Math.max(0,n-window),
  polishBudget:(w.TargetParetoPolishIterations??0)<=window,reflectanceMainBudget:input.Target.Kind==='NarrowBand'||(w.ReflectancePreconditionerIterations??0)+(w.QualityIterations??0)===r.Run.Iterations,
  reflectanceMetricBudget:(w.ReflectanceMetricIterations??0)<=Math.ceil(Math.sqrt(w.QualityIterations||1)),
  inputTargets:inside(current,start),previousTargetsWithinOriginalResolution:current.every((v,i)=>v<=previous[i]+resolution(input.Target)[i]),
  previousPassedChecks:docs.before.Result.Checks.filter(c=>c.Passed).every(c=>d.Result.Checks.find(x=>x.Name===c.Name)?.Passed),
  passedRetained:!rows.before.AfterPassed||r.AfterPassed,
  verdict:r.AfterPassed===(d.Result.SamplingConverged&&d.Result.Checks.every(c=>c.Passed)),
  gates:arms.every(a=>same(docs[a].Result.Checks.map(c=>[c.Name,c.Requirement]),d.Result.Checks.map(c=>[c.Name,c.Requirement]))),
  trialEnvelopes:(r.Run.TargetRepairTrials??[]).filter(t=>t.Retained).every(t=>t.ViolationCeilings&&inside(t.Violations,t.ViolationCeilings))};
 return {name:input.Name,kind:input.Target.Kind,inputSha256:sha(`${root}/inputs/${file}`),fingerprint:r.InputFingerprint,guards,
  previousTargetsExact:inside(current,previous),originalConvergenceResolution:resolution(input.Target),
  startDeficits:start,previousDeficits:previous,newDeficits:current,
  before:compact('before'),priorBudgetOnly:compact('prior-budget-only'),budgetOnly:compact('budget-only'),after:compact('after')};
});
if(arms.some(a=>summaries[a].length!==files.length))throw Error('Incomplete arm');
const comparison=key=>({better:results.filter(r=>r.after.loss<r[key].loss).length,worse:results.filter(r=>r.after.loss>r[key].loss).length,same:results.filter(r=>r.after.loss===r[key].loss).length,
 worseCases:results.filter(r=>r.after.loss>r[key].loss).map(r=>r.name)});
const cases=arms.flatMap(a=>read(`${root}/final-matrix/${a}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(cases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',pkg,`${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const old=norm(`${root}/baseline-source/DesignService.cs`),now=norm('labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs');
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((s,i)=>({name:s,unchanged:section(old,s,i?null:'private static RequirementCheck[] Checks')===section(now,s,i?null:'private static RequirementCheck[] Checks')}));
const physics=[...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/MaterialLibrary.cs'].map(path=>({path,
 unchanged:norm(path)===execFileSync('git',['show',`3b43bb7f:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const controlSource=readdirSync(`${root}/control-source`).filter(f=>f.endsWith('.cs')).map(file=>({file,unchangedExceptAllowance:
 norm(`${root}/control-source/${file}`)===norm(`validation/rechecks/2026-10-31/coating-constrained-continuation/control-source/${file}`).replace(
 'Math.Max(1, 3 * (int)Math.Ceiling(Math.Sqrt(maximumIterations)))','maximumIterations')}));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Debug/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,
 after:sha(`${root}/final-matrix/after-runtime/${name}.dll`),control:sha(`${root}/final-matrix/budget-only-runtime/${name}.dll`),
 cold:sha(`${root}/final-matrix/evaluator-runtime/${name}.dll`),test:sha(`${test}/${name}.dll`)}));
const referenceArchives=[['third_party/coating-reference/TFStudio-8b942b3.tar.gz','055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'],
 ['third_party/coating-reference/tmmcore-0.4.2.tgz','fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44']].map(([path,expected])=>({path,sha256:sha(path),unchanged:sha(path)===expected}));
const tfPrefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
const referenceSources=['src/utils/physics/optimizer/lsqEngine.js','src/utils/physics/optimizer/boundedStep.js','src/utils/physics/optimizer/linalg.js','src/utils/optimizers/newton.js','src/utils/optimizers/sqp.js'].map(path=>({path,
 unchanged:createHash('sha256').update(execFileSync('tar',['-xOf',referenceArchives[0].path,`${tfPrefix}/${path}`])).digest('hex')===sha(`.tmp/coating-infeasible-repair/${tfPrefix}/${path}`)}));
const integrity={gates,physics,controlSource,binaries,referenceArchives,referenceSources,passed:gates.every(x=>x.unchanged)&&physics.every(x=>x.unchanged)&&controlSource.every(x=>x.unchangedExceptAllowance)
 &&binaries.every(x=>x.cold===x.test&&(x.name.endsWith('.Engine')?x.after===x.test&&x.after!==x.control:x.after===x.control))
 &&summaries.after.every(x=>x.IdenticalReportedMetrics&&x.IdenticalReportedSpectrum&&x.IdenticalReportedChecks)
 &&referenceArchives.every(x=>x.unchanged)&&referenceSources.every(x=>x.unchanged)};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const rta=read(`${root}/final-matrix/tmmcore-comparison.json`);
const summary={cases:results.length,freshOptimizations:files.length*2,coldStacks:files.length*arms.length,
 coldEvaluationCalls:files.length*arms.length*2,passed:Object.fromEntries(arms.map(a=>[a,summaries[a].filter(x=>x.AfterPassed).length])),
 vsBefore:comparison('before'),vsPriorBudgetOnly:comparison('priorBudgetOnly'),vsBudgetOnly:comparison('budgetOnly'),
 exactPreviousTargetRegressions:results.filter(r=>!r.previousTargetsExact).map(r=>({name:r.name,previous:r.previousDeficits,current:r.newDeficits,originalResolution:r.originalConvergenceResolution})),
 guardFailures:results.filter(r=>!Object.values(r.guards).every(Boolean)).map(r=>({name:r.name,failed:Object.entries(r.guards).filter(([,v])=>!v).map(([k])=>k)})),
 integrityPassed:integrity.passed,rta:{cases:rta.cases,maximumErrors:rta.maximumErrors},results};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
const reference=read(`${root}/reference-native/summary.json`).map(r=>{
 const input=read(`${root}/reference-inputs/${r.c.Source}.coating.json`),d=read(`${root}/reference-native/${r.Name}.coating.json`),p=results.find(p=>p.name===r.c.Source);
 const od=(input.Target.StopBands??[]).map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 return {name:r.Name,source:r.c.Source,passed:r.Passed,metrics:r.Metrics,od,targetsPreserved:inside(deficits(input.Target,r.Metrics,od),p.startDeficits),iterations:r.c.Iterations,reason:r.c.Reason,seconds:r.c.Seconds};
});
writeFileSync(`${root}/reference-comparison.json`,JSON.stringify({runs:reference.length,passed:reference.filter(r=>r.passed).length,targetRegressions:reference.filter(r=>!r.targetsPreserved).length,
 scope:'Pinned DLS/Newton/SQP point-squared targets; original Core gates reapplied. Different guide and work; no same-objective or equal-total-work ranking.',results:reference},null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined}));
if(!integrity.passed||summary.guardFailures.length)process.exitCode=1;
