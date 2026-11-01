// Offline exploration with unmodified tmmcore. The product never reads these candidates.
import {readFileSync,writeFileSync} from 'node:fs';
import {tmm} from '../../../.tmp/coating-edge-audit-20261030/package/src/tmm.js';
const rows=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,'')),results=[];
for(const row of rows.filter(r=>r.Input.Target.Kind==='HighReflector')){
 const d=row.Input,t=d.Target,mats=new Map(row.Materials.map(m=>[m.Id,new Map(m.Values.map(v=>[v[0],v.slice(1)]))]));
 const waves=row.Materials[0].Values.map(v=>v[0]),base=row.Retained.map(l=>l.ThicknessNm);
 const pols=t.Polarization==='Unpolarized'?['s','p']:[t.Polarization.toLowerCase()];
 const loss=values=>Math.max(...waves.map(w=>1-pols.reduce((sum,pol)=>sum+tmm(w,t.AngleDegrees,pol,mats.get(d.IncidentId).get(w),mats.get(d.SubstrateId).get(w),d.Layers.map((l,i)=>({n:mats.get(l.MaterialId).get(w),d:values[i]}))).R,0)/pols.length));
 const initial=loss(base),top=[];
 function add(values,indices){const v=loss(values);if(v<initial){top.push({loss:v,indices,values});top.sort((a,b)=>a.loss-b.loss);if(top.length>12)top.pop();}}
 const values=i=>d.Layers[i].Variable?[...new Set([d.Settings.MinimumThicknessNm,...[-1,-.5,-.25,0,.25,.5,1].map(q=>Math.max(d.Settings.MinimumThicknessNm,Math.min(d.Settings.MaximumThicknessNm,base[i]+q*row.Units[i])))])]:[base[i]];
 for(let i=0;i<base.length;i++)if(d.Layers[i].Variable){for(const value of values(i)){const trial=[...base];trial[i]=value;add(trial,[i]);}}
 for(let i=0;i<base.length-1;i++)if(d.Layers[i].Variable&&d.Layers[i+1].Variable){for(const a of values(i))for(const b of values(i+1)){const trial=[...base];trial[i]=a;trial[i+1]=b;add(trial,[i,i+1]);}}
 results.push({name:d.Name,scope:'Sparse exact-n/k reference exploration only, not acceptance',initialLoss:initial,top});
 console.log(JSON.stringify({name:d.Name,initial,best:top[0]?.loss,indices:top[0]?.indices}));
 writeFileSync(process.argv[3],JSON.stringify(results,null,2)+'\n');
}
