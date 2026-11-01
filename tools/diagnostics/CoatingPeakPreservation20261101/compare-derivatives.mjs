import {readFileSync,writeFileSync} from 'node:fs';
import {tmmThicknessJacobian} from '../../../.tmp/coating-edge-audit-20261030/package/src/tmm.js';
const points=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,''));
const records=points.map(c=>{
 const run=pol=>tmmThicknessJacobian(c.Lambda,c.Angle,pol,c.N0,c.Ns,c.Layers.map(l=>({n:l.N,d:l.D})));
 let expected;
 if(c.Pol==='Unpolarized'){
  const s=run('s'),p=run('p');expected=Object.fromEntries(['R','T','A'].map(k=>[k,(s[k]+p[k])/2]));
  for(const key of ['dRdd','dTdd','dAdd'])expected[key]=s[key].map((v,i)=>(v+p[key][i])/2);
 }else expected=run(c.Pol.toLowerCase());
 const errors={R:Math.abs(c.Core.Reflectance-expected.R),T:Math.abs(c.Core.Transmittance-expected.T),A:Math.abs(c.Core.Absorptance-expected.A)};
 for(const [key,field] of [['dRdd','Reflectance'],['dTdd','Transmittance'],['dAdd','Absorptance']])errors[key]=Math.max(...c.Derivatives.map((d,i)=>Math.abs(d[field]-expected[key][i])));
 return {file:c.File,wave:c.Lambda,pol:c.Pol,errors};
});
const maximumErrors=Object.fromEntries(['R','T','A','dRdd','dTdd','dAdd'].map(k=>[k,Math.max(...records.map(r=>r.errors[k]))]));
writeFileSync(process.argv[3],JSON.stringify({source:'Unmodified pinned tmmcore 0.4.2 thickness Jacobian',points:records.length,tolerance:2e-11,maximumErrors,records},null,2)+'\n');
console.log(JSON.stringify({points:records.length,maximumErrors}));
if(Object.values(maximumErrors).some(v=>!Number.isFinite(v)||v>2e-11))process.exitCode=1;
