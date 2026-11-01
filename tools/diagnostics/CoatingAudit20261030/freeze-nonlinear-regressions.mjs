import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
const root=process.argv[2],read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const source='validation/rechecks/2026-10-31/coating-margin-refinement/final-matrix/after-raw';
const names=['holdout-real-hr-red','real-hr-96','real-nbf2-96','new-real-nbf-red','holdout-violet','correction-real-nbf-red'];
const cases=names.map(name=>{const prior=`${source}/${name}.coating.json`,file=existsSync(prior)?prior:`${root}/before-raw/${name}.coating.json`,old=read(file),controlFile=`${root}/control-cold/${name}.coating.json`,control=read(controlFile);return{Input:read(`${root}/inputs/${name}.coating.json`),PreviousLayers:old.Layers,PreviousMetrics:old.Result.Metrics,ControlLayers:control.Layers,ControlSource:controlFile,ControlSha256:createHash('sha256').update(readFileSync(controlFile)).digest('hex'),Source:file,SourceSha256:createHash('sha256').update(readFileSync(file)).digest('hex')};});
writeFileSync('labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/Fixtures/nonlinear-correction-20261031.json',JSON.stringify({Scope:'Frozen previous release margins; original targets and material snapshots',Cases:cases},null,2)+'\n');
