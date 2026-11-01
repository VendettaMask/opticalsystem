import {readFileSync,writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root='validation/rechecks/2026-10-31/coating-tradeoff-repair';
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const cases=['prior-double-96','holdout-violet','continued-double-96','real-nbf2-96','holdout-real-hr-nearir','holdout-real-hr-red'].map(name=>{
 const path=`${root}/inputs/${name}.coating.json`,input=read(path),old=read(`${root}/final-matrix/after/${name}.coating.json`);
 const od=(input.Target.StopBands??[]).map(b=>Math.min(...old.Result.Spectrum.filter(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).map(s=>s.Power.OpticalDensity)));
 return {Input:input,InputSha256:createHash('sha256').update(readFileSync(path)).digest('hex'),
  Previous:{Merit:old.Result.Merit,Metrics:old.Result.Metrics,BlockingOd:od,Passed:old.Result.Passed}};
});
writeFileSync('labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/target-continuation-20261031.json',JSON.stringify({Source:'Frozen version 3/repair 1 results, original material snapshots and acceptance gates',Cases:cases},null,2)+'\n');
