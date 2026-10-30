// Primary-source construction reference only. No product import of upstream code.
import { buildPrototypeLayers } from '../../../.tmp/coating-review-20261030/TFStudio-8b942b3f956728a23cea2b35023c8b5d8de17428/src/utils/filter/filterDesign/prototypeLayers.js';
import { writeFileSync } from 'node:fs';
const cases=[];
for (const mirrors of [[3,3,3],[4,4,4],[3,5,3],[3,7,3]]) for (const order of [1,2]) {
  const spacers=[order,order];
  const layers=buildPrototypeLayers({ nH:()=>[2.1,0],nL:()=>[1.45,0],lambda0_nm:550,mirrors,spacers });
  cases.push({mirrors,spacers,layers:layers.map(l=>({tag:l.tag,role:l.role,order:l.order,thicknessNm:l.d}))});
}
writeFileSync(process.argv[2],JSON.stringify({source:'TFStudio 8b942b3f956728a23cea2b35023c8b5d8de17428 prototypeLayers.js',nH:2.1,nL:1.45,wavelengthNm:550,cases},null,2)+'\n');
