// Three engine versions on frozen inputs, one shared physical kernel and original gates.
import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],pkg=process.argv[3];
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=b=>createHash('sha256').update(b).digest('hex'),sha=p=>hash(readFileSync(p));
const same=(a,b)=>JSON.stringify(a)===JSON.stringify(b);
const arms=['legacy-v1','previous-v2','after'];
const summaries=Object.fromEntries(arms.map(a=>[a,read(`${root}/final-matrix/${a}/summary.json`)]));
function deficits(t,m,od){
 if(t.Kind==='Antireflection')return [Math.max(0,m.MaximumReflectance-t.Reflectance)/(t.Reflectance||1)];
 if(t.Kind==='HighReflector')return [Math.max(0,t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance||1)];
 return [Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
  m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
  Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,
  ...od.map(x=>Math.max(0,t.BlockingOd-x)/Math.max(1,t.BlockingOd))];
}
const inside=(a,b)=>a.length===b.length&&a.every((v,i)=>v<=b[i]);
const files=readdirSync(`${root}/inputs`).filter(p=>p.endsWith('.coating.json')).sort();
const pairs=files.map(file=>{
 const input=read(`${root}/inputs/${file}`);
 const records=Object.fromEntries(arms.map(arm=>{
  const s=summaries[arm].find(r=>r.Name===input.Name),d=read(`${root}/final-matrix/${arm}/${file}`);
  if(!s)throw Error(`Missing ${arm} ${input.Name}`);
  return [arm,{summary:s,document:d,deficits:deficits(input.Target,s.AfterMetrics,s.AfterBlockingOd)}];
 }));
 const current=records.after,previous=records['previous-v2'],legacy=records['legacy-v1'],s=current.summary;
 const start=deficits(input.Target,s.BeforeMetrics,s.BeforeBlockingOd);
 const work=s.Run.GeneralWork,run=s.Run;
 const guards={sameInput:arms.every(a=>records[a].summary.InputFingerprint===s.InputFingerprint&&same(records[a].summary.Target,input.Target)&&same(records[a].summary.Settings,input.Settings)),
  material:arms.every(a=>same(records[a].document.Materials,input.Materials)),target:same(current.document.Target,input.Target),settings:same(current.document.Settings,input.Settings),
  structure:current.document.Layers.length===input.Layers.length&&input.Layers.every((l,i)=>l.MaterialId===current.document.Layers[i].MaterialId&&l.Variable===current.document.Layers[i].Variable),
  fixed:input.Layers.every((l,i)=>l.Variable||same(l,current.document.Layers[i])),
  bounds:current.document.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  mainOptimizerBudget:run.Iterations<=input.Settings.MaximumIterations,
  boundedExtraCorrection:(work.TargetRepairIterations??0)<=Math.ceil(Math.sqrt(input.Settings.MaximumIterations)),
  boundedDirectionComparison:(work.TargetComparisonRuns??0)<=1&&(work.TargetComparisonIterations??0)<=input.Settings.MaximumIterations,
  individualInputTargets:inside(current.deficits,start),certified:s.AfterPassed===(current.document.Result.SamplingConverged&&current.document.Result.Checks.every(c=>c.Passed)),
  requirements:arms.every(a=>same(records[a].document.Result.Checks.map(c=>[c.Name,c.Requirement]),current.document.Result.Checks.map(c=>[c.Name,c.Requirement]))),
  passedRetained:arms.every(a=>!records[a].summary.AfterPassed||s.AfterPassed)};
 const compact=a=>{const x=records[a];return {loss:x.summary.AfterLoss,passed:x.summary.AfterPassed,metrics:x.summary.AfterMetrics,od:x.summary.AfterBlockingOd,deficits:x.deficits,run:x.summary.Run,seconds:x.summary.Seconds};};
 return {name:input.Name,kind:input.Target.Kind,inputSha256:sha(`${root}/inputs/${file}`),fingerprint:s.InputFingerprint,startDeficits:start,
  guards,legacyEnvelope:inside(current.deficits,legacy.deficits),legacy:compact('legacy-v1'),previous:compact('previous-v2'),current:compact('after'),
  betterThanLegacy:s.AfterLoss<legacy.summary.AfterLoss,worseThanLegacy:s.AfterLoss>legacy.summary.AfterLoss,
  betterThanPrevious:s.AfterLoss<previous.summary.AfterLoss,worseThanPrevious:s.AfterLoss>previous.summary.AfterLoss,
  improvedFromInput:s.AfterLoss<s.BeforeLoss,checks:s.Checks};
});
if(arms.some(a=>summaries[a].length!==files.length))throw Error('Unexpected arm length');
const regressions=['continued-double-96','holdout-violet','new-ir-double','prior-double-96','real-narrowband-control'].map(n=>pairs.find(p=>p.name===n));
const cases=arms.flatMap(a=>read(`${root}/final-matrix/${a}/core-cases.json`));
writeFileSync(`${root}/final-matrix/core-cases-all.json`,JSON.stringify(cases,null,2)+'\n');
execFileSync(process.execPath,['tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs',pkg,`${root}/final-matrix/core-cases-all.json`,`${root}/final-matrix/tmmcore-comparison.json`],{stdio:'inherit'});
const norm=p=>readFileSync(p,'utf8').replace(/\r\n/g,'\n');
const before=norm(`${root}/baseline-source/DesignService.cs`),now=norm('labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs');
const section=(s,start,end)=>s.slice(s.indexOf(start),end?s.indexOf(end,s.indexOf(start)):s.length);
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((name,i)=>({name,unchanged:section(before,name,i?null:'private static RequirementCheck[] Checks')===section(now,name,i?null:'private static RequirementCheck[] Checks')}));
const paths=[...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(p=>p.endsWith('.cs')).map(p=>`src/OpticalSystemDesign.Core/Coatings/${p}`),
 'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Examples.cs','labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Data/films.json'];
