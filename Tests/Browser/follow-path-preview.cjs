const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
const fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 try {
 const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:58582/',{waitUntil:'networkidle'});
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(__dirname,'Fixtures/Approved/ObstaclePusher4mLevel1.Item.Gbx'));
 await page.waitForFunction(()=>typedMotions.length===1);
 await page.getByLabel('Ident.Id / File name',{exact:true}).fill('TMIS_FollowPath_Proof');
 await page.getByLabel('Ident.Id / File name',{exact:true}).press('Tab');
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
 const panel=page.getByTestId('draw-motion-path');await panel.locator('summary').click();
 assert.equal(await page.locator('#pathUse').inputValue(),'Scenery');
 assert.equal(await page.getByLabel('Path anchor',{exact:true}).locator('option:checked').innerText(),'Mesh centre follows path');
 await page.locator('#pathSpeed').fill('8');await page.locator('#pathSpeed').press('Tab');
 const points='0 4 0\n12 4 0\n20 4 8\n20 8 20\n12 8 28\n0 4 28\n-8 4 20\n-8 4 8';
 await page.locator('#pathCoordinates').fill(points);
 await panel.getByRole('button',{name:'Use coordinates',exact:true}).click();
 const before=await page.evaluate(()=>JSON.stringify(currentPayload));
 await panel.getByRole('button',{name:'Preview path',exact:true}).click();
 await page.waitForFunction(()=>!!drawPathDraft);
 assert.equal(await page.evaluate(()=>JSON.stringify(currentPayload)),before,'Draft must not replace authored graph');
 // Check the visible mesh centre at every segment midpoint, not only carrier count.
 const samples=await page.evaluate(()=>{
  const d=drawPathDraft;const expected=new THREE.Vector3(d.plan.start.x,d.plan.start.y,d.plan.start.z);
  return d.plan.legs.map(leg=>{
   const delta=new THREE.Vector3(leg.delta.x,leg.delta.y,leg.delta.z);
   applyDrawPathPreview(d.started+leg.startMs+leg.moveMs/2);
   scene.updateMatrixWorld(true);
   const actual=new THREE.Box3().setFromObject(d.ghost).getCenter(new THREE.Vector3());
   const target=expected.clone().addScaledVector(delta,.5);expected.add(delta);
   return {timeMs:leg.startMs+leg.moveMs/2,actual:actual.toArray(),target:target.toArray(),error:actual.distanceTo(target)};
  });
 });
 assert.ok(samples.every(x=>x.error<.001),JSON.stringify(samples));
 await page.getByLabel('Path anchor',{exact:true}).selectOption('origin');
 await page.waitForFunction(()=>drawPathDraft.anchor.length()===0);
 await page.getByLabel('Path anchor',{exact:true}).selectOption('centre');
 await page.waitForFunction(()=>drawPathDraft.anchor.length()>1);
 await panel.getByRole('button',{name:'Start drawing',exact:true}).click();
 await page.evaluate(()=>{setStudioView('top');controls.update();});
 const point=await page.evaluate(()=>{
  scene.updateMatrixWorld(true);camera.updateMatrixWorld(true);
  const p=drawPathPoints[0].clone().project(camera),r=renderer.domElement.getBoundingClientRect();
  return {x:r.x+(p.x+1)*r.width/2,y:r.y+(1-p.y)*r.height/2};
 });
 await page.mouse.move(point.x,point.y);await page.mouse.down();
 await page.mouse.move(point.x+30,point.y,{steps:5});await page.mouse.up();
 await page.waitForFunction(()=>Math.abs(getDrawnPathPoints()[0].x)>.01);
 assert.equal(await page.evaluate(()=>getDrawnPathPoints().length),8,'Dragging must move, not append a point');
 await panel.getByRole('button',{name:'Stop drawing',exact:true}).click();
 // Editing coordinates while previewing must rebuild the preview without applying it.
 await page.locator('#pathCoordinates').fill(points.replace('12 4 0','14 4 0'));
 await panel.getByRole('button',{name:'Use coordinates',exact:true}).click();
 await page.waitForFunction(()=>drawPathDraft?.plan.legs[0].delta.x===14);
 await page.locator('#pathCoordinates').fill(points);
 await panel.getByRole('button',{name:'Use coordinates',exact:true}).click();
 await panel.getByRole('button',{name:'Apply path',exact:true}).click();
 await panel.getByText(/^Applied /).waitFor();
 assert.equal(await page.evaluate(()=>drawPathDraft),null);
 assert.equal(await page.evaluate(()=>typedMotions.length),9);
 const applied=await page.evaluate(samples=>{
  isPlaying=false;
  const visible=typedMotions.find(m=>(motionGroups.get(m.childPath)?.[0].children.length || 0)>1);
  const times=[0,...samples.map(s=>s.timeMs)];
  return times.map(t=>{animTime=t;applyTypedMotion();scene.updateMatrixWorld(true);return new THREE.Box3().setFromObject(motionGroups.get(visible.childPath)[0]).getCenter(new THREE.Vector3()).toArray();});
 },samples);
 assert.ok(Math.abs(applied[0][0])<.001&&Math.abs(applied[0][1]-4)<.001&&Math.abs(applied[0][2])<.001,JSON.stringify(applied));
 for(let i=0;i<samples.length;i++) for(let axis=0;axis<3;axis++) assert.ok(Math.abs(applied[i+1][axis]-samples[i].target[axis])<.001,JSON.stringify(applied));
 const out=path.resolve(__dirname,'../../artifacts/follow-path');fs.mkdirSync(out,{recursive:true});
 const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();
 const download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();
 await (await download).saveAs(path.join(out,'TMIS_FollowPath_Proof.Item.Gbx'));
 fs.writeFileSync(path.join(out,'preview-samples.json'),JSON.stringify({samples,applied},null,2));
 await page.screenshot({path:path.join(out,'preview-controls.png')});
 assert.deepEqual(errors,[]);
 console.log('PASS eight-point preview, all midpoints, live coordinate changes, non-mutating draft, centred applied motion and proof export');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
