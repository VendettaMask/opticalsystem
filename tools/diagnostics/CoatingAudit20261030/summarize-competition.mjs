// Reduces saved evidence; does not calculate optical quantities or acceptance.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const root=process.argv[2], read=p=>JSON.parse(readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const tags=readdirSync(`${root}/final`,{withFileTypes:true}).filter(d=>d.isDirectory()&&/^(modern|legacy)-/.test(d.name)).map(d=>d.name).sort();
const rows=tags.map(tag=>{
  const s=read(`${root}/final/${tag}/summary.json`),r=s.Result;
  const g=s.GenerationRun, competition=g?.NarrowBandCompetition??r.Run.NarrowBandCompetition;
  return {tag,source:s.SourceInput,syntheticControl:s.Name.startsWith('ideal'),workflow:s.Workflow,layers:s.Layers.length,
    metrics:r.Metrics,passed:r.Passed,samplingConverged:r.SamplingConverged,checks:r.Checks,seconds:s.Seconds,
    generationOptimizerEvaluations:g?.Screening?g.Evaluations:0,generationTemplateEvaluations:g?.TemplateEvaluations??(g?g.Evaluations:0),
    finalOrSearchOptimizerEvaluations:r.Run.Evaluations,finalOrSearchIterations:r.Run.Iterations,
    competition:competition?{initial:competition.InitialStructures,discrete:competition.DiscreteEvaluations,limit:competition.DiscreteEvaluationLimit,
      rounds:competition.IntegerRounds,matchingTrials:competition.MatchingTrials.length,
      matchingOptimizerEvaluations:competition.MatchingTrials.reduce((a,m)=>a+m.Run.Evaluations,0),
      shortCandidates:(g?.Screening??r.Run.Screening).length}:null,
    materialSuitability:r.MaterialSuitability};
});
function comparable(s){const settings={...s.Settings};delete settings.DesignProtocolVersion;
  return {Target:s.Target,Settings:settings,IncidentId:s.IncidentId,SubstrateId:s.SubstrateId,LowId:s.LowId,HighId:s.HighId,Materials:s.Materials,Layers:s.Layers};}
const inputs=['real1','real2','ideal1','ideal2'].map(name=>({name,
  equalExceptDesignProtocol:JSON.stringify(comparable(rows.find(r=>r.tag===`modern-${name}`).source))===JSON.stringify(comparable(rows.find(r=>r.tag===`legacy-${name}`).source))}));
if(inputs.some(i=>!i.equalExceptDesignProtocol))throw new Error('Paired inputs differ outside protocol');
const points=tags.flatMap(tag=>read(`${root}/final/${tag}/core-cases.json`));
writeFileSync(`${root}/core-cases.json`,JSON.stringify(points,null,2)+'\n');
writeFileSync(`${root}/comparison-summary.json`,JSON.stringify({scope:'Same goals/materials/thickness/layer bounds and 24 final iterations per Generate+Optimize case; modern screening/matching adds compute. Synthetic controls are not real film success.',inputs,rows},null,2)+'\n');
const hash=b=>createHash('sha256').update(b).digest('hex').toUpperCase();
const previous=read('validation/rechecks/2026-10-30/coating-deep-audit/integrity.json');
const frozen=previous.frozen.map(f=>({path:f.path,priorSha256:f.priorSha256,currentSha256:hash(readFileSync(f.path)),unchanged:hash(readFileSync(f.path))===f.priorSha256}));
const path='labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs';
const current=readFileSync(path,'utf8').replace(/\r/g,''),head=execFileSync('git',['show',`HEAD:${path}`],{encoding:'utf8'}).replace(/\r/g,'');
const acceptance=current.slice(current.indexOf('    private static bool Converged('));
const baselineAcceptance=head.slice(head.indexOf('    private static bool Converged('));
const acceptanceUnchanged=acceptance===baselineAcceptance;
if(frozen.some(f=>!f.unchanged)||!acceptanceUnchanged)throw new Error('Frozen physics/acceptance changed');
const sources=[...'Experiment.cs,MaterialSuitability.cs,DesignService.cs,DesignService.Search.cs,DesignService.Balanced.cs,DesignService.NarrowBand.cs,ExperimentStore.cs,Examples.cs'.split(',').map(n=>`labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/${n}`),
  'src/OpticalSystemDesign.App/Laboratories/CoatingDesign/MainWindow.cs',
  ...'NarrowBandCompetitionTests.cs,DailyDesignTests.cs,DesktopProtocolTests.cs,OpticalSystemDesign.CoatingDesign.Tests.csproj,Fixtures/tfstudio-competition-8b942b3.json'.split(',').map(n=>`labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/${n}`),
  ...'CompetitionAudit.cs,Program.cs,reference-competition.mjs,summarize-competition.mjs,compare-reference.mjs,CoatingAudit.csproj'.split(',').map(n=>`tools/diagnostics/CoatingAudit20261030/${n}`),
  'third_party/coating-reference/SiO2-Rodriguez-de-Marcos.yml','third_party/coating-reference/Ta2O5-Rodriguez-de-Marcos.yml'];
writeFileSync(`${root}/integrity.json`,JSON.stringify({head:execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),uncommitted:true,
  scope:'Final source snapshot; earlier attempt builds and non-optical metadata additions are recorded separately in README.',
  acceptanceBodiesUnchangedAgainstHead:acceptanceUnchanged,acceptanceSha256:hash(acceptance),frozen,
  sources:sources.map(path=>({path,sha256:hash(readFileSync(path))}))},null,2)+'\n');
console.log(JSON.stringify({cases:rows.length,rtaPoints:points.length,pairedInputs:inputs,acceptanceUnchanged},null,2));