const physics=paths.map(path=>({path,unchanged:norm(path)===execFileSync('git',['show',`3b43bb7f:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')}));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,
 ...Object.fromEntries(arms.map(a=>[a,sha(`${root}/final-matrix/${a}-runtime/${name}.dll`)])),test:sha(`${test}/${name}.dll`)}));
const binaryPassed=binaries.every(b=>b.after===b.test&&(b.name.endsWith('.Engine')?b.after!==b['legacy-v1']&&b.after!==b['previous-v2']:arms.every(a=>b[a]===b.after)));
const tf='third_party/coating-reference/TFStudio-8b942b3.tar.gz',tmm='third_party/coating-reference/tmmcore-0.4.2.tgz',prefix='TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428';
const referenceSources=['src/utils/physics/optimizer/lsqEngine.js','src/utils/physics/optimizer/boundedStep.js','src/utils/optimizers/newton.js','src/utils/physics/optimizer/operandModel.js'].map(path=>({path,
 unchanged:hash(execFileSync('tar',['-xOf',tf,`${prefix}/${path}`]))===sha(`.tmp/coating-infeasible-repair/${prefix}/${path}`)}));
const referencePassed=sha(tf)==='055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'&&sha(tmm)==='fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44'&&referenceSources.every(x=>x.unchanged)
 &&hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`${pkg}/src/tmm.js`)
 &&hash(execFileSync('tar',['-xOf',tmm,'package/src/tmm.js']))===sha(`.tmp/coating-infeasible-repair/${prefix}/node_modules/tmmcore/src/tmm.js`);
const integrity={gates,physics,binaries,binaryPassed,referenceSources,referencePassed};
writeFileSync(`${root}/integrity.json`,JSON.stringify(integrity,null,2)+'\n');
const count=filter=>pairs.filter(filter).length;
const summary={pairs:pairs.length,completedRuns:arms.reduce((n,a)=>n+summaries[a].length,0),
 passed:Object.fromEntries(arms.map(a=>[a,summaries[a].filter(x=>x.AfterPassed).length])),
 inputTargetRegressions:count(p=>!p.guards.individualInputTargets),improvedFromInput:count(p=>p.improvedFromInput),
 betterThanLegacy:count(p=>p.betterThanLegacy),worseThanLegacy:count(p=>p.worseThanLegacy),sameAsLegacy:count(p=>p.legacy.loss===p.current.loss),
 betterThanPrevious:count(p=>p.betterThanPrevious),worseThanPrevious:count(p=>p.worseThanPrevious),sameAsPrevious:count(p=>p.previous.loss===p.current.loss),
 fiveRepaired:regressions.every(p=>!p.worseThanLegacy&&p.legacyEnvelope),regressionNames:regressions.map(p=>p.name),
 extraCorrectionCases:count(p=>(p.current.run.GeneralWork.TargetRepairIterations??0)>0),
 guardFailures:pairs.filter(p=>!Object.values(p.guards).every(Boolean)).map(p=>({name:p.name,guards:p.guards})),
 integrityPassed:gates.every(x=>x.unchanged)&&physics.every(x=>x.unchanged)&&binaryPassed&&referencePassed,
 comparison:read(`${root}/final-matrix/tmmcore-comparison.json`),results:pairs};
writeFileSync(`${root}/paired-results.json`,JSON.stringify(summary,null,2)+'\n');
const refs=read(`${root}/reference-final-native/summary.json`).map(r=>{
 const input=read(`${root}/reference-inputs/${r.c.Source}.coating.json`),p=pairs.find(p=>p.name===r.c.Source);
 const d=read(`${root}/reference-final-native/${r.Name}.coating.json`);
 const od=(input.Target.StopBands??[]).map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 const v=deficits(input.Target,r.Metrics,od);
 return {name:r.Name,source:r.c.Source,passed:r.Passed,targetsPreserved:inside(v,p.startDeficits),deficits:v,startDeficits:p.startDeficits,metrics:r.Metrics,od,
 iterations:r.c.Iterations,reason:r.c.Reason,initialMerit:r.c.InitialMerit,finalMerit:r.c.FinalMerit,seconds:r.c.Seconds};
});
writeFileSync(`${root}/reference-comparison.json`,JSON.stringify({runs:refs.length,passed:refs.filter(r=>r.passed).length,targetRegressions:refs.filter(r=>!r.targetsPreserved).length,
 scope:'Pinned unmodified DLS/Newton, their point squared guide, original native acceptance reapplied; different objective, not a same-work optimizer ranking',results:refs},null,2)+'\n');
console.log(JSON.stringify({...summary,results:undefined,comparison:summary.comparison.maximumErrors}));
if(!summary.integrityPassed||summary.guardFailures.length||!summary.fiveRepaired)process.exitCode=1;
