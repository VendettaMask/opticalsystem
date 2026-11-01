import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),same=(a,b)=>JSON.stringify(a)===JSON.stringify(b),norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n'),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const arms=['before','control','after'],sum=Object.fromEntries(arms.map(a=>[a,read(`${root}/${a}-cold/summary.json`)]));
const deficits=(t,m,od)=>t.Kind==='Antireflection'?[Math.max(0,m.MaximumReflectance-t.Reflectance)/(t.Reflectance||1)]:t.Kind==='HighReflector'?[Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)]:[
 Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
 Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,...od.map(v=>Math.max(0,t.BlockingOd-v)/Math.max(1,t.BlockingOd))];
const inside=(a,b)=>a.length===b.length&&a.every((v,i)=>v<=b[i]);
const results=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).sort().map(file=>{
 const input=read(`${root}/inputs/${file}`),d=Object.fromEntries(arms.map(a=>[a,read(`${root}/${a}-cold/${file}`)]));
 const r=Object.fromEntries(arms.map(a=>[a,sum[a].find(x=>x.Name===input.Name)])),out=d.after,w=r.after.Run.GeneralWork??{},n=input.Settings.MaximumIterations,window=Math.ceil(Math.sqrt(n));
 const v=Object.fromEntries(arms.map(a=>[a,deficits(input.Target,r[a].AfterMetrics,r[a].AfterBlockingOd)])),start=deficits(input.Target,r.after.BeforeMetrics,r.after.BeforeBlockingOd);
 const raw=(a,b)=>r[a].AfterMetrics.PeakTransmittance>=r[b].AfterMetrics.PeakTransmittance&&Math.abs(r[a].AfterMetrics.FwhmNanometers-input.Target.FwhmNm)<=Math.abs(r[b].AfterMetrics.FwhmNanometers-input.Target.FwhmNm)&&r[a].AfterBlockingOd.every((x,i)=>x>=r[b].AfterBlockingOd[i]);
 const guard={sameInput:arms.every(a=>r[a].InputFingerprint===r.after.InputFingerprint&&same(d[a].Target,input.Target)&&same(d[a].Settings,input.Settings)),
  materialSnapshots:arms.every(a=>same(d[a].Materials,input.Materials)),structure:out.Layers.length===input.Layers.length&&out.Layers.every((l,i)=>l.MaterialId===input.Layers[i].MaterialId&&l.Variable===input.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||same(l,out.Layers[i])),bounds:out.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  mainBudget:r.after.Run.Iterations<=n,comparisonBudget:(w.TargetComparisonIterations??0)<=n&&(w.TargetComparisonRuns??0)<=1,
  sharedNarrowBudget:(w.TargetRepairIterations??0)+(w.TargetParetoPolishIterations??0)+(w.PassbandQualityIterations??0)<=n,
  marginBudget:(w.PassbandQualityIterations??0)<=4*window&&(w.PassbandCorrectionIterations??0)<=2*window&&(w.PassbandQualityIterations??0)-(w.PassbandCorrectionIterations??0)<=2*window,
  reflectanceBudget:(w.ReflectanceMetricIterations??0)<=(w.QualityIterations??0),
  reflectanceMain:input.Target.Kind==='NarrowBand'||(w.QualityIterations??0)+(w.ReflectancePreconditionerIterations??0)===r.after.Run.Iterations,
  inputTargets:inside(v.after,start),previousTargets:inside(v.after,v.before),
  previousPass:!r.before.AfterPassed||r.after.AfterPassed,controlPass:!r.control.AfterPassed||r.after.AfterPassed,
  previousPassedChecks:d.before.Result.Checks.filter(x=>x.Passed).every(c=>out.Result.Checks.find(x=>x.Name===c.Name)?.Passed),
  originalGates:arms.every(a=>same(d[a].Result.Checks.map(c=>[c.Name,c.Requirement]),out.Result.Checks.map(c=>[c.Name,c.Requirement]))),
  verdict:r.after.AfterPassed===(out.Result.SamplingConverged&&out.Result.Checks.every(c=>c.Passed)),
  coldIdentity:arms.every(a=>r[a].IdenticalReportedMetrics&&r[a].IdenticalReportedSpectrum&&r[a].IdenticalReportedChecks),
  previousRawNarrow:input.Target.Kind!=='NarrowBand'||r.before.AfterMetrics.FwhmNanometers===null||raw('after','before'),
  controlRawNarrow:input.Target.Kind!=='NarrowBand'||r.control.AfterMetrics.FwhmNanometers===null||raw('after','control'),
  passbandDirection:(r.after.Run.PassbandDirectionTrials??[]).length===0||(r.after.Run.PassbandDirectionTrials.filter(x=>x.Selected).length===1&&r.after.Run.PassbandDirectionTrials.every(x=>x.Allowance<=window&&x.Iterations<=x.Allowance)),
  marginTrials:(r.after.Run.PassbandQualityTrials??[]).filter(t=>t.Retained).every(t=>t.Metrics.PeakTransmittance>=t.Bounds.MinimumPeakTransmittance&&t.Metrics.FwhmNanometers!==null&&Math.abs(t.Metrics.FwhmNanometers-input.Target.FwhmNm)<=t.Bounds.MaximumWidthErrorNm&&t.OpticalDensities.every((v,i)=>v>=t.Bounds.MinimumOpticalDensities[i])&&inside(deficits(input.Target,t.Metrics,t.OpticalDensities),t.Bounds.ViolationCeilings)),
  metricWindows:(r.after.Run.ReflectanceMetricWindows??[]).every((x,i,all)=>(!x.Accepted||x.CandidateLoss<x.BaselineLoss)&&(!i||x.DampingStart===all[i-1].DampingEnd&&x.BaselineLoss===all[i-1].CandidateLoss&&same(x.StartLayers,all[i-1].CandidateLayers)&&all[i-1].BaselineLoss-all[i-1].CandidateLoss>1e-5))};
 const comparison=a=>({loss:r[a].AfterLoss,passed:r[a].AfterPassed,metrics:r[a].AfterMetrics,od:r[a].AfterBlockingOd,violations:v[a],score:v[a].reduce((s,x)=>s+x*x,0),work:r[a].Run.GeneralWork});
 return{name:input.Name,kind:input.Target.Kind,target:input.Target,before:comparison('before'),control:comparison('control'),after:comparison('after'),guard,
  rawDominatesControl:input.Target.Kind==='NarrowBand'&&r.control.AfterMetrics.FwhmNanometers!==null?raw('after','control'):null};
});
const beforeSource=readdirSync(`${root}/baseline-source`).filter(f=>f.endsWith('.cs')).map(file=>({file,unchanged:norm(`${root}/baseline-source/${file}`)===norm(`validation/rechecks/2026-10-31/coating-margin-refinement/final-source/${file}`)}));
const afterDir='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine';
const allowedControlChanges={'DesignService.NonlinearCorrection.cs':'Disable Core trial correction','DesignService.PassbandQuality.cs':'Old two-window margin direction; final optimizer also compares directions and jointly polishes within shared N','Experiment.cs':'Optional direction-result records'};
const controlSource=readdirSync(`${root}/control-source`).filter(f=>f.endsWith('.cs')&&!allowedControlChanges[f]).map(file=>({file,unchanged:norm(`${root}/control-source/${file}`)===norm(`${afterDir}/${file}`)}));
const controlDifferences=Object.entries(allowedControlChanges).map(([file,reason])=>({file,reason,controlSha256:sha(`${root}/control-source/${file}`),finalSha256:sha(`${afterDir}/${file}`)}));
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const old=norm(`${root}/baseline-source/DesignService.cs`),now=norm(`${afterDir}/DesignService.cs`);
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((s,i)=>({name:s,unchanged:section(old,s,i?null:'private static RequirementCheck[] Checks')===section(now,s,i?null:'private static RequirementCheck[] Checks')}));
const physics=readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')&&f!=='CoherentThinFilmSolver.cs').map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`).concat([`${afterDir}/Data/films.json`,`${afterDir}/MaterialLibrary.cs`]).map(path=>({path,unchanged:norm(path)===execFileSync('git',['show',`3b43bb7f:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const originalInputs=readdirSync('validation/rechecks/2026-10-31/coating-margin-refinement/inputs').filter(f=>f.endsWith('.coating.json')).map(file=>({file,unchanged:sha(`${root}/inputs/${file}`)===sha(`validation/rechecks/2026-10-31/coating-margin-refinement/inputs/${file}`)}));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Debug/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,baseline:sha(`${root}/baseline-runtime/${name}.dll`),control:sha(`${root}/control-runtime/${name}.dll`),after:sha(`${root}/certified-runtime/${name}.dll`),test:sha(`${test}/${name}.dll`)}));
const tf='third_party/coating-reference/TFStudio-8b942b3.tar.gz',tmm='third_party/coating-reference/tmmcore-0.4.2.tgz',prefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
const referenceSources=['src/utils/physics/optimizer/lsqEngine.js','src/utils/physics/optimizer/boundedStep.js','src/utils/optimizers/newton.js','src/utils/optimizers/sqp.js'].map(path=>({path,unchanged:createHash('sha256').update(execFileSync('tar',['-xOf',tf,`${prefix}/${path}`])).digest('hex')===sha(`.tmp/coating-infeasible-repair/${prefix}/${path}`)}));
const referenceIntegrity=sha(tf)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'&&sha(tmm)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'&&referenceSources.every(x=>x.unchanged);
const integrity={beforeSource,controlSource,controlDifferences,gates,physics,originalInputs,binaries,referenceSources,referenceIntegrity,passed:beforeSource.every(x=>x.unchanged)&&controlSource.every(x=>x.unchanged)&&gates.every(x=>x.unchanged)&&physics.every(x=>x.unchanged)&&originalInputs.every(x=>x.unchanged)&&referenceIntegrity&&binaries.every(x=>x.after===x.test&&(x.name.endsWith('.Engine')||x.baseline===x.after&&x.control===x.after))};
const changed=(a,b)=>a<b?'better':a>b?'worse':'same',countComparison=arm=>results.reduce((o,r)=>{const which=changed(r.kind==='NarrowBand'?r.after.score:r.after.loss,r.kind==='NarrowBand'?r[arm].score:r[arm].loss);o[which]++;return o;},{better:0,worse:0,same:0});
const guardFailures=results.flatMap(r=>Object.entries(r.guard).filter(([,v])=>!v).map(([guard])=>({name:r.name,guard})));
const output={cases:results.length,passed:Object.fromEntries(arms.map(a=>[a,results.filter(r=>r[a].passed).length])),newPasses:results.filter(r=>!r.before.passed&&r.after.passed).map(r=>r.name),vsBefore:countComparison('before'),vsControl:countComparison('control'),guardFailures,integrityPassed:integrity.passed,results};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(output,null,2)+'\n');writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
console.log(JSON.stringify({...output,results:undefined}));
