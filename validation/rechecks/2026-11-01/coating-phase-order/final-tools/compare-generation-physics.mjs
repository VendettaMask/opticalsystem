// Unmodified, locally unpacked pinned tmmcore; offline evidence only.
import {tmm} from '../../../.tmp/coating-edge-audit-20261030/package/src/tmm.js';
import {readFileSync,writeFileSync} from 'node:fs';
const points=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,''));
const results=points.map(c=>{
 const run=pol=>tmm(c.Lambda,c.Angle,pol,c.N0,c.Ns,c.Layers.map(l=>({n:l.N,d:l.D})));
 const expected=c.Pol==='Unpolarized'?(()=>{const s=run('s'),p=run('p');return {R:(s.R+p.R)/2,T:(s.T+p.T)/2,A:(s.A+p.A)/2};})():run(c.Pol.toLowerCase());
 return {arm:c.Arm,name:c.Name,wavelength:c.Lambda,pol:c.Pol,expected,core:c.Core,errors:{R:Math.abs(c.Core.Reflectance-expected.R),T:Math.abs(c.Core.Transmittance-expected.T),A:Math.abs(c.Core.Absorptance-expected.A)}};
});
const maximumErrors=Object.fromEntries(['R','T','A'].map(k=>[k,Math.max(...results.map(x=>x.errors[k]))]));
writeFileSync(process.argv[3],JSON.stringify({source:'tmmcore 0.4.2 pinned JavaScript',cases:results.length,maximumErrors,results},null,2)+'\n');
console.log(JSON.stringify({cases:results.length,maximumErrors}));
if(Object.values(maximumErrors).some(x=>!Number.isFinite(x)||x>2e-11))process.exitCode=1;
