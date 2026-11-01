import {readFileSync,writeFileSync} from 'node:fs';
import {tmmThicknessHessian} from '../../../.tmp/coating-edge-audit-20261030/package/src/tmm.js';
const input=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,'')),results=[];
for(const row of input)for(let k=0;k<row.Points.length;k++){
 const p=row.Points[k],run=pol=>tmmThicknessHessian(p.Wavelength,p.Angle,pol,p.N0,p.Ns,p.Layers.map(f=>({n:f.N,d:f.D})));
 const values=p.Polarization==='Unpolarized'?[run('s'),run('p')]:[run(p.Polarization.toLowerCase())];let maximumError=0;
 for(let i=0;i<row.Indices.length;i++)for(let j=0;j<row.Indices.length;j++){
  const expected=-values.reduce((s,v)=>s+v.d2Rdd[row.Indices[i]][row.Indices[j]],0)/values.length*row.Units[i]*row.Units[j];
  maximumError=Math.max(maximumError,Math.abs(expected-row.Hessians[k][i][j]));
 }
 results.push({name:row.Name,wavelength:p.Wavelength,maximumError});
}
const maximumError=Math.max(...results.map(x=>x.maximumError));
writeFileSync(process.argv[3],JSON.stringify({scope:'Offline central differences of Core analytic gradients versus unmodified tmmcore analytic Hessian; diagnostic, not a new acceptance tolerance',points:results.length,maximumError,results},null,2)+'\n');
console.log(JSON.stringify({points:results.length,maximumError}));
