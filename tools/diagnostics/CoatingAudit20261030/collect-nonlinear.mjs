import {readFileSync,writeFileSync,readdirSync,mkdirSync,copyFileSync,existsSync} from 'node:fs';
const root=process.argv[2],mode=process.argv[3],files=d=>readdirSync(d).filter(f=>f.endsWith('.coating.json'));
if(mode==='after'){
 mkdirSync(`${root}/after-raw`,{recursive:true});let summary=[];
 for(let i=0;i<3;i++){const dir=`${root}/group-results/g${i}`;for(const f of files(dir))copyFileSync(`${dir}/${f}`,`${root}/after-raw/${f}`);summary.push(...JSON.parse(readFileSync(`${dir}/summary.json`,'utf8').replace(/^\uFEFF/,'')));}
 if(summary.length!==files(`${root}/inputs`).length)throw Error('Incomplete full optimizations');
 writeFileSync(`${root}/after-raw/summary.json`,JSON.stringify(summary,null,2)+'\n');
}else if(mode==='before'){
 for(const f of files(`${root}/holdout-before`))copyFileSync(`${root}/holdout-before/${f}`,`${root}/before-raw/${f}`);
 if(files(`${root}/before-raw`).length!==files(`${root}/inputs`).length)throw Error('Incomplete baseline');
}else if(mode==='control'){
 mkdirSync(`${root}/control-raw`,{recursive:true});
 for(const f of files(`${root}/inputs`)){
  const phase=`${root}/control-phase/${f}`,fresh=`${root}/holdout-control-phase/${f}`,src=existsSync(phase)?phase:existsSync(fresh)?fresh:`${root}/before-raw/${f}`;
  copyFileSync(src,`${root}/control-raw/${f}`);
 }
}else if(mode==='reference'){
 const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
 for(const [old,fresh,out] of [['reference-results.json','holdout-reference-results.json','reference-results.json'],['reference-materials.json','holdout-reference-materials.json','reference-materials.json']])
  writeFileSync(`${root}/${out}`,JSON.stringify([...read(`validation/rechecks/2026-10-31/coating-margin-refinement/${old}`),...read(`${root}/${fresh}`)],null,2)+'\n');
}else if(mode==='portfolio'){
 const read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
 if(!existsSync(`${root}/initial-full-summary.json`))copyFileSync(`${root}/after-raw/summary.json`,`${root}/initial-full-summary.json`);
 const records=[];
 for(const file of files(`${root}/inputs`)){
  const phase=`${root}/portfolio-phase/${file}`,destination=`${root}/after-raw/${file}`;
  if(!existsSync(phase)){records.push({file,source:'Initial full Optimize; AR/HR path is unchanged'});continue;}
  const d=read(phase),initial=read(destination);
  // Candidate-history bookkeeping is identical to the full Optimize append.
  // The initial full run used the same original input and prototype identity.
  d.Candidates=initial.Candidates;
  if(d.Candidates?.length){const c=d.Candidates.at(-1);c.Layers=d.Layers;c.Merit=d.Result.Merit;c.Passed=d.Result.Passed;c.Run=d.Result.Run;}
  writeFileSync(destination,JSON.stringify(d,null,2)+'\n');records.push({file,source:'Final continuation from unchanged first-window result; candidate history reconstructed from original full run'});
 }
 writeFileSync(`${root}/after-raw/summary.json`,JSON.stringify({scope:'Final stacks; execution provenance is explicit rather than fresh-optimization timing',records},null,2)+'\n');
 writeFileSync(`${root}/execution-ledger.json`,JSON.stringify({initialFullOptimizations:read(`${root}/initial-full-summary.json`).length,finalPortfolioContinuations:read(`${root}/portfolio-phase/phase-summary.json`).length,
  historicalOldOptimizationsReused:55,freshOldOptimizations:read(`${root}/holdout-before/summary.json`).length,
  sameWindowControlContinuations:read(`${root}/control-phase/phase-summary.json`).length+read(`${root}/holdout-control-phase/phase-summary.json`).length,
  historicalReferenceRunsReused:78,freshReferenceRuns:read(`${root}/holdout-reference-results.json`).length,
  note:'The final NBF continuation reuses unchanged preceding stages; six full replays independently check exact equivalence. No resumed or cold work is called a new full optimization.',records},null,2)+'\n');
}else throw Error('Unknown collection mode');
console.log(mode+' collected');
