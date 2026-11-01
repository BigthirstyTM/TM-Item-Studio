const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
const fs=require('node:fs');
(async()=>{
 const out=path.resolve(__dirname,'../../artifacts/coaster-flight/profile');fs.mkdirSync(out,{recursive:true});
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 try {
  const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
  await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.resolve(__dirname,'../../artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx'));
  await page.waitForFunction(()=>typedMotions.length===1);
  const id=page.getByLabel('Ident.Id / File name',{exact:true});await id.fill('TMIS_Profile_RampPose');await id.press('Tab');
  await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
  const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
  await page.locator('#pathRepeat').selectOption('Closed');
  await page.locator('#pathCoordinates').fill('0 0 0\n30 5 0\n30 5 30\n0 0 30');
  await page.getByRole('button',{name:'Use coordinates',exact:true}).click();
  await page.getByRole('button',{name:'Preview path',exact:true}).click();
  await page.waitForFunction(()=>!!drawPathDraft);
  const rotation=page.getByTestId('path-rotation-editor');await rotation.locator('summary').click();
  await rotation.getByRole('button',{name:'Follow route direction',exact:true}).click();
  await page.waitForFunction(()=>!!drawPathDraft.plan.rotations);
  const roll=rotation.getByLabel('Point 2 Roll',{exact:true});await roll.fill('30');await roll.press('Tab');
  await page.waitForFunction(()=>drawPathDraft.plan.rotations[1].roll===30);
  const speed=page.getByTestId('path-speed-editor');await speed.locator(':scope > summary').click();
  await speed.getByRole('button',{name:'Enable speed curve',exact:true}).click();
  await speed.getByRole('button',{name:'Speed point 2',exact:true}).click();
  await speed.getByLabel('Curve point speed',{exact:true}).fill('15');await speed.getByLabel('Curve point speed',{exact:true}).press('Tab');
  await page.waitForFunction(()=>drawPathDraft.plan.legs.length>4);
  assert.match(await page.getByLabel('Path segment',{exact:true}).inputValue(),/^Segment 2:/);
  await speed.getByText('Speed ramp & presets',{exact:true}).click();
  await speed.getByLabel('Ramp to point',{exact:true}).selectOption({label:'Point 3'});
  await speed.getByRole('button',{name:'Apply speed ramp',exact:true}).click();
  await page.waitForTimeout(150);
  // Save the editable profile, not the subdivided export; restoration must be exact.
  const save=page.waitForEvent('download');await page.getByRole('button',{name:'Save route draft (.json)',exact:true}).click();
  const draft=path.join(out,'ramp-pose.motion-path.json');await(await save).saveAs(draft);
  const document=JSON.parse(fs.readFileSync(draft));assert.ok(document.SpeedPoints || document.speedPoints);
  const compiled=await page.evaluate(()=>drawPathDraft.plan);
  await page.getByLabel('Load route draft',{exact:false}).setInputFiles(draft);
  await page.waitForTimeout(150);assert.deepEqual(await page.evaluate(()=>drawPathDraft.plan),compiled);
  // Snapshot ghost transforms before Apply, then compare native-graph visual matrices.
  const expected=await page.evaluate(()=>drawPathDraft.plan.legs.map((l,i)=>{
   applyDrawPathPreview(drawPathDraft.started+l.startMs+l.moveMs/2);
   return {time:l.startMs+l.moveMs/2,matrix:drawPathDraft.ghost.matrix.toArray(),body:drawPathDraft.plan.legs.length+4*i+3};
  }));
  await page.getByTestId('path-segment-editor').screenshot({path:path.join(out,'profile-controls.png')});
  await page.getByRole('button',{name:'Apply edited route',exact:true}).click();
  await panel.getByText(/^Applied /).waitFor();
  const actual=await page.evaluate(expected=>{
   isPlaying=false;
   return expected.map(e=>{
    animTime=e.time;applyTypedMotion();scene.updateMatrixWorld(true);
    const m=typedMotions.find(m=>m.childPath.endsWith(`/ent:${e.body}`));
    if(!m)return {paths:typedMotions.map(m=>m.childPath),wanted:e.body};
    const g=motionGroups.get(m.childPath)[0];
    return Math.max(...g.matrix.elements.map((v,i)=>Math.abs(v-e.matrix[i])));
   });
  },expected);
  assert.ok(actual.every(e=>typeof e==='number'&&e<.003),JSON.stringify(actual));
  const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();
  const download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();
  await(await download).saveAs(path.join(out,'TMIS_Profile_RampPose.Item.Gbx'));
  fs.writeFileSync(path.join(out,'preview-export-comparison.json'),JSON.stringify({compiled,errors:actual},null,2));
  assert.deepEqual(errors,[]);console.log('PASS curve/ramp controls, rotation, selection sync, JSON, preview/export transform parity and item export');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
