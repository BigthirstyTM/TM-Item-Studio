const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const page=await browser.newPage({viewport:{width:1500,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.resolve('artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx'));
 await page.waitForFunction(()=>typedMotions.length===1);
 const initial=await page.evaluate(()=>{const b=visibleModelBounds();return {distance:camera.position.distanceTo(b.getCenter(new THREE.Vector3())),diagonal:b.getSize(new THREE.Vector3()).length()};});
 assert.ok(initial.distance<initial.diagonal*3,JSON.stringify(initial));
 const fit=await page.evaluate(()=>{
  const p=structuredClone(currentPayload);p.frameCamera=true;p.pivots=[{x:100000,y:100000,z:100000}];renderStudioScene(p);
  const b=visibleModelBounds(),center=b.getCenter(new THREE.Vector3()),radius=b.getSize(new THREE.Vector3()).length()/2;
  return {distance:camera.position.distanceTo(center),radius,targetError:controls.target.distanceTo(center)};
 });assert.ok(fit.distance<fit.radius*5,JSON.stringify(fit));assert.ok(fit.targetError<.001);
 for(const mood of ['Morning','Day','Evening','Night','Neutral']){
  await page.getByRole('group',{name:'Trackmania viewport mood'}).getByRole('button',{name:mood,exact:true}).click();
  await page.waitForFunction(name=>getStudioMoodInfo().name===name,mood.toLowerCase());
  if(mood!=='Neutral')assert.match(await page.evaluate(()=>getStudioMoodInfo().source),/^assets\/moods\//);
  await page.locator('#threeContainer').screenshot({path:path.resolve('artifacts/collision-arrays/mood-'+mood+'.png')});
 }
 const nav=page.getByRole('navigation',{name:'Item editing workflow'});
 for(const name of ['Lights','Effects','Instances','Flags','Sockets'])assert.ok(await nav.getByRole('button',{name,exact:true}).isVisible(),name);
 assert.deepEqual(errors,[]);console.log('PASS visible-model fit ignores distant gizmos; all repository moods load without GameData; extra tabs visible');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
