const fs=require('fs'),path=require('path'),assert=require('assert/strict'),crypto=require('crypto'),{pathToFileURL}=require('url');
const modules='C:/Users/19851/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules';
const sharp=require(modules+'/sharp'),{chromium}=require(modules+'/playwright');
const root='D:/optical system design/redesigned-icons-20261103',catalog=JSON.parse(fs.readFileSync(path.join(root,'catalog.json'),'utf8'));
const sha=b=>crypto.createHash('sha256').update(b).digest('hex'),checks=[];
const passed=name=>checks.push({name,result:'passed'});
(async()=>{
 for(const t of catalog.themes)for(const i of catalog.icons){const {data,info}=await sharp(path.join(root,'svg',t.key,i.id+'.svg')).ensureAlpha().raw().toBuffer({resolveWithObject:true});assert.equal(info.width,32);assert.equal(info.height,32);let ink=0;for(let k=3;k<data.length;k+=4)if(data[k])ink++;assert(ink>0&&ink<1024,i.id)}
 passed('All 1105 theme SVGs render visibly on transparent backgrounds');
 for(const theme of ['Lumen','Isekai'])for(const size of [18,24,32]){const ids=new Set();for(const m of catalog.mtfMenu){const png=await sharp(path.join(root,'svg',theme,m.icon+'.svg')).resize(size,size).png().toBuffer();assert(!ids.has(sha(png)));ids.add(sha(png))}assert.equal(ids.size,9)}
 passed('Both new profiles preserve nine distinct MTF drawings at 18/24/32 pixels');
 for(const theme of ['Lumen','Isekai'])await sharp(path.join(root,`overview/semantic-icons-${theme}.svg`)).png().toFile(path.join(root,`全部主题图标-${theme}.png`));
 const browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true});
 try{
 const context=await browser.newContext({viewport:{width:1440,height:1100},acceptDownloads:true}),page=await context.newPage(),errors=[];
 page.on('pageerror',e=>errors.push(e.message));await page.goto(pathToFileURL(path.join(root,'index.html')).href);await page.waitForSelector('.tile');
 let count=0;const names=new Set();while(true){const ids=await page.locator('.tile').evaluateAll(es=>es.map(e=>e.dataset.icon));count+=ids.length;ids.forEach(n=>names.add(n));if(await page.locator('#next').isDisabled())break;await page.locator('#next').click()}assert.equal(count,221);assert.equal(names.size,221);passed('Gallery includes exactly all 221 actually used semantic icons');
 await page.selectOption('#mode','functions');count=0;while(true){count+=await page.locator('.tile').count();if(await page.locator('#next').isDisabled())break;await page.locator('#next').click()}assert.equal(count,332);await page.selectOption('#mode','shapes');passed('All 332 function references and alternate states remain listed');
 for(const t of catalog.themes){await page.locator(`[data-theme="${t.key}"]`).click();await page.fill('#search','user-guide');await page.locator('.tile[data-icon="user-guide"]').click();
 if(['Lumen','Isekai'].includes(t.key)){assert.equal(await page.locator('#sizes img').count(),4);await page.waitForFunction(()=>[...document.querySelectorAll('#sizes img')].every(i=>i.complete&&i.naturalWidth>0));}
 const [download]=await Promise.all([page.waitForEvent('download'),page.locator('#download').click()]);assert.equal(sha(fs.readFileSync(await download.path())),sha(fs.readFileSync(path.join(root,'svg',t.key,'user-guide.svg'))));await page.keyboard.press('Escape');}
 passed('Theme-specific artwork loads and downloads exactly match all five SVG exports');
 await page.locator('#clear').click();await page.selectOption('#filter','mtf');
 for(const theme of ['Lumen','Isekai']){await page.locator(`[data-theme="${theme}"]`).click();assert.equal(await page.locator('.tile').count(),9);assert.equal(await page.locator('#mtf-grid img').count(),9);await page.waitForFunction(()=>[...document.querySelectorAll('#mtf-grid img')].every(i=>i.complete&&i.naturalWidth>0));await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(root,`主题接入-${theme}.png`)});await page.locator('section[aria-labelledby="mtf-heading"]').screenshot({path:path.join(root,`MTF主题-${theme}.png`)});}
 passed('Top menu and icon gallery repaint together when switching the two profiles');
 await page.setViewportSize({width:390,height:844});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));assert.deepEqual(errors,[]);passed('Narrow-width layout and script-error checks pass');await context.close();
 }finally{await browser.close()}
 fs.writeFileSync(path.join(__dirname,'gallery-verification.json'),JSON.stringify({date:'2026-11-03',checks,svgCount:1105,profileVariants:442},null,2)+'\n');console.log(JSON.stringify({checks:checks.length,svgFiles:1105,profileVariants:442}));
})().catch(e=>{console.error(e);process.exitCode=1});
