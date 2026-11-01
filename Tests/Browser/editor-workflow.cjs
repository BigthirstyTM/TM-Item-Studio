const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const page=await browser.newPage({viewport:{width:1500,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.resolve('artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx'));
 const nav=page.getByRole('navigation',{name:'Item editing workflow'});await nav.waitFor();
 const buttons=nav.getByRole('button');assert.deepEqual(await buttons.allTextContents(),['1. Identity & icon','2. Model & materials','3. Placement & pivots','4. Collision','5. Motion & paths']);
 for(let i=0;i<5;i++){await buttons.nth(i).click();await page.waitForFunction(n=>document.querySelector('[data-testid="editing-section-intro"] strong')?.textContent.startsWith(String(n)),i+1);assert.equal(await buttons.nth(i).getAttribute('aria-expanded'),'true');}
 const search=page.getByLabel('Search Editing section',{exact:true}),select=page.getByLabel('Editing section',{exact:true});
 await search.fill('flag');await select.selectOption('Flag tools');await page.getByTestId('editing-section-intro').getByText('Flag tools',{exact:true}).waitFor();
 assert.equal(await select.locator('option').count(),2,'search should show placeholder and matching tool');
 await search.fill('');for(const section of ['Lights & atmosphere','Audio & effects','Instances & macroblocks','Sockets (inspection)']){await select.selectOption(section);await page.getByTestId('editing-section-intro').getByText(section,{exact:true}).waitFor();}
 await buttons.nth(4).click();await page.getByTestId('draw-motion-path').waitFor();await page.getByTestId('model-route-editor').waitFor();
 await page.locator('.studio-inspector').screenshot({path:path.resolve('artifacts/collision-arrays/editor-workflow.png')});
 await page.setViewportSize({width:800,height:900});assert.ok(await nav.boundingBox());
 assert.deepEqual(errors,[]);console.log('PASS ordered editor sections, active state, searchable optional tools, motion tools and responsive navigation');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
