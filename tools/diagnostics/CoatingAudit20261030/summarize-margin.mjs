import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],pkg=process.argv[3],arms=['before','budget-only','after'];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n'),same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const summaries=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/summary.json`)]));
const raw=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}-raw/summary.json`)]));
const freshNames=new Set(read(`${root}/input-provenance.json`).records.map(r=>r.name));
const deficits=(t,m,od)=>t.Kind==='Antireflection'?[Math.max(0,m.MaximumReflectance-t.Reflectance)/(t.Reflectance||1)]:t.Kind==='HighReflector'?[Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)]:[
 Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
 Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,...od.map(v=>Math.max(0,t.BlockingOd-v)/Math.max(1,t.BlockingOd))];
const resolution=t=>t.Kind==='Antireflection'?[1e-5/(t.Reflectance||1)]:t.Kind==='HighReflector'?[1e-5/(1-t.Reflectance||1)]:[
 1e-5/t.PeakTransmittance,Math.min(.02,t.FwhmToleranceNm/10)/t.FwhmNm,Math.min(.02,t.CenterToleranceNm/10)/t.FwhmNm,...(t.StopBands??[]).map(()=>.005/Math.max(1,t.BlockingOd))];
const inside=(a,b)=>a.length===b.length&&a.every((v,i)=>v<=b[i]);
const files=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).sort();
const results=files.map(file=>{
 const input=read(`${root}/inputs/${file}`),rows=Object.fromEntries(arms.map(a=>[a,summaries[a].find(x=>x.Name===input.Name)]));
 const docs=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/${file}`)]));
 if(arms.some(a=>!rows[a]))throw Error('Missing case '+file);
 const r=rows.after,d=docs.after,w=r.Run.GeneralWork,n=input.Settings.MaximumIterations,window=Math.ceil(Math.sqrt(n));
 const previous=deficits(input.Target,rows.before.AfterMetrics,rows.before.AfterBlockingOd),current=deficits(input.Target,r.AfterMetrics,r.AfterBlockingOd),start=deficits(input.Target,r.BeforeMetrics,r.BeforeBlockingOd);
 const kept=(r.Run.PassbandQualityTrials??[]).filter(t=>t.Retained);
 const guard={sameInput:arms.every(a=>rows[a].InputFingerprint===r.InputFingerprint&&same(rows[a].Target,input.Target)&&same(rows[a].Settings,input.Settings)),
  sameMaterials:arms.every(a=>same(docs[a].Materials,input.Materials)),sameStructure:d.Layers.length===input.Layers.length&&d.Layers.every((l,i)=>l.MaterialId===input.Layers[i].MaterialId&&l.Variable===input.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||same(l,d.Layers[i])),bounds:d.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  mainBudget:r.Run.Iterations<=n,comparisonBudget:(w.TargetComparisonIterations??0)<=n&&(w.TargetComparisonRuns??0)<=1,
  sharedNarrowBandBudget:(w.TargetRepairIterations??0)+(w.TargetParetoPolishIterations??0)+(w.PassbandQualityIterations??0)<=n,
  marginBudget:(w.PassbandQualityIterations??0)<=window,reflectanceMainBudget:input.Target.Kind==='NarrowBand'||(w.QualityIterations??0)+(w.ReflectancePreconditionerIterations??0)===r.Run.Iterations,
  metricBudget:(w.ReflectanceMetricIterations??0)<=(w.QualityIterations??0),inputTargets:inside(current,start),
  previousTargetsWithinOriginalResolution:current.every((v,i)=>v<=previous[i]+resolution(input.Target)[i]),
  previousPassedChecks:docs.before.Result.Checks.filter(c=>c.Passed).every(c=>d.Result.Checks.find(x=>x.Name===c.Name)?.Passed),
  passedRetained:!rows.before.AfterPassed||r.AfterPassed,verdict:r.AfterPassed===(d.Result.SamplingConverged&&d.Result.Checks.every(c=>c.Passed)),
  gates:arms.every(a=>same(docs[a].Result.Checks.map(c=>[c.Name,c.Requirement]),d.Result.Checks.map(c=>[c.Name,c.Requirement]))),
  marginTrials:kept.every(t=>t.Metrics.PeakTransmittance>=t.Bounds.MinimumPeakTransmittance&&t.Metrics.FwhmNanometers!==null
   &&Math.abs(t.Metrics.FwhmNanometers-input.Target.FwhmNm)<=t.Bounds.MaximumWidthErrorNm&&t.OpticalDensities.every((v,i)=>v>=t.Bounds.MinimumOpticalDensities[i])),
  restorationTrials:(r.Run.TargetRepairTrials??[]).filter(t=>t.Retained).every(t=>inside(t.Violations,t.ViolationCeilings)),
  metricWindows:(r.Run.ReflectanceMetricWindows??[]).every((x,i,all)=>(!x.Accepted||x.CandidateLoss<x.BaselineLoss)&&(!i||x.DampingStart===all[i-1].DampingEnd))};
 const compact=a=>({loss:rows[a].AfterLoss,passed:rows[a].AfterPassed,metrics:rows[a].AfterMetrics,od:rows[a].AfterBlockingOd,run:rows[a].Run,
  optimizationSeconds:raw[a].find(x=>x.Name===input.Name)?.Seconds??null,coldSeconds:rows[a].Seconds,
  originalReportedPassed:rows[a].OriginalReportedPassed,originalReportedMetrics:rows[a].OriginalReportedMetrics});
 const physicalMargins=key=>{
  if(input.Target.Kind!=='NarrowBand')return null;
  const a=rows.after,b=rows[key],am=a.AfterMetrics,bm=b.AfterMetrics;
  const widthA=am.FwhmNanometers===null?Infinity:Math.abs(am.FwhmNanometers-input.Target.FwhmNm),widthB=bm.FwhmNanometers===null?Infinity:Math.abs(bm.FwhmNanometers-input.Target.FwhmNm);
  const rawDominates=am.PeakTransmittance>=bm.PeakTransmittance&&widthA<=widthB&&a.AfterBlockingOd.every((v,i)=>v>=b.AfterBlockingOd[i]);
  const resolvedGain=am.PeakTransmittance-bm.PeakTransmittance>1e-5||widthB-widthA>Math.min(.02,input.Target.FwhmToleranceNm/10)||a.AfterBlockingOd.some((v,i)=>v-b.AfterBlockingOd[i]>.005);
  return {rawDominates,resolvedGain,peakChange:am.PeakTransmittance-bm.PeakTransmittance,widthErrorChange:widthA-widthB,odChanges:a.AfterBlockingOd.map((v,i)=>v-b.AfterBlockingOd[i])};
 };
 return {name:input.Name,kind:input.Target.Kind,newSpecification:freshNames.has(input.Name),
  fingerprint:r.InputFingerprint,inputSha256:sha(`${root}/inputs/${file}`),guards:guard,previousTargetsExact:inside(current,previous),
  startDeficits:start,previousDeficits:previous,newDeficits:current,originalResolution:resolution(input.Target),
  marginsVsBefore:physicalMargins('before'),marginsVsBudgetOnly:physicalMargins('budget-only'),
  before:compact('before'),budgetOnly:compact('budget-only'),after:compact('after')};
});
if(arms.some(a=>summaries[a].length!==files.length))throw Error('Incomplete arm');
const comparison=(key,items=results)=>({better:items.filter(r=>r.after.loss<r[key].loss).length,worse:items.filter(r=>r.after.loss>r[key].loss).length,same:items.filter(r=>r.after.loss===r[key].loss).length,
 worseCases:items.filter(r=>r.after.loss>r[key].loss).map(r=>r.name)});
