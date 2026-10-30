// Offline execution of the unmodified pinned TFStudio engines. No product imports.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { performance } from 'node:perf_hooks';
import { DLSOptimizer } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/physics/optimizer/lsqEngine.js';
import { NewtonOptimizer } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/optimizers/newton.js';
import { makeOperand } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/physics/optimizer/operandModel.js';
import { buildPrototypeFamily } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/prototypeFamily.js';
import { couplingOrder, coupledMirrors } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/coupledPrototype.js';
import { buildPrototypeLayers } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/prototypeLayers.js';
import { adjustToIncidentMedium } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/adjustToIncidentMedium.js';
import { buildFilterTarget } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/filterTarget.js';
import { globalIntegerSearch } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/globalSearch.js';
import { tmm } from '../../../.tmp/coating-review-20261030/package/src/index.js';

const input=JSON.parse(readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,''));
const out=process.argv[3]; mkdirSync(out,{recursive:true}); const candidates=[], summaries=[];
function material(row, lossless=false) {
  let queries=0;
  const getNK = w => {
    queries++;
    const exact = row.Values.find(v=>Math.abs(v[0]-w)<1e-8);
    if (!exact) throw new Error(`Unexported material wavelength ${w}; no approximation allowed`);
    return [exact[1],lossless?0:exact[2]];
  };
  return {name:row.Name,getNK,getQueries:()=>queries};
}
function save(){writeFileSync(`${out}/reference-results.json`,JSON.stringify({source:'Unmodified TFStudio 8b942b3 + tmmcore 0.4.2',summaries,candidates},null,2)+'\n');}
const hr=input.find(x=>x.Name==='HR');
for (const [label,Engine] of [['LM',DLSOptimizer],['Newton',NewtonOptimizer]]) {
  const mats=new Map(hr.Materials.map(m=>[m.Id,material(m)]));
  const ops=Array.from({length:121},(_,i)=>makeOperand({id:`r${i}`,type:'R',lambdaStart:520+i*.5,lambdaEnd:520+i*.5,aoi:0,pol:'avg',target:1,weight:1}));
  const design={incidentMedium:hr.IncidentId,exitMedium:hr.IncidentId,substrate:{material:hr.SubstrateId,thickness:1},frontLayers:hr.Layers.map((l,i)=>({id:`l${i}`,material:l.MaterialId,thickness:l.ThicknessNm,locked:!l.Variable})),backLayers:[],surfaceMode:'front_only',mfEvalMode:'side'};
  const engine=new Engine(ops,design,id=>mats.get(id),{dMin:2,dMax:2000,tol:1e-10});
  let analytic=0,fd=0, fallback=0; const oldJac=engine._analyticJacobian.bind(engine),oldLm=engine.lmStep.bind(engine);
  engine._analyticJacobian=(...a)=>{const j=oldJac(...a); if(j)analytic++;else fd++; return j;};
  engine.lmStep=(...a)=>{fallback++;return oldLm(...a);};
  const initial=engine.mf, start=performance.now();
  for(let i=0;i<80&&!engine.isConverged()&&performance.now()-start<120000;i++)engine.step();
  const layers=hr.Layers.map((l,i)=>({MaterialId:l.MaterialId,ThicknessNm:engine.thickBest[i]}));
  summaries.push({name:`HR-${label}`,scope:'Same 121-point R=1 squared objective and thickness bounds as diagnostic native run; differs from product v2 merit',initial,final:engine.mfBest,iterations:engine.iter,converged:engine.isConverged(),reason:engine.convergedBy,analyticCalls:analytic,fdFallbackCalls:fd,lmCalls:fallback,seconds:(performance.now()-start)/1000});
  candidates.push({Name:`HR-${label}`,Source:'HR',Lossless:false,Embedded:false,TransparentExteriors:false,Layers:layers});
  console.log(`Reference HR ${label}: ${engine.mfBest}, ${engine.iter} steps, analytic=${analytic}, fd=${fd}`);save();
}
for(const source of input.filter(x=>x.Name!=='HR')) {
  const mats=new Map(source.Materials.map(m=>[m.Id,material(m,true)]));
  const nH=w=>mats.get(source.HighId).getNK(w), nL=w=>mats.get(source.LowId).getNK(w), nSub=w=>mats.get(source.SubstrateId).getNK(w), nInc=w=>mats.get(source.IncidentId).getNK(w);
  const q=source.Name==='NBF1'?1:2, delta=couplingOrder(nH(550)[0],nL(550)[0],nSub(550)[0]);
  const describe=(layers)=>layers.map(l=>({MaterialId:l.tag==='H'?source.HighId:source.LowId,ThicknessNm:l.d}));
  const add=(name,layers,embedded,metadata)=>candidates.push({Name:name,Source:source.Name,Lossless:true,Embedded:embedded,TransparentExteriors:true,Layers:describe(layers),Metadata:metadata});
  for(const level of [.8913,.5]) {
    const family=buildPrototypeFamily({nH,nL,nSub,lambda0_nm:550,cavities:q,targetFWHM:15,passLevel:level});
    summaries.push({name:`${source.Name}-family-${level}`,scope:'Lossless embedded reference table; targetFWHM names the width AT passLevel, not generally half-power width',delta,family});
    for(const row of family) {
      const mirrors=coupledMirrors(q,row.notationM,delta),spacers=Array(q).fill(row.spacerOrder);
      const layers=buildPrototypeLayers({nH,nL,lambda0_nm:550,mirrors,spacers});
      const name=`${source.Name}-level${level}-m${row.notationM}-order${row.spacerOrder}`;
      add(name+'-embedded',layers,true,{level,mirrors,spacers});
      add(name+'-air',layers,false,{level,mirrors,spacers});
      const match=adjustToIncidentMedium({filterLayers:layers,nH,nL,nInc,nSub,lambda0_nm:550,mode:'vcoat',pol:'avg'});
      add(name+'-matched',match.layers,false,{level,mirrors,spacers,addedCoat:match.arLayers.map(l=>({tag:l.tag,d:l.d})),matchCenterR:match.residualR});
    }
  }
  // One bounded run of the actual integer optimizer; not equal to the native objective or a global optimum claim.
  const target=buildFilterTarget({lambda0_nm:550,halfPass:7.5,halfStop:40,passLevel:50,aoi:0,pol:'avg'});
  const start=performance.now();
  const searched=globalIntegerSearch({nH,nL,nSub,lambda0_nm:550,target,cavities:q,seedMirror:5,seedSpacer:1,seedMirrors:coupledMirrors(q,5,delta),symMirrors:true,symCavities:true,minMirror:1,maxMirror:19,minOrder:1,maxOrder:10,restarts:1,rngSeed:1234});
  summaries.push({name:`${source.Name}-integer`,scope:'Idealized lossless control; 1 restart, mirror<=19, spacer order<=10; point target differs from native FWHM+OD, original layer cap NOT enforced internally',target,seconds:(performance.now()-start)/1000,candidates:searched.candidates});
  for(const [i,c] of searched.candidates.slice(0,4).entries()) {
    const layers=buildPrototypeLayers({nH,nL,lambda0_nm:550,mirrors:c.mirrors,spacers:c.spacers});
    const match=adjustToIncidentMedium({filterLayers:layers,nH,nL,nInc,nSub,lambda0_nm:550,mode:'vcoat',pol:'avg'});
    add(`${source.Name}-integer${i}-matched`,match.layers,false,{integer:c,matchCenterR:match.residualR});
  }
  console.log(`Reference ${source.Name}: ${familyCount(source.Name)} candidate geometries; integer ${searched.best.mf}`);save();
}
function familyCount(name){return candidates.filter(c=>c.Source===name).length;}
const rta=[];
for(const c of candidates) {
  const s=input.find(x=>x.Name===c.Source), mats=new Map(s.Materials.map(m=>[m.Id,material(m,c.Lossless)]));
  for(const w of [s.Target.MinimumNm,550,s.Target.MaximumNm]) {
    const n0=mats.get(c.Embedded?s.SubstrateId:s.IncidentId).getNK(w),ns=mats.get(s.SubstrateId).getNK(w);
    rta.push({Name:c.Name,Lambda:w,Angle:0,Pol:'S',N0:n0,Ns:ns,Layers:c.Layers.map(l=>({N:mats.get(l.MaterialId).getNK(w),D:l.ThicknessNm})),Reference:tmm(w,0,'s',n0,ns,c.Layers.map(l=>({n:mats.get(l.MaterialId).getNK(w),d:l.ThicknessNm})))});
  }
}
writeFileSync(`${out}/reference-rta.json`,JSON.stringify(rta,null,2)+'\n');save();
