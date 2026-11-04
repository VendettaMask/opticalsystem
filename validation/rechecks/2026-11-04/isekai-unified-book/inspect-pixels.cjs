const sharp = require('C:/Users/19851/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const fs = require('fs'), path = require('path');
(async () => {
  for(const [sub,stem] of [['previews','Dark'],['material-checks','Spot-Diagram-Dark'],['material-checks','Spot-Diagram-Light'],['material-checks','Spot-Diagram-Lumen'],['material-checks','Spot-Diagram-Pixel']]) {
    const before=path.join(__dirname,sub,stem+'-before-failure.png'),after=path.join(__dirname,sub,stem+'-after-failure.png');
    if(!fs.existsSync(before))continue;
    const a=await sharp(before).ensureAlpha().raw().toBuffer({resolveWithObject:true}),b=await sharp(after).ensureAlpha().raw().toBuffer({resolveWithObject:true});
    const w=a.info.width,h=a.info.height;
    let count=0,x0=w,y0=h,x1=0,y1=0,max=0;const examples=[];
    for(let y=0;y<h;y++)for(let x=0;x<w;x++){const p=(y*w+x)*4;let d=0;for(let c=0;c<4;c++)d=Math.max(d,Math.abs(a.data[p+c]-b.data[p+c]));if(d){count++;x0=Math.min(x0,x);x1=Math.max(x1,x);y0=Math.min(y0,y);y1=Math.max(y1,y);if(d>max){max=d;if(examples.length<12)examples.push({x,y,delta:d,before:[...a.data.subarray(p,p+4)],after:[...b.data.subarray(p,p+4)]});}}}
    console.log(JSON.stringify({stem,count,bounds:[x0,y0,x1,y1],max,examples}));
  }
})().catch(e=>{console.error(e);process.exitCode=1});
