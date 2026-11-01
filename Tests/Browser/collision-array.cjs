const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true});const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 const out=path.resolve('artifacts/collision-arrays');await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.resolve('artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx'));await page.waitForFunction(()=>typedMotions.length===1);
 const id=page.getByLabel('Ident.Id / File name',{exact:true});await id.fill('TMIS_CA_Browser.Item.Gbx');await id.press('Tab');
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();await page.getByLabel('Load route draft',{exact:false}).setInputFiles(path.join(out,'TMIS_CA_Scaled.json'));await page.waitForFunction(()=>drawPathDraft?.instances?.length===3);
 const array=page.getByTestId('path-array-editor');await array.locator(':scope > summary').click();await page.getByText(/Experimental physical copies:/).waitFor();
 for(const [field,value] of [['Spread X (m)','1'],['Spread Y (m)','0.5'],['Spread Z (m)','1']]){const control=array.getByLabel('Array '+field,{exact:true});await control.fill(value);await control.press('Tab');}
 await page.getByLabel('Path anchor',{exact:true}).selectOption('origin');
 const timeline=page.getByLabel('Preview timeline',{exact:true});await timeline.fill('12000');await page.waitForFunction(()=>drawPathDraft.paused);const before=await page.evaluate(()=>drawPathDraft.instances.map(i=>i.ghost.matrix.toArray()));assert.notDeepEqual(before[0],before[1]);
 const layers=page.getByTestId('path-layers-editor');await layers.locator(':scope > summary').click();await page.getByText(/Experimental physical layers are sampled/).waitFor();
 await layers.getByRole('button',{name:'Add animation layer',exact:true}).click();await page.waitForFunction(()=>drawPathDraft.plan.collisionPoses?.length===128);
 assert.deepEqual(await layers.getByLabel('Layer 1 movement').locator('option').allTextContents(),['Translate','Rotate','Spin']);
 for(const kind of ['Translate','Spin','Rotate']){await layers.getByLabel('Layer 1 movement').selectOption(kind);await page.waitForFunction(()=>drawPathDraft.plan.collisionPoses?.length>0);}
 await layers.getByRole('button',{name:'Remove layer 1',exact:true}).click();await page.waitForFunction(()=>!drawPathDraft.plan.collisionPoses);
 await page.getByLabel('Sample route tilt into collision',{exact:true}).check();await page.waitForFunction(()=>drawPathDraft.plan.collisionPoses?.length>0);
 await page.getByLabel('Sample route tilt into collision',{exact:true}).uncheck();await page.waitForFunction(()=>!drawPathDraft.plan.collisionPoses);
 await page.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
 const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();const download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();await(await download).saveAs(path.join(out,'TMIS_CA_Browser.Item.Gbx'));
 assert.deepEqual(errors,[]);console.log('PASS physical array, scale preview, scrub, experimental layers, sampled tilt, warning, Apply and export');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