const cases=arms.flatMap(a=>read(`${root}/final-matrix/${a}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(cases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',pkg,`${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const old=norm(`${root}/baseline-source/DesignService.cs`),now=norm('labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs');
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((s,i)=>({name:s,unchanged:section(old,s,i?null:'private static RequirementCheck[] Checks')===section(now,s,i?null:'private static RequirementCheck[] Checks')}));
const physics=[...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/MaterialLibrary.cs'].map(path=>({path,
 unchanged:norm(path)===execFileSync('git',['show',`3b43bb7f:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Debug/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,
 ...Object.fromEntries(arms.map(a=>[a,sha(`${root}/final-matrix/${a}-runtime/${name}.dll`)])),test:sha(`${test}/${name}.dll`),cold:sha(`${root}/final-matrix/evaluator-runtime/${name}.dll`)}));
const controlSource=readdirSync(`${root}/control-source`).filter(f=>f.endsWith('.cs')).map(f=>({file:f,unchanged:norm(`${root}/control-source/${f}`)===norm(`validation/rechecks/2026-10-31/coating-progress-allocation/control-source/${f}`)}));
const tf='third_party/coating-reference/TFStudio-8b942b3.tar.gz',tmm='third_party/coating-reference/tmmcore-0.4.2.tgz',prefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
const refs=['src/utils/physics/optimizer/lsqEngine.js','src/utils/physics/optimizer/boundedStep.js','src/utils/optimizers/newton.js','src/utils/optimizers/sqp.js'].map(path=>({path,
 unchanged:createHash('sha256').update(execFileSync('tar',['-xOf',tf,`${prefix}/${path}`])).digest('hex')===sha(`.tmp/coating-infeasible-repair/${prefix}/${path}`)}));
const refPassed=sha(tf)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'&&sha(tmm)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'&&refs.every(r=>r.unchanged);
const integrity={gates,physics,binaries,controlSource,referenceSources:refs,referencePassed:refPassed,
 passed:gates.every(x=>x.unchanged)&&physics.every(x=>x.unchanged)&&controlSource.every(x=>x.unchanged)&&refPassed
 &&binaries.every(x=>x.cold===x.test&&(x.name.endsWith('.Engine')?x.after===x.test:x.before===x.after&&x['budget-only']===x.after))
 &&summaries.after.every(x=>x.IdenticalReportedMetrics&&x.IdenticalReportedSpectrum&&x.IdenticalReportedChecks)};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const rta=read(`${root}/final-matrix/tmmcore-comparison.json`),fresh=results.filter(r=>r.newSpecification);
const summary={cases:results.length,freshOptimizations:results.length+2*fresh.length,historicalOptimizationsReused:2*(results.length-fresh.length),
 coldStacks:3*results.length,coldEvaluationCalls:6*results.length,passed:Object.fromEntries(arms.map(a=>[a,summaries[a].filter(x=>x.AfterPassed).length])),
 vsBefore:comparison('before'),vsBudgetOnly:comparison('budgetOnly'),newSpecifications:{cases:fresh.length,vsBefore:comparison('before',fresh),vsBudgetOnly:comparison('budgetOnly',fresh)},
 physicallyDominantNarrowBands:results.filter(r=>r.marginsVsBefore?.rawDominates&&r.marginsVsBefore?.resolvedGain).map(r=>r.name),
 exactPreviousTargetRegressions:results.filter(r=>!r.previousTargetsExact).map(r=>({name:r.name,previous:r.previousDeficits,current:r.newDeficits,resolution:r.originalResolution})),
 guardFailures:results.filter(r=>!Object.values(r.guards).every(Boolean)).map(r=>({name:r.name,failed:Object.entries(r.guards).filter(([,v])=>!v).map(([k])=>k)})),
 integrityPassed:integrity.passed,rta:{cases:rta.cases,maximumErrors:rta.maximumErrors},results};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
const reference=read(`${root}/reference-native/summary.json`).map(r=>{
 const d=read(`${root}/reference-native/${r.Name}.coating.json`),p=results.find(x=>x.name===r.c.Source),t=d.Target;
 const od=(t.StopBands??[]).map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 return {name:r.Name,source:r.c.Source,passed:r.Passed,metrics:r.Metrics,od,targetsPreserved:inside(deficits(t,r.Metrics,od),p.startDeficits),iterations:r.c.Iterations,seconds:r.c.Seconds};
});
writeFileSync(`${root}/reference-comparison.json`,JSON.stringify({runs:reference.length,passed:reference.filter(r=>r.passed).length,targetRegressions:reference.filter(r=>!r.targetsPreserved).length,
 scope:'Unmodified pinned DLS/Newton/SQP use point squared targets; formal measured-band/minmax goals differ. Original Core gates reapplied; no equal-objective or equal-total-cost ranking.',results:reference},null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined}));
if(summary.guardFailures.length||!integrity.passed)process.exitCode=1;
