// Offline fixture from the unmodified pinned reference; never imported by the product.
import { writeFileSync } from 'node:fs';
import { buildPrototypeLayers, toNDLayers } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/prototypeLayers.js';
import { adjustToIncidentMedium } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/adjustToIncidentMedium.js';
import { tmm } from '../../../.tmp/coating-review-20261030/package/src/index.js';
const nH=()=>[2.1,0], nL=()=>[1.45,0], nSub=()=>[1.52,0], nInc=()=>[1,0];
const vectors=[[[7,7],[1]],[[4,4],[2]],[[3,7,3],[1,1]],[[5,11,5],[1,1]],
  [[6,13,6],[1,1]],[[4,9,5],[2,1]],[[5,11,11,5],[1,2,1]],[[3,7,7,3],[2,1,3]],
  [[3,7,7,7,3],[1,1,2,1]],[[2,5,5,5,5,2],[1,2,1,2,1]]];
const cases=vectors.map(([mirrors,spacers])=>({mirrors,spacers,layers:buildPrototypeLayers({nH,nL,lambda0_nm:550,mirrors,spacers})
  .map(l=>({tag:l.tag,role:l.role,order:l.order,thicknessNm:l.d}))}));
const matching=vectors.slice(0,6).map(([mirrors,spacers])=>{
  const original=buildPrototypeLayers({nH,nL,lambda0_nm:550,mirrors,spacers});
  const result=adjustToIncidentMedium({filterLayers:original,nH,nL,nSub,nInc,lambda0_nm:550,mode:'vcoat',pol:'avg'});
  return {mirrors,spacers,before:tmm(550,0,'s',nInc(),nSub(),toNDLayers(original,550)),
    after:tmm(550,0,'s',nInc(),nSub(),toNDLayers(result.layers,550)),residualR:result.residualR,
    layers:result.layers.map(l=>({tag:l.tag,thicknessNm:l.d}))};
});
writeFileSync(process.argv[2],JSON.stringify({source:'TFStudio 8b942b3f956728a23cea2b35023c8b5d8de17428 + tmmcore 0.4.2, unmodified JavaScript',
  scope:'Synthetic lossless constant-index controls, not real film data or native goal equivalence',nH:2.1,nL:1.45,nSub:1.52,wavelengthNm:550,cases,matching},null,2)+'\n');
console.log(JSON.stringify({prototypes:cases.length,matching:matching.length,matchingR:matching.map(m=>m.after.R)}));
