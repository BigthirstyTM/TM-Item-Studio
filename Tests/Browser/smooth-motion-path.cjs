const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
const fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 try {
 const page=await browser.newPage({viewport:{width:1500,height:1000},acceptDownloads:true});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(__dirname,'Fixtures/Approved/ObstaclePusher4mLevel1.Item.Gbx'));
 await page.waitForFunction(()=>typedMotions.length===1);
 await page.getByLabel('Ident.Id / File name',{exact:true}).fill('TMIS_Smooth_Proof');
 await page.getByLabel('Ident.Id / File name',{exact:true}).press('Tab');
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
 const panel=page.getByTestId('draw-motion-path');await panel.locator('summary').click();
 await page.locator('#pathRepeat').selectOption('Closed');
 await page.locator('#pathCoordinates').fill('0 8 0\n24 8 0\n24 8 24\n0 8 24');
 await page.getByRole('button',{name:'Use coordinates',exact:true}).click();
 const before=await page.evaluate(()=>JSON.stringify(currentPayload));
 await page.getByTestId('smooth-motion-path').locator('summary').click();
 await page.getByRole('button',{name:'Preview smooth path',exact:true}).click();
 await page.waitForFunction(()=>drawPathDraft?.plan.curved);
 assert.equal(await page.evaluate(()=>JSON.stringify(currentPayload)),before);
 assert.equal(await page.evaluate(()=>drawPathLine.material.color.getHex()),0x22d3ee);
 await page.getByLabel('Path smoothness',{exact:true}).fill('100');
 await page.waitForFunction(()=>Math.abs(drawPathDraft.plan.start.z-10.8)<.01);
 await page.getByLabel('Curve detail',{exact:true}).selectOption('4');
 await page.waitForFunction(()=>drawPathDraft.plan.legs.length===20);
 const plan=await page.evaluate(()=>drawPathDraft.plan);
 const samples=await page.evaluate(()=>{
   const d=drawPathDraft;const p=new THREE.Vector3(d.plan.start.x,d.plan.start.y,d.plan.start.z);
   return d.plan.legs.map(l=>{const delta=new THREE.Vector3(l.delta.x,l.delta.y,l.delta.z);const target=p.clone().addScaledVector(delta,.5);p.add(delta);return {t:l.startMs+l.moveMs/2,target:target.toArray()};});
 });
 await page.getByRole('button',{name:'Apply smooth path',exact:true}).click();
 await panel.getByText(/^Applied /).waitFor();
 assert.equal(await page.evaluate(()=>drawPathDraft),null);
 const actual=await page.evaluate(samples=>{
   isPlaying=false;const motion=typedMotions.find(m=>(motionGroups.get(m.childPath)?.[0].children.length||0)>1);
   return samples.map(s=>{animTime=s.t;applyTypedMotion();scene.updateMatrixWorld(true);return new THREE.Box3().setFromObject(motionGroups.get(motion.childPath)[0]).getCenter(new THREE.Vector3()).toArray();});
 },samples);
 samples.forEach((s,i)=>s.target.forEach((v,a)=>assert.ok(Math.abs(v-actual[i][a])<.002)));
 const out=path.resolve(__dirname,'../../artifacts/path-collision');fs.mkdirSync(out,{recursive:true});
 const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();
 const download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();
 await (await download).saveAs(path.join(out,'TMIS_Smooth_Proof.Item.Gbx'));
 fs.writeFileSync(path.join(out,'smooth-preview.json'),JSON.stringify({plan,samples,actual},null,2));
 await page.screenshot({path:path.join(out,'smooth-controls.png')});
 assert.deepEqual(errors,[]);
 console.log('PASS smooth live preview, curve display, immutable draft, applied midpoint parity and export');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
