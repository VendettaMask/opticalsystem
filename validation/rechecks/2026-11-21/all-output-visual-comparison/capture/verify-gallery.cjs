const fs=require('fs'),vm=require('vm'),path=require('path');
const root=process.argv[2]?path.resolve(process.argv[2]):path.dirname(__dirname),html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
const ids=new Set([...html.matchAll(/id="([^"]+)"/g)].map(x=>x[1]));
const elements=new Map([...ids].map(id=>[id,{value:0,innerHTML:'',textContent:'',hidden:false,disabled:false,options:[],add(x){this.options.push(x)},close(){}}]));
const context={document:{getElementById(id){if(!elements.has(id))throw Error('Missing element: '+id);return elements.get(id)},addEventListener(){}},window:{scrollTo(){}},Option:function(text,value){this.text=text;this.value=value},console};
vm.createContext(context);vm.runInContext(script,context);
vm.runInContext(`for(li=0;li<data.lenses.length;li++){for(ai=0;ai<data.analyses.length;ai++){render()}ai=0;grid=true;render();grid=false;}li=0;ai=data.analyses.findIndex(x=>x.key==='Spot Diagram');render();`,context);
const data=JSON.parse(fs.readFileSync(path.join(root,'gallery-data.json'),'utf8'));let links=0,corrections=0,historical=0,reviews=0;
if(vm.runInContext('JSON.stringify(data)',context)!==JSON.stringify(data))throw Error('Embedded HTML data does not match gallery-data.json');
const manifestPath=path.join(__dirname,'..','corrections.json'),manifest=fs.existsSync(manifestPath)?JSON.parse(fs.readFileSync(manifestPath,'utf8')):{entries:[]};
function exists(file){if(!fs.existsSync(file)||!fs.statSync(file).isFile()||fs.statSync(file).size===0)throw Error('Invalid evidence link: '+file);}
for(const lens of data.lenses){if(!lens.sourceUnchanged)throw Error('Source changed');for(const [analysis,result] of Object.entries(lens.results)){
  let declared;
  if(result.review){
    const review=(manifest.reviews||[]).find(entry=>entry.id===result.review.id&&entry.analysis===analysis&&entry.sourceSha256===lens.sha256);
    if(!review)throw Error('Undeclared review');
    const evidence=path.resolve(root,result.review.evidence);exists(evidence);
    if(evidence!==path.resolve(path.dirname(manifestPath),review.evidence))throw Error('Review evidence mismatch');
    if(result.review.disposition!==review.disposition)throw Error('Review verdict mismatch');
    reviews++;
  }
  if(result.correction){
    declared=manifest.entries.find(entry=>entry.id===result.correction.id&&entry.analysis===analysis&&entry.sourceSha256===lens.sha256);
    if(!declared)throw Error('Undeclared correction');
    if(path.resolve(root,result.current)!==path.resolve(path.dirname(manifestPath),declared.image))throw Error('Correction image mismatch');
    const evidence=path.resolve(root,result.correction.evidence);exists(evidence);
    if(evidence!==path.resolve(path.dirname(manifestPath),declared.evidence))throw Error('Correction record mismatch');
    const original=path.resolve(root,result.originalCurrent);exists(original);
    if(!original.startsWith(root+path.sep))throw Error('Historical image outside its gallery');
    for(const image of result.correction.relatedImages||[])exists(path.resolve(root,image.path));
    corrections++;historical++;
  }
  for(const side of ['current','native']){if(result[side]){const file=path.resolve(root,result[side]);exists(file);if(!file.startsWith(root+path.sep)&&!(side==='current'&&declared))throw Error('Image outside gallery: '+file);links++}}
}}
console.log(JSON.stringify({allSelectionsRendered:data.lenses.length*data.analyses.length,localImageLinksChecked:links,allSourcesUnchanged:true,embeddedDataMatchesJson:true,scopedCorrectionsChecked:corrections,historicalImagesPreserved:historical,scopedReviewsChecked:reviews}));
