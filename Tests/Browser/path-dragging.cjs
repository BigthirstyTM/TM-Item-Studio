const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 try {
  const page=await browser.newPage({viewport:{width:1500,height:1000}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
  await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(__dirname,'Fixtures/Approved/ObstaclePusher4mLevel1.Item.Gbx'));
  await page.waitForFunction(()=>typedMotions.length===1);
  await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
  const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
  await page.locator('#pathRepeat').selectOption('Closed');
  await page.locator('#pathCoordinates').fill('0 8 0\n20 12 0\n20 8 20');
  await page.getByRole('button',{name:'Use coordinates',exact:true}).click();
  await page.getByRole('button',{name:'Preview path',exact:true}).click();
  await page.waitForFunction(()=>!!drawPathDraft);
  assert.equal(await page.getByRole('button',{name:'Preview path',exact:true}).getAttribute('aria-pressed'),'true');
  const first=await page.evaluate(()=>drawPathDraft.ghost.matrix.elements.slice());await page.waitForTimeout(250);
  assert.notDeepEqual(await page.evaluate(()=>drawPathDraft.ghost.matrix.elements.slice()),first,'preview is frozen');
  for(const [view,depth] of [['top','y'],['front','z'],['right','x']]) {
   await page.getByRole('button',{name:`Path view ${view}`,exact:true}).click();
   await page.getByRole('button',{name:'Fit path in view',exact:true}).click();
   assert.equal(await page.getByRole('button',{name:`Path view ${view}`,exact:true}).getAttribute('aria-pressed'),'true');
   const before=await page.evaluate(()=>{camera.updateMatrixWorld();const r=renderer.domElement.getBoundingClientRect(),p=drawPathPoints[1].clone().project(camera);return{camera:camera.matrixWorld.elements.slice(),point:drawPathPoints[1].toArray(),x:r.left+(p.x+1)*r.width/2,y:r.top+(1-p.y)*r.height/2};});
   await page.mouse.move(before.x,before.y);await page.mouse.down();await page.mouse.move(before.x+35,before.y-25,{steps:12});await page.mouse.up();
   await page.waitForTimeout(200);
   const after=await page.evaluate(()=>({camera:camera.matrixWorld.elements.slice(),point:drawPathPoints[1].toArray(),enabled:controls.enabled,view:activeStudioView}));
   assert.ok(after.camera.every((v,i)=>Math.abs(v-before.camera[i])<.00001),`${view}: camera moved during point drag`);
   assert.notDeepEqual(after.point,before.point,`${view}: point did not move`);
   assert.ok(Math.abs(after.point['xyz'.indexOf(depth)]-before.point['xyz'.indexOf(depth)])<.00001,`${view}: hidden depth changed`);
   assert.equal(after.enabled,true);assert.equal(after.view,view);
  }
  const speed=page.getByTestId('path-speed-editor');await speed.locator(':scope > summary').click();
  await speed.getByRole('button',{name:'Enable speed curve',exact:true}).click();
  const dot=speed.getByRole('button',{name:'Speed point 2',exact:true});await dot.scrollIntoViewIfNeeded();
  const rect=await dot.boundingBox();const old=await page.evaluate(()=>drawPathDraft.plan.durationMs);
  await page.mouse.move(rect.x+rect.width/2,rect.y+rect.height/2);await page.mouse.down();await page.mouse.move(rect.x+rect.width/2,rect.y-20,{steps:8});await page.mouse.up();
  await page.waitForFunction(old=>drawPathDraft.plan.durationMs<old,old);
  const edited=await page.evaluate(()=>JSON.stringify(drawPathDraft.plan));
  await page.getByRole('button',{name:'Stop path preview',exact:true}).click();
  await page.waitForFunction(()=>!drawPathDraft);
  await page.getByRole('button',{name:'Preview path',exact:true}).click();
  await page.waitForFunction(()=>!!drawPathDraft);
  assert.equal(await page.evaluate(()=>JSON.stringify(drawPathDraft.plan)),edited,'Preview discarded speed edits');
  const loop=page.getByLabel('Total path loop time',{exact:true});const beforeTotal=await page.evaluate(()=>drawPathDraft.plan.durationMs);
  await loop.fill(String(beforeTotal/500));await loop.press('Tab');
  await page.waitForFunction(before=>Math.abs(drawPathDraft.plan.durationMs-2*before)<20,beforeTotal);
  await page.locator('#pathUse').selectOption('CollisionRelay');
  await page.getByLabel('Collision start delay',{exact:true}).fill('8000');await page.getByLabel('Collision start delay',{exact:true}).press('Tab');
  await page.getByRole('button',{name:'Preview path',exact:true}).click();
  await page.waitForFunction(()=>document.getElementById('draw-path-playback-status')?.textContent.includes('waiting'));
  assert.equal(await page.getByRole('button',{name:'Preview path',exact:true}).getAttribute('aria-pressed'),'true');
  assert.deepEqual(errors,[]);
  console.log('PASS locked top/front/side pointer drags, depth/camera preservation, active buttons, moving preview, speed-curve drag and preview resume');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
