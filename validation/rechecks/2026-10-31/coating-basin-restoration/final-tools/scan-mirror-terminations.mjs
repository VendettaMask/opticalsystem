// Exact-n/k offline comparison of complementary physical terminations, not a product evaluator.
import {readFileSync,writeFileSync} from 'node:fs';
import {tmm} from '../../../.tmp/coating-edge-audit-20261030/package/src/tmm.js';
const rows=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,'')),results=[];
for(const row of rows.filter(r=>r.Input.Target.Kind==='HighReflector'&&r.Input.Layers.every(l=>l.Variable))){
 const d=row.Input,t=d.Target,mats=new Map(row.Materials.map(m=>[m.Id,new Map(m.Values.map(v=>[v[0],v.slice(1)]))])),waves=row.Materials[0].Values.map(v=>v[0]);
 const units=id=>row.Units[d.Layers.findIndex(l=>l.MaterialId===id)],pols=t.Polarization==='Unpolarized'?['s','p']:[t.Polarization.toLowerCase()];let best=null;
 for(const firstHigh of [true,false])for(let h=5;h<=15;h++)for(let l=5;l<=15;l++){
  const layers=d.Layers.map((_,i)=>{const id=(i%2===0)===firstHigh?d.HighId:d.LowId;return{MaterialId:id,ThicknessNm:units(id)*(id===d.HighId?h:l)/10,Variable:true};});
  if(layers.some(x=>x.ThicknessNm<d.Settings.MinimumThicknessNm||x.ThicknessNm>d.Settings.MaximumThicknessNm))continue;
  let loss=0;for(const w of waves){const R=pols.reduce((sum,pol)=>sum+tmm(w,t.AngleDegrees,pol,mats.get(d.IncidentId).get(w),mats.get(d.SubstrateId).get(w),layers.map(f=>({n:mats.get(f.MaterialId).get(w),d:f.ThicknessNm}))).R,0)/pols.length;loss=Math.max(loss,1-R);if(best&&loss>best.loss)break;}
  if(!best||loss<best.loss)best={loss,firstHigh,h:h/10,l:l/10,layers};
 }
 results.push({name:d.Name,scope:'Sparse exploration; actual Core gate not evaluated',best});console.log(JSON.stringify({name:d.Name,best:{loss:best?.loss,firstHigh:best?.firstHigh,h:best?.h,l:best?.l}}));
 writeFileSync(process.argv[3],JSON.stringify(results,null,2)+'\n');
}
