// Offline exploration only: unchanged tmmcore and exact exported n/k, no runtime candidate source.
import {readFileSync,writeFileSync} from 'node:fs';
import {tmm} from '../../../.tmp/coating-edge-audit-20261030/package/src/tmm.js';
const rows=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,'')),results=[];
for(const {Input:d,Materials:mats} of rows.filter(r=>r.Input.Target.Kind==='HighReflector')){
 const waves=mats[0].Values.map(v=>v[0]),t=d.Target;
 const nk=(id,w)=>mats.find(m=>m.Id===id).Values.find(v=>v[0]===w).slice(1);
 let best={R:0};
 for(let h=1;h<=19;h++)for(let l=1;l<=19;l++){
  let R=1;
  for(const w of waves.filter((_,i)=>i%10===0)){
   const films=d.Layers.map(f=>({n:nk(f.MaterialId,w),d:f.ThicknessNm*(f.Variable?(f.MaterialId===d.HighId?h:l)/10:1)}));
   const pol=t.Polarization==='S'?'s':t.Polarization==='P'?'p':'s';
   let r=tmm(w,t.AngleDegrees,pol,nk(d.IncidentId,w),nk(d.SubstrateId,w),films).R;
   if(t.Polarization==='Unpolarized')r=(r+tmm(w,t.AngleDegrees,'p',nk(d.IncidentId,w),nk(d.SubstrateId,w),films).R)/2;
   R=Math.min(R,r);if(R<best.R)break;
  }
  if(R>best.R)best={R,high:h/10,low:l/10};
 }
 results.push({name:d.Name,guide:'Shared phase multiplier by material role; sparse exploration only, not acceptance',best});
 console.log(JSON.stringify(results.at(-1)));
}
writeFileSync(process.argv[3],JSON.stringify(results,null,2)+'\n');
