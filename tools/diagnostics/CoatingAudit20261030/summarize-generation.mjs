// Offline evidence only. No material-specific optimizer decisions are made here.
import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const same=(a,b)=>JSON.stringify(a)===JSON.stringify(b),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const deficit=(t,m,od)=>[
 Math.max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
 m.FwhmNanometers===null?1:Math.max(0,Math.abs(m.FwhmNanometers-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm,
 Math.max(0,Math.abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,
 ...od.map(x=>Math.max(0,t.BlockingOd-x)/Math.max(1,t.BlockingOd))];
const od=d=>(d.Target.StopBands??[]).map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
const rows=[];
for(const file of readdirSync(`${root}/reference-inputs`).filter(f=>f.endsWith('.coating.json')).sort()){
 const input=read(`${root}/reference-inputs/${file}`),before=read(`${root}/baseline-generate-cold/${file}`),after=read(`${root}/final-generate-cold/${file}`);
 const base=read(`${root}/baseline-generate-all/${file}`),fresh=read(`${root}/final-generate-all/${file}`),r=after.Result,run=r.Run;
 const beforeOd=od(before),afterOd=od(after),inputStack=read(`validation/rechecks/2026-10-31/coating-nonlinear-correction/after-cold/${file}`);
 const v0=deficit(input.Target,inputStack.Result.Metrics,od(inputStack)),v=deficit(input.Target,r.Metrics,afterOd);
 const validations=run.SearchSeedValidations??[],refinements=validations.filter(x=>x.RefinementRun),n=input.Settings.MaximumIterations;
 const children=[...run.Screening.map(x=>x.Run),...run.NarrowBandCompetition.MatchingTrials.map(x=>x.Run),...refinements.map(x=>x.RefinementRun)];
 const guards={
  unchangedInputs:[before,after].every(d=>same(d.Target,input.Target)&&same(d.Settings,input.Settings)&&same(d.Materials,input.Materials)),
  unchangedGateRequirements:same(before.Result.Checks.map(x=>[x.Name,x.Requirement]),r.Checks.map(x=>[x.Name,x.Requirement])),
  originalTargetEnvelope:v.every((x,i)=>x<=v0[i]),
  preservedConvergence:!inputStack.Result.SamplingConverged||r.SamplingConverged,
  preservedPassed:!before.Result.Passed||r.Passed,
  physicalVerdict:r.Passed===(r.SamplingConverged&&r.Checks.every(x=>x.Passed)),
  coldIdentical:[ [base,before],[fresh,after] ].every(([a,b])=>same(a.Result.Metrics,b.Result.Metrics)&&same(a.Result.Spectrum,b.Result.Spectrum)&&same(a.Result.Checks,b.Result.Checks)),
  bounds:after.Layers.length<=input.Target.MaximumLayers&&after.Layers.every(l=>l.ThicknessNm>=input.Settings.MinimumThicknessNm&&l.ThicknessNm<=input.Settings.MaximumThicknessNm),
  fixedLayers:input.Layers.every((l,i)=>l.Variable||same(l,after.Layers[i])),
  shortlistBudget:validations.length<=12&&refinements.length<=Math.min(12,input.Settings.Search?.Starts??6),
  refinementBudget:refinements.every(x=>x.RefinementRun.Iterations<=Math.min(n,Math.max(1,Math.ceil(Math.sqrt(n))))),
  evaluationsAccounted:run.Evaluations===children.reduce((s,x)=>s+x.Evaluations,0),
  iterationsAccounted:run.Iterations===children.reduce((s,x)=>s+x.Iterations,0),
  rankingIdentity:run.SeedRankingProtocol==='dense-feasibility-allocation/1'
 };
 const arm=d=>({passed:d.Result.Passed,converged:d.Result.SamplingConverged,metrics:d.Result.Metrics,od:od(d),layers:d.Layers.length,checks:d.Result.Checks,
  evaluations:d.Result.Run.Evaluations,iterations:d.Result.Run.Iterations,work:d.Result.Run.GeneralWork});
 rows.push({name:input.Name,target:input.Target,settings:input.Settings,before:arm(before),after:arm(after),guards,
  rawJointImprovement:r.Metrics.PeakTransmittance>=before.Result.Metrics.PeakTransmittance&&Math.abs(r.Metrics.FwhmNanometers-input.Target.FwhmNm)<=Math.abs(before.Result.Metrics.FwhmNanometers-input.Target.FwhmNm)&&afterOd.every((x,i)=>x>=beforeOd[i]),
  shortlist:validations.length,refinements:refinements.length,inputSha256:sha(`${root}/reference-inputs/${file}`)});
}
const failures=rows.flatMap(r=>Object.entries(r.guards).filter(([,v])=>!v).map(([guard])=>({name:r.name,guard})));
const reference=read(`${root}/reference-native/summary.json`);
const hr=read(`${root}/hr-cold/summary.json`);
const result={scope:'Same warm starts, real n/k and original complete gates. Generate only; Search path retained unchanged.',cases:rows.length,
 beforePassed:rows.filter(x=>x.before.passed).length,afterPassed:rows.filter(x=>x.after.passed).length,newlyPassed:rows.filter(x=>!x.before.passed&&x.after.passed).map(x=>x.name),
 guardFailures:failures,reference:{runs:reference.length,passed:reference.filter(x=>x.Passed).length,scope:'Pinned unmodified TFStudio DLS/Newton/SQP point operands; different objective, not equal-cost or equal-objective superiority'},
 highReflector:{cases:hr.length,passed:hr.filter(x=>x.AfterPassed).length,unchanged:hr.every(x=>x.IdenticalReportedMetrics&&x.IdenticalReportedSpectrum&&x.IdenticalReportedChecks),newlyPassed:[],remaining:hr.filter(x=>!x.AfterPassed).map(x=>({name:x.Name,reflectance:x.AfterMetrics.MinimumReflectance,target:x.Target.Reflectance}))},results:rows};
writeFileSync(`${root}/paired-generation.json`,JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify({cases:result.cases,beforePassed:result.beforePassed,afterPassed:result.afterPassed,newlyPassed:result.newlyPassed,guardFailures:failures,highReflector:result.highReflector}));
if(failures.length||!result.highReflector.unchanged)process.exitCode=1;
