import {readFileSync,writeFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,'')),sha=p=>createHash('sha256').update(readFileSync(p)).digest('hex');
const p=read(`${root}/paired-results.json`),tests=read(`${root}/verification-counts.json`),integrity=read(`${root}/integrity.json`),rta=read(`${root}/tmmcore-comparison.json`);
if(p.guardFailures.length||!integrity.passed||tests.failed||tests.notExecuted||tests.total!==tests.passed||Object.values(rta.maximumErrors).some(x=>x>2e-11))throw Error('Unverified scope');
const files=[];
function walk(dir){for(const e of readdirSync(dir,{withFileTypes:true})){const path=`${dir}/${e.name}`;if(e.isDirectory()){if(!e.name.endsWith('-runtime')&&!['bin','obj'].includes(e.name))walk(path);}else if(e.name!=='final-manifest.json')files.push(path);}}
walk(root);
for(const dir of ['labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine','labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests','tools/diagnostics/CoatingAudit20261030'])for(const e of readdirSync(dir,{withFileTypes:true}))if(e.isFile()&&/\.(cs|csproj|mjs)$/.test(e.name))files.push(`${dir}/${e.name}`);
files.push(...readdirSync('src/OpticalSystemDesign.Core/Coatings').filter(x=>x.endsWith('.cs')).map(x=>`src/OpticalSystemDesign.Core/Coatings/${x}`),
 'src/OpticalSystemDesign.Core/Optimization/InequalityLeastSquaresStep.cs','src/OpticalSystemDesign.Core/Optimization/NonlinearInequalityCorrection.cs',
 'labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/nonlinear-correction-20261031.json',
 'docs/COATING_NONLINEAR_CORRECTION_2026-10-31.md','docs/COATING_DESIGN_LAB.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/CURRENT_STATUS.md','docs/README.md','labs/CoatingDesign/README.md');
const records=[...new Set(files)].sort().map(path=>({path,bytes:readFileSync(path).length,sha256:sha(path)}));
const binaryNames=['OpticalSystemDesign.Core','OpticalSystemDesign.Application','OpticalSystemDesign.CoatingDesign.Engine','OpticalSystemDesign.CoatingDesign.Tests'];
const testPath='labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/bin/Debug/net10.0';
writeFileSync(`${root}/final-manifest.json`,JSON.stringify({sealedAt:new Date().toISOString(),baseCommit:execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),
 scope:'General nonlinear trial correction, physical-direction dominance, immutable material preparation reuse; unchanged gates and n/k',
 protocols:{design:'general-constraint-coating/4',reflectanceQuality:'worst-reflectance/8',passbandQuality:'passband-margin-quality/2',correction:'trial-constraint-correction/1',sampling:'anchored-band-domain/1',searchSampling:'search-peak-reference/2'},
 comparison:{cases:p.cases,passed:p.passed,vsBefore:p.vsBefore,vsControl:p.vsControl,newPasses:p.newPasses,guardFailures:p.guardFailures,physicalNarrowBandImprovements:p.physicalNarrowBandImprovements},
 tests,rta:{cases:rta.cases,maximumErrors:rta.maximumErrors},execution:read(`${root}/execution-ledger.json`),integrity,
 testedBinaries:binaryNames.map(name=>({name,sha256:sha(`${testPath}/${name}.dll`)})),files:records,
 note:'Runtime/build directories and this manifest are excluded. Source snapshots and final data are included. Earlier failed/interrupted prototypes remain labeled, not counted as passes. Frozen previous evidence was not overwritten; current source/doc fingerprints refer to this sealing time.'},null,2)+'\n');
const failures=records.filter(r=>sha(r.path)!==r.sha256);if(failures.length)throw Error('Manifest verification failed');
console.log(JSON.stringify({sealedFiles:records.length,hashFailures:failures.length,cases:p.cases,tests:tests.passed,rta:rta.cases}));
