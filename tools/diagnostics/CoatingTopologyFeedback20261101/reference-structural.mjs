// Offline adapter only. Mutation, Metropolis acceptance and DLS are unmodified pinned reference functions.
import {readFileSync,writeFileSync} from 'node:fs';
import {performance} from 'node:perf_hooks';
import {DLSOptimizer} from '../../../.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/physics/optimizer/lsqEngine.js';
import {makeOperand} from '../../../.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/physics/optimizer/operandModel.js';
import {proposeMutation,makeRng,metropolisAccept,temperatureAt,tidyLayers} from '../../../.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/synthesis/structuralOptimizer.js';
const rows=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,'')),results=[];
for(const {Input:d,Materials:mr} of rows){
 const t=d.Target,mat=new Map(mr.map(m=>[m.Id,{name:m.Name,getNK(w){const row=m.Values.find(v=>Math.abs(v[0]-w)<1e-8);if(!row)throw Error('Unexported material wavelength '+w);return row.slice(1);}}]));
 const pol=({S:'s',P:'p',Unpolarized:'avg'})[t.Polarization],ops=[];
 const point=(w,type,target)=>makeOperand({id:'p'+ops.length,type,lambdaStart:w,lambdaEnd:w,aoi:t.AngleDegrees,pol,target,weight:1});
 if(t.Kind==='HighReflector')for(let i=0;i<=120;i++)ops.push(point(t.MinimumNm+(t.MaximumNm-t.MinimumNm)*i/120,'R',1));
 else{ops.push(point(t.CenterNm,'T',t.PeakTransmittance));for(const s of [-1,1])ops.push(point(t.CenterNm+s*t.FwhmNm/2,'T',t.PeakTransmittance/2));for(const b of t.StopBands)for(let i=0;i<=8;i++)ops.push(point(b.MinimumNm+(b.MaximumNm-b.MinimumNm)*i/8,'T',0));}
 const initial=d.Layers.map((l,i)=>({id:'l'+i,material:l.MaterialId,thickness:l.ThicknessNm,locked:!l.Variable}));
 const rng=makeRng(d.Settings.RandomSeed),w=Math.ceil(Math.sqrt(d.Settings.MaximumIterations)),trace=[];let calls=0,iterations=0;
 function refine(layers,n){
  const design={incidentMedium:d.IncidentId,exitMedium:d.IncidentId,substrate:{material:d.SubstrateId,thickness:1},frontLayers:layers,backLayers:[],surfaceMode:'front_only',mfEvalMode:'side'};
  const e=new DLSOptimizer(ops,design,id=>mat.get(id),{dMin:d.Settings.MinimumThicknessNm,dMax:d.Settings.MaximumThicknessNm,tol:1e-10});
  for(let i=0;i<n&&!e.isConverged();i++)e.step();calls++;iterations+=e.iter;
  return {layers:layers.map((l,i)=>({...l,thickness:e.thickBest[i]})),merit:e.mfBest,iterations:e.iter};
 }
 let state=refine(initial,0),best=state;const watch=performance.now(),structural=(d.Settings.Search?.VaryStructure??true)&&initial.every(l=>!l.locked);
 for(let round=0;round<2;round++){
  for(let step=0;step<Math.min(4,d.Settings.Search?.Starts??6);step++){
   let proposed=state.layers,mutation={kind:'old-continuation'};
   if(structural){
    let valid=null;
    for(let attempt=0;attempt<32&&!valid;attempt++){
     const p=proposeMutation(state.layers,{rng,pool:[{id:d.LowId},{id:d.HighId}],dMin:d.Settings.MinimumThicknessNm,dMax:d.Settings.MaximumThicknessNm,addMaxNm:t.CenterNm/(4*mat.get(d.HighId).getNK(t.CenterNm)[0]),jitterPct:(d.Settings.Search?.JitterPercent??12)/100});
     if(!p)continue;const clean=tidyLayers(p.layers,d.Settings.MinimumThicknessNm);
     if(clean.length&&clean.length<=t.MaximumLayers&&clean.every(l=>l.thickness>=d.Settings.MinimumThicknessNm&&l.thickness<=d.Settings.MaximumThicknessNm))valid={...p,layers:clean};
    }
    if(!valid){trace.push({round,step,skipped:true});continue;}proposed=valid.layers;mutation=valid.mutation;
   }
   const candidate=refine(proposed,w),accepted=metropolisAccept(state.merit,candidate.merit,temperatureAt((round*4+step)/8,.1,.001),rng);
   if(accepted)state=candidate;if(candidate.merit<best.merit)best=candidate;
   trace.push({round,step,mutation,accepted,merit:candidate.merit,layers:candidate.layers,iterations:candidate.iterations});
  }
  const full=refine(state.layers,d.Settings.MaximumIterations);state=full;if(full.merit<best.merit)best=full;
  trace.push({round,full:true,merit:full.merit,layers:full.layers,iterations:full.iterations});
 }
 results.push({Name:d.Name+'-structural-DLS',Source:d.Name,ReferenceCommit:'8b942b3f956728a23cea2b35023c8b5d8de17428',Guide:'Reference structural mutations/annealing with native point-squared DLS; not equal objective or total optical-query cost',Calls:calls,Iterations:iterations,Seconds:(performance.now()-watch)/1000,Trace:trace,Layers:best.layers.map(l=>({MaterialId:l.material,ThicknessNm:l.thickness,Variable:!l.locked}))});
 writeFileSync(process.argv[3],JSON.stringify(results,null,2)+'\n');console.log(d.Name+' reference structural: '+best.merit+'; layers='+best.layers.length+'; iterations='+iterations);
}
