const fs=require('fs');
const crypto=require('crypto');
const sharp=require('C:/Users/19851/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
(async()=>{
  const root=process.argv[2]||'validation/rechecks/2026-11-04/isekai-folio-rails/';
  const old='validation/rechecks/2026-11-04/isekai-frayed-vellum/previews/';
  const comparisons=[];
  for(const name of ['Huygens-MTF','Huygens-PSF','Wavefront-Map','Spot-Diagram']){
    const before=await sharp(old+name+'-source.png').ensureAlpha().raw().toBuffer({resolveWithObject:true});
    const after=await sharp(root+'previews/'+name+'-source.png').ensureAlpha().raw().toBuffer({resolveWithObject:true});
    comparisons.push({name,width:after.info.width,height:after.info.height,before:hash(before.data),after:hash(after.data),same:before.info.width===after.info.width&&before.info.height===after.info.height&&before.data.equals(after.data)});
  }
  const baseline=JSON.parse(fs.readFileSync('artifacts/validation/isekai-multi-concepts-20261104/unchanged-pixels.json','utf8').replace(/^\uFEFF/,''));
  const otherThemes=[];
  for(const theme of ['Light','Dark','Lumen','Pixel']){
    const current=JSON.parse(fs.readFileSync(root+'previews/'+theme+'-pixels.json','utf8').replace(/^\uFEFF/,''));
    for(const kind of ['png','report']){const key=theme+'/'+kind;otherThemes.push({key,same:current[key]===baseline[key]});}
  }
  const result={date:'2026-11-04',scope:'Only live analysis toolbar and bottom navigation changed; original parchment source is unchanged.',isekaiSourceComparisons:comparisons,otherThemeSourceComparisons:otherThemes,allSame:comparisons.every(c=>c.same)&&otherThemes.every(c=>c.same)};
  fs.writeFileSync(root+'source-pixel-comparisons.json',JSON.stringify(result,null,2)+'\n');
  console.log(JSON.stringify(result));
  if(!result.allSame)process.exitCode=1;
})();
