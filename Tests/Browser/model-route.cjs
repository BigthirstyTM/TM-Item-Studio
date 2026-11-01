const {chromium}=require('playwright');const assert=require('node:assert/strict');const path=require('node:path');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const page=await browser.newPage({viewport:{width:1500,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
 const source=path.resolve('artifacts/handoff-cart/fixtures/TMIS_Cart_Source.Item.Gbx');
 await page.getByLabel('Open item files',{exact:true}).setInputFiles(source);await page.waitForFunction(()=>typedMotions.length===1);
 await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();
 const editor=page.getByTestId('model-route-editor');await editor.locator(':scope > summary').click();
 await page.getByLabel('Load route reference',{exact:true}).setInputFiles(source);
 await page.getByText(/Reference loaded/).waitFor();
 assert.ok(await page.getByLabel('Reference mesh part',{exact:true}).locator('option').count()>1);
 await page.getByLabel('Draw on reference surface',{exact:true}).check();
 assert.equal(await page.evaluate(()=>routeSurfacePicking&&drawPathEnabled),true);
 // Choose the endpoints of the longest triangle edge in the first selected part.
 await page.evaluate(()=>{const part=routeReferenceParts[0];let best=[],length=0;for(let i=0;i<part.indices.length;i+=3)for(let j=0;j<3;j++){const a=new THREE.Vector3().fromArray(part.positions,3*part.indices[i+j]),b=new THREE.Vector3().fromArray(part.positions,3*part.indices[i+(j+1)%3]);if(a.distanceTo(b)>length){best=[a,b];length=a.distanceTo(b);}}setDrawnPathPoints(best.map(p=>p.add(new THREE.Vector3(0,.05,0))));});
 await page.getByRole('button',{name:'Snap current points to surface',exact:true}).click();await editor.getByText(/Snapped 2 points/).waitFor();
 await page.getByRole('button',{name:'Generate route along mesh',exact:true}).click();await editor.getByText(/Generated .*route points/).waitFor();
 await page.waitForFunction(()=>drawPathDraft?.instances?.length===1);
 assert.ok(await page.evaluate(()=>getDrawnPathPoints().length>=2));
 assert.equal(await page.locator('.studio-viewport [data-testid="path-playback"]').count(),1);
 // Reference alignment and ray-picking use the same world space.
 await page.getByLabel('Reference offset Y',{exact:true}).fill('3');await page.getByLabel('Reference offset Y',{exact:true}).press('Tab');
 assert.equal(await page.evaluate(()=>routeReference.position.y),3);
 const hit=await page.evaluate(()=>{const mesh=routeMesh(),g=mesh.geometry,a=new THREE.Vector3().fromBufferAttribute(g.attributes.position,g.index.getX(0)),b=new THREE.Vector3().fromBufferAttribute(g.attributes.position,g.index.getX(1)),c=new THREE.Vector3().fromBufferAttribute(g.attributes.position,g.index.getX(2));const tri=new THREE.Triangle(a,b,c),n=tri.getNormal(new THREE.Vector3()),p=mesh.localToWorld(tri.getMidpoint(new THREE.Vector3()));const ray=new THREE.Raycaster(p.clone().addScaledVector(n,1),n.negate());return !!pickDrawPoint(ray);});assert.ok(hit,'surface ray missed');
 await editor.screenshot({path:path.resolve('artifacts/collision-arrays/model-route-ui.png')});
 await page.getByRole('button',{name:'Remove reference',exact:true}).click();await page.waitForFunction(()=>routeReference===null&&!routeSurfacePicking).catch(async e=>{console.log(await editor.innerText());throw e;});
 const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
 await page.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
 const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();
 const download=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();await(await download).saveAs(path.resolve('artifacts/collision-arrays/Model_Route_Proof.Item.Gbx'));
 assert.deepEqual(errors,[]);console.log('PASS reference upload, part selection, surface snap, automatic route preview, offset, picking and cleanup');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
