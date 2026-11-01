import {readFileSync,writeFileSync,readdirSync,statSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root=process.argv[2],sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const files=[];
function visit(directory,relative=''){
 for(const e of readdirSync(directory,{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name))){
  const r=relative?`${relative}/${e.name}`:e.name,p=`${directory}/${e.name}`;
  if(e.isDirectory()){if(!e.name.endsWith('-runtime'))visit(p,r);}
  else if(e.name!=='final-manifest.json')files.push({path:r,bytes:statSync(p).size,sha256:sha(p)});
 }
}
visit(root);
const manifest={scope:'Current task evidence only; ignored runtimes have separate verification.json binary identities',files:files.length,totalBytes:files.reduce((s,x)=>s+x.bytes,0),entries:files};
writeFileSync(`${root}/final-manifest.json`,JSON.stringify(manifest,null,2)+'\n');
const reopened=JSON.parse(readFileSync(`${root}/final-manifest.json`,'utf8'));
const mismatches=reopened.entries.filter(e=>sha(`${root}/${e.path}`)!==e.sha256);
if(mismatches.length)throw Error('Manifest readback mismatch');
console.log(JSON.stringify({files:files.length,bytes:manifest.totalBytes,readbackMismatches:mismatches.length}));
