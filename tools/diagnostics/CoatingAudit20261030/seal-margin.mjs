import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const p=read(`${root}/paired-results.json`),tests=read(`${root}/verification-counts.json`),integrity=read(`${root}/integrity.json`);
if(!integrity.passed||p.guardFailures.length||tests.failed||tests.notExecuted||tests.passed!==tests.total)throw Error('Cannot seal incomplete or failed scope');
const files=[];
function walk(dir){for(const e of readdirSync(dir,{withFileTypes:true})){
 const path=`${dir}/${e.name}`;
 if(e.isDirectory()){if(!e.name.endsWith('-runtime')&&!['bin','obj'].includes(e.name))walk(path);}
 else if(e.name!=='final-manifest.json')files.push(path);
}}
walk(root);
for(const dir of ['labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine','labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests','tools/diagnostics/CoatingAudit20261030'])
 for(const e of readdirSync(dir,{withFileTypes:true}))if(e.isFile()&&/\.(cs|csproj|mjs)$/.test(e.name))files.push(`${dir}/${e.name}`);
files.push(...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(f=>f.endsWith('.cs')).map(f=>`src/OpticalSystemDesign.Core/Coatings/${f}`),
 'src/OpticalSystemDesign.Core/Optimization/InequalityLeastSquaresStep.cs','docs/COATING_MARGIN_REFINEMENT_2026-10-31.md','docs/COATING_DESIGN_LAB.md',
 'docs/FILE_FORMATS_AND_PLUGINS.md','docs/CURRENT_STATUS.md','labs/CoatingDesign/README.md',
 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/margin-refinement-20261031.json');
const records=[...new Set(files)].sort().map(path=>({path,bytes:readFileSync(path).length,sha256:sha(path)}));
writeFileSync(`${root}/final-manifest.json`,JSON.stringify({date:'2026-10-31',sealedAt:new Date().toISOString(),baseCommit:execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),
 scope:'Full search-peak separation, raw passband margin preservation and stateful reflectance continuation',
 protocols:{design:'general-constraint-coating/4',sampling:'anchored-band-domain/1',searchSampling:'search-peak-reference/2',passbandQuality:'passband-margin-quality/1',reflectanceQuality:'worst-reflectance/7'},
 result:{...p,results:undefined},tests,integrity,files:records,
 note:'47 original specifications are unchanged. Historical control optimization and new/cold work are distinguished; exact differences remain reported. Original passed checks are strict, historical unfinished results use only the original convergence resolution for discernibility. Ignored runtimes/build directories and this manifest are excluded.'},null,2)+'\n');
console.log(JSON.stringify({sealedFiles:records.length,cases:p.cases,tests:tests.passed}));
