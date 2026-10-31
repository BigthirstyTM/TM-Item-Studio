const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 try {
  const page=await browser.newPage({acceptDownloads:true});
  await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:58582/',{waitUntil:'networkidle'});
  await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(__dirname,'Fixtures/Approved/ObstaclePusher4mLevel1.Item.Gbx'));
  await page.waitForFunction(()=>typedMotions.length===1);
  await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
  const panel=page.getByTestId('draw-motion-path');await panel.locator('summary').click();
  await page.locator('#pathUse').selectOption('CollisionRelay');
  await page.getByTestId('collision-relay-warning').waitFor();
  await page.locator('#pathCoordinates').fill('0 0 0\n20 0 0\n20 4 20');
  await panel.getByRole('button',{name:'Use coordinates',exact:true}).click();
  const before=await page.evaluate(()=>JSON.stringify(currentPayload));
  await panel.getByRole('button',{name:'Preview path',exact:true}).click();
  await page.waitForFunction(()=>!!drawPathDraft);
  assert.equal(await page.evaluate(()=>JSON.stringify(currentPayload)),before);
  await panel.getByRole('button',{name:'Apply path',exact:true}).click();
  await page.getByText(/Both collision shapes must be finite/).waitFor();
  assert.equal(await page.evaluate(()=>JSON.stringify(currentPayload)),before,'Refusal mutated the item');
  if(!process.env.STUDIO_RELAY_FIXTURE) throw new Error('Set STUDIO_RELAY_FIXTURE to a supported static triangle item for the native export test');
  await page.getByLabel('Open item files',{exact:true}).setInputFiles(process.env.STUDIO_RELAY_FIXTURE);
  await page.waitForFunction(()=>typedMotions.length===0);
  if(!await panel.locator('#pathUse').isVisible()) await panel.locator('summary').click();
  await page.locator('#pathUse').selectOption('CollisionRelay');
  await page.locator('#pathCoordinates').fill('0 0 0\n20 0 0\n20 4 20');
  await panel.getByRole('button',{name:'Use coordinates',exact:true}).click();
  await panel.getByRole('button',{name:'Apply path',exact:true}).click();
  try { await panel.getByText(/^Applied /).waitFor(); }
  catch(e) { console.error((await page.locator('body').innerText()).slice(-16000)); throw e; }
  await page.getByLabel('Edited collision start delay',{exact:true}).fill('4000');
  await page.getByLabel('Edited collision start delay',{exact:true}).press('Tab');
  await page.getByTestId('path-segment-editor').getByRole('button',{name:'Apply edited route',exact:true}).click();
  assert.ok(await page.evaluate(()=>typedMotions.length>1));
  const stableAnchor=await page.evaluate(()=>getDrawPathAnchor(true));
  await page.getByTestId('path-segment-editor').getByRole('button',{name:'Preview edited route',exact:true}).click();
  await page.waitForFunction(()=>!!drawPathDraft);
  assert.deepEqual(await page.evaluate(()=>getDrawPathAnchor(true)),stableAnchor);
  const previewError=await page.evaluate(()=>{
    const d=drawPathDraft,l=d.plan.legs[0];
    applyDrawPathPreview(d.started+l.startMs+l.moveMs/2);scene.updateMatrixWorld(true);
    const actual=new THREE.Box3().setFromObject(d.ghost).getCenter(new THREE.Vector3());
    return actual.distanceTo(new THREE.Vector3(d.plan.start.x+l.delta.x/2,d.plan.start.y+l.delta.y/2,d.plan.start.z+l.delta.z/2));
  });
  assert.ok(previewError<.001,'Reapplied relay preview drifted: '+previewError);
  await page.getByTestId('path-segment-editor').getByRole('button',{name:'Apply edited route',exact:true}).click();
  assert.deepEqual(await page.evaluate(()=>getDrawPathAnchor(true)),stableAnchor);
  const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();
  const download=page.waitForEvent('download');
  await page.getByRole('button',{name:/Export selected file/}).click();
  await(await download).saveAs(path.resolve(__dirname,'../../artifacts/collision-experimental/BrowserRelay.Item.Gbx'));
  const draftDownload=page.waitForEvent('download');
  await page.getByRole('button',{name:'Save route draft (.json)',exact:true}).click();
  await(await draftDownload).saveAs(path.resolve(__dirname,'../../artifacts/collision-experimental/route.motion-path.json'));
  await page.getByRole('button',{name:'Restore previous motion',exact:true}).click();
  await page.waitForFunction(()=>typedMotions.length===0);
  console.log('PASS experimental relay preview, apply, timing edit and export');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
