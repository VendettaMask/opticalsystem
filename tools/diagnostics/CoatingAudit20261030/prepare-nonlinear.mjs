import {readFileSync,writeFileSync,readdirSync,mkdirSync,copyFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root=process.argv[2],old='validation/rechecks/2026-10-31/coating-margin-refinement',sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const provenance=[];
for(const file of readdirSync(`${root}/holdout-inputs`).filter(f=>f.endsWith('.coating.json'))){copyFileSync(`${root}/holdout-inputs/${file}`,`${root}/inputs/${file}`);provenance.push({file,sha256:sha(`${root}/inputs/${file}`)});}
mkdirSync(`${root}/before-raw`,{recursive:true});
for(const file of readdirSync(`${old}/inputs`).filter(f=>f.endsWith('.coating.json'))){if(sha(`${old}/inputs/${file}`)!==sha(`${root}/inputs/${file}`))throw Error('Changed original input');copyFileSync(`${old}/final-matrix/after-raw/${file}`,`${root}/before-raw/${file}`);}
const groups=Array.from({length:3},(_,i)=>({id:i,weight:0,files:[]}));
const specs=readdirSync(`${root}/inputs`).filter(f=>f.endsWith('.coating.json')).map(file=>({file,d:JSON.parse(readFileSync(`${root}/inputs/${file}`,'utf8').replace(/^\uFEFF/,''))}));
specs.sort((a,b)=>(b.d.Target.Kind==='HighReflector')-(a.d.Target.Kind==='HighReflector')||a.file.localeCompare(b.file));
for(const s of specs){const g=groups.reduce((a,b)=>a.weight<=b.weight?a:b);g.weight+=s.d.Target.Kind==='HighReflector'?4:s.d.Target.Kind==='NarrowBand'?1:.2;g.files.push(s.file);mkdirSync(`${root}/groups/g${g.id}`,{recursive:true});copyFileSync(`${root}/inputs/${s.file}`,`${root}/groups/g${g.id}/${s.file}`);}
writeFileSync(`${root}/input-provenance.json`,JSON.stringify({frozenAt:new Date().toISOString(),originalInputs:55,novelSpecifications:provenance,groups},null,2)+'\n');
console.log(JSON.stringify({cases:specs.length,groups:groups.map(g=>[g.id,g.files.length,g.weight])}));
