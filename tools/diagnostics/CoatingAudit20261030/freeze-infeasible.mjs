// Freeze declared probes before either implementation runs. Targets and n/k are never edited.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
const root='validation/rechecks/2026-10-30/coating-infeasible-repair';
mkdirSync(`${root}/extended-inputs`,{recursive:true});
const old='validation/rechecks/2026-10-30/coating-general-fixes/final-runtime';
const cases=[
 ['real-hr-96',`${root}/inputs/real-hr-original-target.coating.json`,96],
 ['real-nbf1-96',`${root}/inputs/real-narrowband-control.coating.json`,96],
 ['real-nbf2-96',`${old}/real/real2/general/result.coating.json`,96],
 ...['visible-low-loss','red-low-contrast','near-ir-oblique-p','ir-absorbing-s'].map(n=>[n,`${old}/matrix/${n}/general/result.coating.json`,48])
];
for(const [name,path,iterations] of cases){
 const d=JSON.parse(readFileSync(path,'utf8').replace(/^\uFEFF/,''));
 d.Name=name;d.Settings.MaximumIterations=iterations;d.Settings.DesignProtocolVersion=4;
 d.Result=null;d.Candidates=null;d.Tolerance=null;
 writeFileSync(`${root}/extended-inputs/${name}.coating.json`,JSON.stringify(d,null,2)+'\n');
}
console.log(`Frozen ${cases.length} additional specifications.`);
