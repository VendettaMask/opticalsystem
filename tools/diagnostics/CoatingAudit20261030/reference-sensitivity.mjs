import {tmmThicknessJacobian} from '../../../.tmp/coating-review-20261030/package/src/index.js';
import {writeFileSync} from 'node:fs';
const cases=[];
for(const [wave,angle,pol,k] of [[450,0,'s',0],[450,0,'p',.02],[830,32,'s',.005],[830,32,'p',.04],[1300,60,'s',.01],[1300,60,'p',0]]){
  const n0=[1,0],ns=[1.52,.001],layers=[{n:[2.15,k],d:wave*.18},{n:[1.43,.002],d:wave*.27},{n:[1.9,.007],d:wave*.12}];
  cases.push({wave,angle,pol,n0,ns,layers,reference:tmmThicknessJacobian(wave,angle,pol,n0,ns,layers)});
}
writeFileSync(process.argv[2],JSON.stringify({source:'Unmodified pinned tmmcore 0.4.2 tmmThicknessJacobian',cases},null,2)+'\n');
console.log(JSON.stringify({cases:cases.length}));
