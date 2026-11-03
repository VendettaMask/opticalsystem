const fs=require('fs'),path=require('path'),assert=require('assert/strict'),{pathToFileURL}=require('url');
const {chromium}=require('C:/Users/19851/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
const root=path.resolve(__dirname,'../../../../docs/assets/icons');
const browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true});
try{
 const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(pathToFileURL(path.join(root,'index.html')).href);await page.waitForSelector('.tile');
 assert.equal(await page.locator('#total-icons').textContent(),'221');
 for(const theme of ['Lumen','Isekai']){await page.locator(`[data-theme="${theme}"]`).click();assert(await page.locator('.tile img').count()>0);}
 const urls=await page.locator('a[href]').evaluateAll(as=>as.map(a=>a.getAttribute('href')).filter(h=>h&&!h.startsWith('#')));
 for(const href of urls)if(!/^[a-z][a-z\d+.-]*:/i.test(href))assert(fs.existsSync(path.resolve(root,decodeURIComponent(href))),'Missing '+href);
 await page.goto(pathToFileURL(path.join(root,'图标风格规范.html')).href);
 const body=await page.locator('body').textContent();for(const phrase of ['剑与魔法','钢铁侠','炼金','后续批量改图流程'])assert(body.includes(phrase));
 assert.deepEqual(errors,[]);
 fs.writeFileSync(path.join(__dirname,'portable-gallery-check.json'),JSON.stringify({passed:true,semanticIcons:221,themesChecked:['Lumen','Isekai'],relativeLinksChecked:urls.length,wordPresent:fs.existsSync(path.join(root,'图标风格规范.docx')),pageErrors:errors},null,2)+'\n');
 console.log('Portable gallery, both theme variants, normative text and download links passed.');
}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
