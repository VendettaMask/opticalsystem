import {readFileSync,writeFileSync,readdirSync,mkdirSync,copyFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root=process.argv[2],prior='validation/rechecks/2026-10-31/coating-progress-allocation';
const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
for(const [arm,oldArm,fresh] of [['before','after-raw','new-before-raw'],['budget-only','budget-only-raw','new-budget-only-raw']]){
 const output=`${root}/final-matrix/${arm}-raw`;mkdirSync(output,{recursive:true});
 const old=read(`${prior}/final-matrix/${oldArm}/summary.json`),added=read(`${root}/${fresh}/summary.json`);
 if(old.length!==47||added.length!==8)throw Error('Incomplete baseline scope');
 for(const dir of [`${prior}/final-matrix/${oldArm}`,`${root}/${fresh}`])for(const f of readdirSync(dir).filter(f=>f.endsWith('.coating.json')))copyFileSync(`${dir}/${f}`,`${output}/${f}`);
 writeFileSync(`${output}/summary.json`,JSON.stringify([...old.map(r=>({...r,OptimizationProvenance:`Frozen prior ${oldArm}; optimizer not rerun this task`})),
  ...added.map(r=>({...r,OptimizationProvenance:'New pre-frozen specification; freshly optimized this task'}))].sort((a,b)=>a.Name.localeCompare(b.Name)),null,2)+'\n');
}
const sourceMap={
 'new-real-nbf-green':'real-nbf2-96','new-real-nbf-red':'real-nbf1-96','new-real-nbf-nearir-fixed':'real-nbf2-96','new-real-nbf-ir':'real-nbf1-96',
 'new-real-hr-green':'real-hr-96','new-real-hr-ir-fixed':'holdout-real-hr-red','new-synthetic-green':'fresh-blue-fixed','new-synthetic-ir-fixed':'fresh-nearir-double'};
const records=Object.entries(sourceMap).map(([name,source])=>{
 const d=read(`${root}/holdout-inputs/${name}.coating.json`),s=read(`${prior}/inputs/${source}.coating.json`);
 return {name,source,inputSha256:sha(`${root}/holdout-inputs/${name}.coating.json`),sourceSha256:sha(`${prior}/inputs/${source}.coating.json`),
  materialSnapshotsUnchanged:JSON.stringify(d.Materials)===JSON.stringify(s.Materials),target:d.Target,settings:d.Settings,
  generation:'Same n/k snapshots; Shared Core characteristic phase units map initial thicknesses. New wavelength, angle, polarization and declared fixed films; matching bounds frozen before optimizer outputs.'};
});
if(records.some(r=>!r.materialSnapshotsUnchanged))throw Error('n/k snapshot changed during input generation');
writeFileSync(`${root}/input-provenance.json`,JSON.stringify({existingCases:47,newCases:8,records},null,2)+'\n');
console.log('Prepared 55 matched raw baseline/control stacks; 47 historical and 8 fresh per arm.');
