const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 try {
  const page=await browser.newPage({viewport:{width:1500,height:1000}});
  await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:59371/',{waitUntil:'networkidle'});
  await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(__dirname,'Fixtures/Approved/TM2020/getting_over_it_hammer.Item.Gbx'));
  await page.waitForFunction(()=>!!currentPayload);
  const before=await page.evaluate(()=>JSON.stringify(currentPayload));
  const originalId=await page.getByLabel('Ident.Id / File name',{exact:true}).inputValue();
  await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
  const panel=page.getByTestId('draw-motion-path');await panel.locator('summary').click();
  const points=Array.from({length:128},(_,i)=>[100*Math.cos(i*Math.PI/64),5+3*Math.sin(i*Math.PI/32),100*Math.sin(i*Math.PI/64)]);
  await page.locator('#pathCoordinates').fill(points.map(v=>v.join(' ')).join('\n'));
  await panel.getByRole('button',{name:'Use coordinates',exact:true}).click();
  assert.equal(await page.evaluate(()=>getDrawnPathPoints().length),128);
  await panel.getByRole('button',{name:'Preview path',exact:true}).click();await page.waitForFunction(()=>!!drawPathDraft);
  assert.equal(await page.evaluate(()=>JSON.stringify(currentPayload)),before);
  await panel.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
  assert.equal(await page.evaluate(()=>typedMotions.length),129);
  await panel.getByRole('button',{name:'Restore previous motion',exact:true}).click();
  await page.waitForFunction(()=>typedMotions.length===0);
  await page.getByRole('button',{name:'Item Identification & Collector Metadata',exact:true}).click();
  assert.equal(await page.getByLabel('Ident.Id / File name',{exact:true}).inputValue(),originalId);
  console.log('PASS 128-point draft, full static document conversion and restore');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
