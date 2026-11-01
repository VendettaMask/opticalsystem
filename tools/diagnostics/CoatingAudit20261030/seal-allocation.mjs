import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2], read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const result=read(`${root}/paired-results.json`),tests=read(`${root}/verification-counts.json`),integrity=read(`${root}/integrity.json`);
if(!integrity.passed||result.guardFailures.length||tests.passed!==tests.total||tests.failed||tests.notExecuted)throw Error('Cannot seal incomplete verification');
const files=[];
function walk(path){for(const e of readdirSync(path,{withFileTypes:true})){
 const p=`${path}/${e.name}`;
 if(e.isDirectory()){if(!e.name.endsWith('-runtime')&&!['bin','obj','final2-matrix'].includes(e.name))walk(p);}
 else if(e.name!=='final-manifest.json')files.push(p);
}}
walk(root);
for(const dir of ['labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine','labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests','tools/diagnostics/CoatingAudit20261030']){
 for(const e of readdirSync(dir,{withFileTypes:true}))if(e.isFile()&&/\.(cs|csproj|mjs)$/.test(e.name))files.push(`${dir}/${e.name}`);
}
files.push(...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 'docs/COATING_PROGRESS_ALLOCATION_2026-10-31.md','docs/COATING_DESIGN_LAB.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CURRENT_STATUS.md',
 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/progress-allocation-20261031.json');
files.push('src/OpticalSystemDesign.Core/Optimization/InequalityLeastSquaresStep.cs','labs/CoatingDesign/README.md');
const sources=[...new Set(files)].sort().map(path=>({path,bytes:readFileSync(path).length,sha256:sha(path)}));
const manifest={date:'2026-10-31',sealedAt:new Date().toISOString(),baseCommit:execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),
 scope:'Progress-conditioned recovery, independent search/formal sampling and bounded spectral-metric reflectance refinement',
 protocols:{design:'general-constraint-coating/4',restoration:'pareto-target-correction/3',quality:'worst-reflectance/6',sampling:'anchored-band-domain/1',searchSampling:'search-peak-reference/1'},
 result:{...result,results:undefined},tests,integrity,files:sources,
 note:'Frozen historical and failed scopes remain separate. Raw exact regressions are retained; only unfinished historical quantities use original convergence resolution for discernibility. Every original passed physical check remains strict. Ignored runtimes and this manifest are excluded from file hashes.'};
writeFileSync(`${root}/final-manifest.json`,JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({sealedFiles:sources.length,tests:tests.passed,cases:result.cases,exactRegressions:result.exactPreviousTargetRegressions.length}));
