// Offline, unmodified pinned reference engines. Product optics and acceptance are not replaced.
import {readFileSync,writeFileSync} from 'node:fs';
import {performance} from 'node:perf_hooks';
import {DLSOptimizer} from '../../../.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/physics/optimizer/lsqEngine.js';
import {NewtonOptimizer} from '../../../.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/optimizers/newton.js';
import {makeOperand} from '../../../.tmp/coating-infeasible-repair/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/physics/optimizer/operandModel.js';
const input=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,'')), results=[];
for(const {Input:d,Materials:materialRows} of input){
 const t=d.Target;
 for(const [name,Engine] of [['DLS',DLSOptimizer],['Newton',NewtonOptimizer]]){
  const mats=new Map(materialRows.map(m=>[m.Id,{name:m.Name,getNK(w){
   const v=m.Values.find(v=>Math.abs(v[0]-w)<1e-8);
   if(!v)throw Error(`Reference asked for unexported n/k at ${w}; no interpolation fallback`);
   return v.slice(1);
  }}]));
  const pol=['s','p','avg'][t.Polarization] ?? ({S:'s',P:'p',Unpolarized:'avg'}[t.Polarization]);
  let ops=[];
  const point=(w,type,target,weight=1)=>makeOperand({id:`p${ops.length}`,type,lambdaStart:w,lambdaEnd:w,aoi:t.AngleDegrees,pol,target,weight});
  if(t.Kind===1 || t.Kind==='HighReflector'){
   ops=Array.from({length:121},(_,i)=>point(t.MinimumNm+(t.MaximumNm-t.MinimumNm)*i/120,'R',1));
  }else{
   // Reference's native point operands: peak equality, half-height at target edges, and stop-band T=0.
   // This is deliberately disclosed as a different guide; the original FWHM/OD gates are applied afterwards.
   ops.push(point(t.CenterNm,'T',t.PeakTransmittance));
   for(const sign of [-1,1])ops.push(point(t.CenterNm+sign*t.FwhmNm/2,'T',t.PeakTransmittance/2));
   for(const b of t.StopBands)for(let i=0;i<=8;i++)ops.push(point(b.MinimumNm+(b.MaximumNm-b.MinimumNm)*i/8,'T',0));
  }
  const design={incidentMedium:d.IncidentId,exitMedium:d.IncidentId,substrate:{material:d.SubstrateId,thickness:1},frontLayers:d.Layers.map((l,i)=>({id:`l${i}`,material:l.MaterialId,thickness:l.ThicknessNm,locked:!l.Variable})),backLayers:[],surfaceMode:'front_only',mfEvalMode:'side'};
  const engine=new Engine(ops,design,id=>mats.get(id),{dMin:d.Settings.MinimumThicknessNm,dMax:d.Settings.MaximumThicknessNm,tol:1e-10});
  const start=performance.now(); let analytic=0,fd=0;
  const jac=engine._analyticJacobian.bind(engine);engine._analyticJacobian=(...args)=>{const j=jac(...args);if(j)analytic++;else fd++;return j;};
  const initial=engine.mf;
  for(let i=0;i<d.Settings.MaximumIterations&&!engine.isConverged()&&performance.now()-start<120000;i++)engine.step();
  results.push({Name:`${d.Name}-${name}`,Source:d.Name,ReferenceCommit:'8b942b3f956728a23cea2b35023c8b5d8de17428',Guide:'Reference point squared targets; different from native measured-band/minmax constraints',InitialMerit:initial,FinalMerit:engine.mfBest,Iterations:engine.iter,Reason:engine.convergedBy,Analytic:analytic,FiniteDifference:fd,Seconds:(performance.now()-start)/1000,Layers:d.Layers.map((l,i)=>({...l,ThicknessNm:engine.thickBest[i]}))});
  writeFileSync(process.argv[3],JSON.stringify(results,null,2)+'\n');
  console.log(`${d.Name} ${name}: ${initial} -> ${engine.mfBest}, ${engine.iter} iterations`);
 }
}
