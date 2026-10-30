// Evidence integrity, not product computation.
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const out=process.argv[2], hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const read=path=>readFileSync(path,'utf8').replace(/\r\n/g,'\n');
const engine='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs';
const old=execFileSync('git',['show',`HEAD:${engine}`],{encoding:'utf8'}).replace(/\r\n/g,'\n'), current=read(engine);
const section=(text,start,end)=>text.slice(text.indexOf(start),end?text.indexOf(end,text.indexOf(start)):text.length);
const gates=['private static bool Converged','private static RequirementCheck[] Checks'].map((start,i)=>({
  name:i?'Checks':'Converged',unchanged:section(old,start,i?null:'private static RequirementCheck[] Checks')===section(current,start,i?null:'private static RequirementCheck[] Checks')
}));
const frozen=[
  ['third_party/coating-reference/TFStudio-8b942b3.tar.gz','055391199312d1ce1a3d30e5640aad1375b58b77ec88f2c3b55cda56d721cbe6'],
  ['third_party/coating-reference/tmmcore-0.4.2.tgz','fc16f4a16149c971178b6a354ecf094dc0511a2bdfa0630c977178c29b262f44']
].map(([path,expected])=>({path,sha256:hash(readFileSync(path)),unchanged:hash(readFileSync(path))===expected}));
const unchanged=['src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs',
  'src/OpticalSystemDesign.Core/Coatings/ThinFilmThicknessSensitivity.cs','validation/coating/tmmcore-0.4.2.json',
  'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.GeneralSearch.cs',
  'labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.GeneralObjective.cs'].map(path=>({path,
    unchanged:execFileSync('git',['show',`HEAD:${path}`],{encoding:'utf8'}).replace(/\r\n/g,'\n')===read(path)}));
const binary=(folder,name)=>hash(readFileSync(`${folder}/${name}.dll`));
const test='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Release/net10.0';
const diag='tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0';
const binaries=['OpticalSystemDesign.Core','OpticalSystemDesign.CoatingDesign.Engine'].map(name=>({name,
  test:binary(test,name),diagnostic:binary(diag,name),identical:binary(test,name)===binary(diag,name)}));
const passed=gates.every(v=>v.unchanged)&&frozen.every(v=>v.unchanged)&&unchanged.every(v=>v.unchanged)&&binaries.every(v=>v.identical);
writeFileSync(out,JSON.stringify({passed,gates,frozen,unchanged,binaries},null,2)+'\n');
console.log(JSON.stringify({passed,gates,binaries}));
if(!passed) process.exitCode=1;
