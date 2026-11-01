// Freeze the five previously reported regressions; never derive targets from a new result.
import {readFileSync,writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root='validation/rechecks/2026-10-31/coating-balanced-targets';
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const cases=['continued-double-96','holdout-violet','new-ir-double','prior-double-96','real-narrowband-control'].map(name=>{
 const path=`${root}/inputs/${name}.coating.json`,input=read(path);
 const old=read(`${root}/final-matrix/before/${name}.coating.json`);
 const prior=read(`${root}/final-matrix/after/${name}.coating.json`);
 const od=d=>d.Target.StopBands.map(b=>Math.min(...d.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 return {Input:input,InputSha256:createHash('sha256').update(readFileSync(path)).digest('hex'),
  Legacy:{Merit:old.Result.Merit,Metrics:old.Result.Metrics,BlockingOd:od(old)},
  Previous:{Merit:prior.Result.Merit,Metrics:prior.Result.Metrics,BlockingOd:od(prior)}};
});
writeFileSync('labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/target-tradeoffs-20261031.json',JSON.stringify({
 Source:'Frozen v1 commit 3b43bb7f and v2 target-balancing paired evidence; original inputs, materials and gates',Cases:cases},null,2)+'\n');
