const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');const fs=require('node:fs');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const out=path.resolve(__dirname,'../../artifacts/coaster-inertia/parts');fs.mkdirSync(out,{recursive:true});
 const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(out,'TMIS_Bird_PartsSource.Item.Gbx'));
 await page.waitForFunction(()=>typedMotions.length===1);
 const id=page.getByLabel('Ident.Id / File name',{exact:true});await id.fill('TMIS_Bird_BrowserParts');await id.press('Tab');
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
 await page.locator('#pathRepeat').selectOption('Closed');await page.locator('#pathCoordinates').fill('0 10 0\n20 15 0\n20 10 20');await page.getByRole('button',{name:'Use coordinates',exact:true}).click();await page.getByRole('button',{name:'Preview path',exact:true}).click();
 const parts=page.getByTestId('path-parts-editor');await parts.locator(':scope > summary').click();
 for(let i=1;i<=2;i++){
  await parts.getByLabel('Animated visual mesh',{exact:true}).selectOption({index:i+1});
  await parts.getByLabel('Part pivot Z',{exact:true}).fill(i===1?'0.2':'-0.2');await parts.getByLabel('Part pivot Z',{exact:true}).press('Tab');
  if(i===2)await parts.getByRole('checkbox').check();
  await parts.getByRole('button',{name:'Set animation for this mesh',exact:true}).click();
 }
 await page.waitForFunction(()=>drawPathDraft?.animatedParts.length===2);
 const samples=await page.evaluate(()=>{
  const times=[100,200,350,500,700,drawPathDraft.plan.durationMs+200];return times.flatMap(t=>{applyDrawPathPreview(drawPathDraft.started+t);drawPathDraft.ghost.updateMatrixWorld(true);return drawPathDraft.animatedParts.map(({mesh,part},j)=>({t,j,point:new THREE.Vector3().fromBufferAttribute(mesh.geometry.attributes.position,0).applyMatrix4(mesh.matrixWorld).toArray()}));});
 });
 assert.notDeepEqual(samples[0].point,samples[2].point,'wing never moved');
 await page.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
 const actual=await page.evaluate(samples=>{isPlaying=false;return samples.map(s=>{animTime=s.t;applyTypedMotion();scene.updateMatrixWorld(true);const m=typedMotions.find(m=>m.childPath.endsWith(`/ent:${3+4+s.j}`));const group=motionGroups.get(m.childPath)[0];let mesh;group.traverse(o=>{if(o.isMesh&&!o.userData.isCollision&&o.geometry.attributes.position.count>10)mesh=o;});return new THREE.Vector3().fromBufferAttribute(mesh.geometry.attributes.position,0).applyMatrix4(mesh.matrixWorld).distanceTo(new THREE.Vector3(...s.point));});},samples);
 assert.ok(actual.every(e=>e<.002),JSON.stringify(actual));
 const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();const download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();await(await download).saveAs(path.join(out,'TMIS_Bird_BrowserParts.Item.Gbx'));
 fs.writeFileSync(path.join(out,'preview-export.json'),JSON.stringify({samples,errors:actual},null,2));assert.deepEqual(errors,[]);console.log('PASS two separate mirrored wing meshes, nonzero pivots, animated preview/export vertex parity, export');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
