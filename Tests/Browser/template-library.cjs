const {chromium}=require('playwright');const assert=require('node:assert/strict');const fs=require('node:fs');const path=require('node:path');
(async()=>{const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});try{
 const out=path.resolve('artifacts/template-gallery');const gallery=JSON.parse(fs.readFileSync(path.join(out,'gallery.json')));
 const context=await browser.newContext({viewport:{width:1500,height:1000},acceptDownloads:true});const errors=[];
 for(let i=0;i<gallery.length;i++){
  const g=gallery[i],page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));await page.goto(process.env.STUDIO_BASE_URL,{waitUntil:'networkidle'});
  await page.getByLabel('Open item files',{exact:true}).setInputFiles(path.join(out,'Sources',`TMIS_Demo_${g.label}_Source.Item.Gbx`));await page.waitForFunction(()=>typedMotions.length===1);
  const id=page.getByLabel('Ident.Id / File name',{exact:true});await id.fill(`TMIS_Demo_${g.label}`);await id.press('Tab');
  await page.getByRole('button',{name:'WaypointType, Gameplay & Kinematics',exact:true}).click();const panel=page.getByTestId('draw-motion-path');await panel.locator(':scope > summary').click();
  await page.getByLabel('Load route draft',{exact:false}).setInputFiles(path.join(out,'Templates',g.label+'.motion-path.json'));await page.waitForFunction(()=>!!drawPathDraft);
  if(i<13){const editor=page.getByTestId('motion-behavior-editor');await editor.locator(':scope > summary').click();assert.equal(await editor.getByLabel('Movement template',{exact:true}).locator('option').count(),14);await editor.getByLabel('Movement template',{exact:true}).selectOption({index:i+1});await editor.getByRole('button',{name:'Apply movement template',exact:true}).click();assert.equal(await editor.locator('[role=alert]').count(),0);}
  const library=page.getByTestId('custom-template-library');await library.locator(':scope > summary').click();
  await library.getByLabel('Custom template name',{exact:true}).fill('Demo '+g.label);await library.getByRole('button',{name:'Save as new template',exact:true}).click();await library.getByText('Template saved in this browser.',{exact:true}).waitFor();
  const before=await page.evaluate(()=>drawPathDraft.plan.durationMs);await library.getByRole('button',{name:'Apply saved template',exact:true}).click();await library.getByText('Template applied to draft. Review the preview before export.',{exact:true}).waitFor();assert.ok(Math.abs((await page.evaluate(()=>drawPathDraft.plan.durationMs))-before)<100);
  if(i===0){const download=page.waitForEvent('download');await library.getByRole('button',{name:'Export template JSON',exact:true}).click();const file=path.join(out,'Templates','Browser-custom.motion-template.json');await(await download).saveAs(file);await library.getByRole('button',{name:'Delete selected template',exact:true}).click();await library.getByLabel('Import custom template',{exact:true}).setInputFiles(file);await library.getByText('Template saved in this browser.',{exact:true}).waitFor();await library.screenshot({path:path.join(out,'custom-library.png')});}
  // Test actual moving preview and compile each profile through the primary Apply path button.
  const initial=await page.evaluate(()=>{applyDrawPathPreview(drawPathDraft.started);return drawPathDraft.ghost.matrix.toArray();});const later=await page.evaluate(()=>{applyDrawPathPreview(drawPathDraft.started+drawPathDraft.plan.durationMs*.37);return drawPathDraft.ghost.matrix.toArray();});assert.notDeepEqual(initial,later,g.label+' frozen');
  await page.getByRole('button',{name:'Apply path',exact:true}).click();await panel.getByText(/^Applied /).waitFor();
  const gate=page.locator('#acknowledge-reencoded-export');if(await gate.count())await gate.check();const exported=page.waitForEvent('download');await page.getByRole('button',{name:/Export selected file/}).click();await(await exported).saveAs(path.join(out,g.item));
  console.log('PASS '+g.label+' template, custom save/apply, moving preview, browser export');await page.close();
 }
 const saved=await context.newPage();await saved.goto(process.env.STUDIO_BASE_URL);assert.equal(await saved.evaluate(()=>JSON.parse(localStorage.getItem('tmis.motionTemplates.v1')).length),14);assert.deepEqual(errors,[]);
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
