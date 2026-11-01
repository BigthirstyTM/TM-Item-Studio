const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');const fs=require('node:fs');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true});const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')console.log(m.text());});
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 const out=path.resolve('artifacts/instance-layers');
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.resolve('artifacts/template-gallery/Sources/TMIS_Demo_BirdFlight_Source.Item.Gbx'));await page.waitForFunction(()=>typedMotions.length===1);
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
 await page.getByLabel('Load route draft',{exact:false}).setInputFiles(path.join(out,'BirdFlight.motion-path.json'));await page.waitForFunction(()=>drawPathDraft?.instances?.length===8);
 const playback=page.getByTestId('path-playback');assert.equal(await playback.count(),1);
 assert.ok(await playback.evaluate(el=>!!el.closest('.studio-viewport')),'timeline must be inside viewport');
 const dock=await playback.boundingBox(),canvas=await page.locator('#threeContainer').boundingBox();assert.ok(dock.y>=canvas.y+canvas.height-1,'timeline overlaps canvas');
 const array=page.getByTestId('path-array-editor');await array.locator(':scope > summary').click();
 const number=array.getByLabel('Array Copies',{exact:true});await number.fill('6');await number.press('Tab');await page.waitForFunction(()=>drawPathDraft.instances.length===6);
 await page.getByRole('button',{name:'Undo path edit',exact:true}).click();await page.waitForFunction(()=>drawPathDraft.instances.length===8).catch(async e=>{console.log(await page.locator('body').innerText());throw e;});
 await page.getByRole('button',{name:'Redo path edit',exact:true}).click();await page.waitForFunction(()=>drawPathDraft.instances.length===6);
 const timeline=page.getByLabel('Preview timeline',{exact:true});await timeline.fill('5000');await page.waitForFunction(()=>drawPathDraft.paused&&drawPathDraft.seekMs===5000);
 const before=await page.evaluate(()=>drawPathDraft.instances.map(i=>i.ghost.matrix.toArray()));await page.waitForTimeout(150);assert.deepEqual(await page.evaluate(()=>drawPathDraft.instances.map(i=>i.ghost.matrix.toArray())),before,'scrub not paused');assert.notDeepEqual(before[0],before[1],'copies overlap');
 // Live edits keep a scrubbed preview paused at the same time.
 await number.fill('7');await page.waitForFunction(()=>drawPathDraft.instances.length===7&&drawPathDraft.paused&&drawPathDraft.seekMs===5000);
 await number.fill('6');await page.waitForFunction(()=>drawPathDraft.instances.length===6&&drawPathDraft.paused&&drawPathDraft.seekMs===5000);
 const sharing=await page.evaluate(()=>{const meshes=i=>{const a=[];i.ghost.traverse(n=>{if(n.isMesh)a.push(n);});return a;};const a=meshes(drawPathDraft.instances[0]),b=meshes(drawPathDraft.instances[1]);return a.every((m,i)=>m.geometry===b[i].geometry&&m.material===b[i].material);});assert.ok(sharing,'preview duplicates geometry');
 await page.getByLabel('Show path anchor & part hinges (XYZ axes)',{exact:true}).check();assert.equal(await page.evaluate(()=>drawPathDraft.pivotHelpers.length),3);
 await page.getByRole('button',{name:'Pause or resume path',exact:true}).click();await page.waitForFunction(()=>!drawPathDraft.paused);
 const layers=page.getByTestId('path-layers-editor');await layers.locator(':scope > summary').click();await layers.getByLabel('Layer 1 enabled',{exact:true}).uncheck();await page.waitForFunction(()=>drawPathDraft.plan.layers[0].enabled===false);
 await layers.getByRole('button',{name:'Add animation layer',exact:true}).click();await page.waitForFunction(()=>drawPathDraft.plan.layers.length===3);
 await layers.getByRole('button',{name:'Remove layer 3',exact:true}).click();await page.waitForFunction(()=>drawPathDraft.plan.layers.length===2);
 const download=page.waitForEvent('download');await page.getByRole('button',{name:'Save route draft (.json)',exact:true}).click();await(await download).saveAs(path.join(out,'browser-array.motion-path.json'));const saved=JSON.parse(fs.readFileSync(path.join(out,'browser-array.motion-path.json')));assert.equal(saved.Array.Count,6);assert.equal(saved.Layers[0].Enabled,false);
 await page.getByRole('button',{name:'Stop path preview',exact:true}).click();await page.waitForFunction(()=>!drawPathDraft);await timeline.fill('3000');await page.waitForFunction(()=>drawPathDraft?.paused&&drawPathDraft.seekMs===3000);
 // A setting edit restarts the viewport after Stop, without a Preview click.
 await page.getByRole('button',{name:'Stop path preview',exact:true}).click();await page.waitForFunction(()=>!drawPathDraft);
 await page.getByLabel('Path average speed',{exact:true}).fill('4');await page.waitForFunction(()=>drawPathDraft?.instances?.length===6);
 assert.equal(await page.evaluate(()=>drawPathDraft.plan.layers.length),2,'speed edit lost layers');
 await page.evaluate(()=>setDrawnPathPoints([{x:0,y:0,z:0},{x:20,y:0,z:0},{x:20,y:0,z:20},{x:0,y:0,z:20}]));
 const smooth=page.getByTestId('smooth-motion-path');await smooth.locator(':scope > summary').click();
 await page.getByLabel('Path smoothness',{exact:true}).fill('30');await page.waitForFunction(()=>drawPathDraft?.plan?.curved===true);
 await array.screenshot({path:path.join(out,'array-ui.png')});await page.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
 assert.deepEqual(errors,[]);console.log('PASS array sharing, count, undo/redo, scrub, resume, layer mute/add/remove, draft serialization and native compilation');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
