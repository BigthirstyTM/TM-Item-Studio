const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.resolve('artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx'));await page.waitForFunction(()=>typedMotions.length===1);
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
 const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
 await page.getByLabel('Path use',{exact:true}).selectOption('Scenery');await page.getByLabel('Path repeat',{exact:true}).selectOption('Closed');
 await page.getByLabel('Path coordinates',{exact:true}).fill('0 0 0\n20 0 0\n20 0 20\n0 0 20');await page.getByRole('button',{name:'Use coordinates',exact:true}).click();await page.waitForFunction(()=>drawPathDraft?.plan?.legs.length===4);
 const bezier=page.getByTestId('bezier-editor');await bezier.locator(':scope > summary').click();await page.getByLabel('Bezier detail',{exact:true}).selectOption('2');await page.getByLabel('Enable Bezier handles',{exact:true}).check();
 await page.waitForFunction(()=>drawPathDraft.plan.legs.length===8&&bezierGroup?.children.length===16);
 // Exercise handle editing with a real drag in the locked top view.
 await page.evaluate(()=>{setDrawPathView('top');fitDrawPath();});
 const handle=await page.evaluate(()=>{const marker=bezierGroup.children.find(m=>m.userData.bezier?.i===1&&m.userData.bezier.key==='out'),p=marker.position.clone().project(camera),r=renderer.domElement.getBoundingClientRect();return {x:r.left+(p.x+1)*r.width/2,y:r.top+(1-p.y)*r.height/2};});
 const oldPlan=await page.evaluate(()=>JSON.stringify(drawPathDraft.plan.legs));const before=await page.evaluate(()=>JSON.stringify(bezierState.handles));await page.mouse.move(handle.x,handle.y);await page.mouse.down();await page.mouse.move(handle.x+30,handle.y+20,{steps:5});await page.mouse.up();
 await page.waitForFunction(old=>JSON.stringify(drawPathDraft.plan.legs)!==old,oldPlan);
 const edited=await page.evaluate(()=>JSON.stringify(bezierState.handles));await page.getByRole('button',{name:'Undo path edit',exact:true}).click();await page.waitForFunction(old=>{const expected=JSON.parse(old);return bezierState.handles.every((h,i)=>['in','out'].every(k=>['x','y','z'].every(a=>Math.abs(h[k][a]-expected[i][k][a])<.0001)));},before);await page.getByRole('button',{name:'Redo path edit',exact:true}).click();await page.waitForFunction(old=>{const expected=JSON.parse(old);return bezierState.handles.every((h,i)=>['in','out'].every(k=>['x','y','z'].every(a=>Math.abs(h[k][a]-expected[i][k][a])<.0001)));},edited);
 const look=page.getByTestId('look-at-editor');await look.locator(':scope > summary').click();await page.getByLabel('Look-at X',{exact:true}).fill('10');await page.getByLabel('Look-at Y',{exact:true}).fill('8');await page.getByLabel('Look-at Z',{exact:true}).fill('10');await page.getByLabel('Enable look-at target',{exact:true}).check();
 await page.waitForFunction(()=>lookAtMarker&&drawPathDraft.plan.rotations?.length===9);
 const array=page.getByTestId('path-array-editor');await array.locator(':scope > summary').click();await array.getByLabel('Array Delay (seconds)',{exact:true}).fill('0');await array.getByLabel('Array Copies',{exact:true}).fill('2');await page.waitForFunction(()=>drawPathDraft.instances.length===2);
 const clearance=page.getByTestId('clearance-editor');await clearance.locator(':scope > summary').click();await page.getByLabel('Clearance samples',{exact:true}).selectOption('16');await page.getByRole('button',{name:'Check full route',exact:true}).click();await clearance.getByText(/possible conflicts across 16/).waitFor();
 assert.ok(await page.evaluate(()=>clearanceGroup.children.length>0),'overlapping followers were missed');
 await page.getByLabel('Clearance margin',{exact:true}).fill('-2');await page.getByLabel('Clearance margin',{exact:true}).press('Tab');await page.waitForFunction(()=>clearanceMargin===-2&&!clearanceBusy);
 const spacing=await page.evaluate(async()=>{
  const instance=drawPathDraft.instances[1],old=instance.plan;
  try{instance.plan={...old,start:{...old.start,x:old.start.x+1000}};return await checkPathClearance(10,16);}
  finally{instance.plan=old;configureClearance(-2,16);}
 });assert.equal(spacing.count,0,'widely separated followers were falsely flagged');
 const reference=page.getByTestId('model-route-editor');await reference.locator(':scope > summary').click();await page.getByLabel('Load route reference',{exact:true}).setInputFiles(path.resolve('artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx'));await reference.getByText(/Reference loaded/).waitFor();await page.getByLabel('Reference offset Y',{exact:true}).fill('-10');await page.getByLabel('Reference offset Y',{exact:true}).press('Tab');await page.waitForFunction(()=>routeReferenceOffset.y===-10);
 await page.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
 const project=path.resolve('artifacts/collision-arrays/Authoring-Proof.tmisproject');let download=page.waitForEvent('download');await page.getByRole('button',{name:'Save Studio project',exact:true}).click();await(await download).saveAs(project);
 const state=await page.evaluate(()=>JSON.parse(getPathAuthoringState()));
 await page.reload({waitUntil:'networkidle'});await page.getByLabel('Open Studio project',{exact:true}).setInputFiles(project);
 await page.getByText(/Project restored/).waitFor();await page.waitForFunction(()=>drawPathDraft?.instances?.length===2&&lookAtMarker&&bezierState.enabled);
 assert.equal(await page.evaluate(()=>routeReferenceOffset.y),-10);assert.ok(await page.evaluate(()=>routeReferenceParts.length>0));
 assert.deepEqual(await page.evaluate(()=>bezierState.handles),state.bezier.handles);assert.equal(await page.evaluate(()=>clearanceMargin),-2);
 await page.getByLabel('Open Studio project',{exact:true}).setInputFiles({name:'broken.tmisproject',mimeType:'application/octet-stream',buffer:Buffer.from('not a zip')});await page.getByText(/Could not open project/).waitFor();assert.equal(await page.evaluate(()=>drawPathDraft.instances.length),2,'invalid project replaced the open item');
 const reopened=page.getByTestId('draw-motion-path');await reopened.locator(':scope > summary').click();await page.getByRole('button',{name:'Apply path',exact:true}).click();await reopened.getByText(/^Applied /).waitFor();
 const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();await(await download).saveAs(path.resolve('artifacts/collision-arrays/Authoring-Proof.Item.Gbx'));
 await page.screenshot({path:path.resolve('artifacts/collision-arrays/authoring-editor.png'),fullPage:true});
 assert.deepEqual(errors,[]);console.log('PASS Bezier handle drag, look-at, overlap margin, project reload of applied path, reapply and GBX export');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
