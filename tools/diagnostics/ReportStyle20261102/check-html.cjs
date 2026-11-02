const fs=require('fs'),path=require('path'),crypto=require('crypto'),{pathToFileURL}=require('url');
const {chromium}=require('playwright');
(async()=>{
 const root='D:/optical system design/opticalsystem/output/report-style-gauss28-20261102',qa=path.join(root,'qa');fs.mkdirSync(qa,{recursive:true});
 const file=path.join(root,'Reports/Double Gauss 28 degree field_full_report.html');
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'}),errors=[];
 try{
  const page=await browser.newPage({viewport:{width:1600,height:1200},deviceScaleFactor:1});page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')errors.push(m.text())});
  await page.goto(pathToFileURL(file).href,{waitUntil:'load',timeout:90000});await page.evaluate(()=>document.fonts.ready);
  const sheets=page.locator('.document-page'),count=await sheets.count();
  const bounds=async()=>sheets.evaluateAll(nodes=>nodes.map(n=>{const p=n.getBoundingClientRect(),b=n.querySelector('.page-body').getBoundingClientRect(),f=n.querySelector('.page-footer').getBoundingClientRect();return{page:Number(n.dataset.page),title:n.querySelector('h1')?.textContent,width:p.width,bodyBottom:b.bottom-p.top,footerTop:f.top-p.top,overlap:b.bottom>f.top+1,overwide:n.scrollWidth>n.clientWidth+1}}));
  const desktop=await bounds();await page.screenshot({path:path.join(qa,'html-desktop.png')});
  const hide=await page.addStyleTag({content:'.viewer-tools,.top-progress{visibility:hidden!important}'});
  for(const n of [1,2,5,6,10,...await page.locator('.document-page:has(.formula-cell)').evaluateAll(ns=>ns.map(n=>Number(n.dataset.page)))])await sheets.nth(n-1).screenshot({path:path.join(qa,`html-page-${String(n).padStart(2,'0')}.png`)});
  await hide.evaluate(n=>n.remove());
  const sourceHashes=new Set(fs.readdirSync(path.join(root,'capture/source-images')).filter(n=>n.endsWith('.png')).map(n=>crypto.createHash('sha256').update(fs.readFileSync(path.join(root,'capture/source-images',n))).digest('hex')));
  const figures=await page.locator('.document-page figure img').evaluateAll(ns=>ns.map(n=>({w:n.naturalWidth,h:n.naturalHeight,src:n.src})));
  for(const im of figures){if(im.w!==2404||im.h!==1804||!sourceHashes.has(crypto.createHash('sha256').update(Buffer.from(im.src.split(',')[1],'base64')).digest('hex')))throw Error('Original diagram changed');delete im.src;}
  if(await page.locator('.document-page .formula-cell').count()!==111)throw Error('Missing Zernike formulas');
  await page.locator('#records-trigger').click();if(!await page.locator('#records-dialog').isVisible())throw Error('Calculation records failed');await page.keyboard.press('Escape');
  await page.locator('.figure-open').first().click();await page.locator('#native-size').click();if(await page.locator('#native-size').getAttribute('aria-pressed')!=='true')throw Error('Native image failed');await page.keyboard.press('Escape');
  await page.setViewportSize({width:1100,height:1000});const medium=await bounds();
  await page.setViewportSize({width:390,height:844});await page.goto(pathToFileURL(file).href,{waitUntil:'load'});await page.screenshot({path:path.join(qa,'html-mobile.png')});
  if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1))throw Error('Mobile document overflow');
  await page.locator('#outline-toggle').click();await page.locator(`.outline a[data-page="${count}"]`).click();await page.waitForTimeout(2500);if(!await page.locator('#next-page').isDisabled())throw Error('Last-page navigation failed');
  await page.setViewportSize({width:1600,height:1200});await page.emulateMedia({media:'print'});const print=await bounds();
  const widths=await page.locator('.document-page figure img').evaluateAll(ns=>ns.map(n=>n.getBoundingClientRect().width));if(widths.some(w=>Math.abs(w-184.6*96/25.4)>1))throw Error('Printed diagram size differs');
  const issues=Object.entries({desktop,medium,print}).flatMap(([mode,rows])=>rows.filter(n=>n.overlap||n.overwide).map(n=>({mode,...n})));
  const result={status:issues.length||errors.length?'needs-repair':'passed',pages:count,formulas:111,figures:figures.length,desktop,medium,print,issues,errors};fs.writeFileSync(path.join(qa,'html-inspection.json'),JSON.stringify(result,null,2));
  if(issues.length||errors.length)throw Error(JSON.stringify({issues,errors}));
  console.log(JSON.stringify({status:'passed',pages:count,formulas:111,figures:figures.length}));
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
