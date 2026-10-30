const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
(async () => {
  const out = process.env.STUDIO_NATIVE_EXPORTS || path.resolve(__dirname, '../../artifacts/motion-path/browser');
  fs.mkdirSync(out, { recursive: true });
  const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
  try {
    const page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1500, height: 1000 } });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
    const open = async (fixture = 'ObstaclePusher4mLevel1') => {
      await page.reload({ waitUntil: 'networkidle' });
      const file = ['TM2020/', ''].map(folder => path.join(__dirname, `Fixtures/Approved/${folder}${fixture}.Item.Gbx`)).find(fs.existsSync);
      await page.getByLabel('Open item files', { exact: true }).setInputFiles(file);
      await page.getByRole('button', { name: /Export selected file/ }).waitFor().catch(async e => {
        console.error('UPLOAD', errors, await page.locator('body').innerText()); throw e;
      });
    };
    const panel = page.getByTestId('draw-motion-path');
    const coordinates = async text => {
      await page.locator('#pathCoordinates').fill(text);
      await panel.getByRole('button', { name: 'Use coordinates', exact: true }).click();
      assert.equal(await page.evaluate(() => getDrawnPathPoints().length), text.split('\n').length);
    };
    const apply = async () => {
      await panel.getByRole('button', { name: 'Apply path', exact: true }).click();
      await panel.getByText(/^Applied /).waitFor({ timeout: 10000 }).catch(async e => {
        throw new Error(`Path did not apply: ${await page.locator('.alert-danger').allTextContents()} ${e.message}`);
      });
    };
    const save = async name => {
      const gate = page.locator('#acknowledge-reencoded-export');
      if (await gate.count()) await gate.check();
      const pending = page.waitForEvent('download');
      await page.getByRole('button', { name: /Export selected file/ }).click();
      await (await pending).saveAs(path.join(out, name + '.Item.Gbx'));
    };
    const fixtures = [
      ['RootX', 'Obstacle', 'PingPong', 'Linear', '0 0 0\n16 0 0', 0],
      ['RootDiagonal', 'Obstacle', 'PingPong', 'QuadInOut', '3 0 5\n15 0 21', 500],
      ['RootVertical', 'Obstacle', 'PingPong', 'QuadIn', '0 0 0\n0 8 0', 500],
      ['RootNegative', 'Obstacle', 'Reset', 'QuadOut', '0 0 0\n-16 0 0', 500],
      ['Square', 'Scenery', 'Closed', 'Linear', '3 0 5\n19 0 5\n19 0 21\n3 0 21', 0],
      ['Triangle', 'Scenery', 'Closed', 'QuadInOut', '0 0 0\n16 0 0\n8 0 16', 500],
      ['ReturnL', 'Scenery', 'PingPong', 'Linear', '0 0 0\n16 0 0\n16 0 16', 500],
      ['Space', 'Scenery', 'Closed', 'Linear', '0 0 0\n12 8 0\n12 0 12', 0],
      ['ResetL', 'Scenery', 'Reset', 'QuadOut', '0 0 0\n16 0 0\n16 0 16', 0],
    ];
    for (const [name, use, repeat, ease, points, pause] of fixtures) {
      await open();
      const id = page.getByLabel('Ident.Id / File name', { exact: true });
      await id.fill('TMIS_Drawn_' + name); await id.press('Tab');
      await page.getByRole('button', { name: 'WaypointType, Gameplay & Kinematics', exact: true }).click();
      await panel.locator('summary').click();
      await page.locator('#pathUse').selectOption(use);
      await page.locator('#pathRepeat').selectOption(repeat);
      await page.locator('#pathEase').selectOption(ease);
      await page.locator('#pathPause').fill(String(pause)); await page.locator('#pathPause').press('Tab');
      await coordinates(points); await apply(); await save('TMIS_Drawn_' + name);
      // Reapply must replace the generated chain rather than adding another chain.
      const count = await page.evaluate(() => typedMotions.length);
      await apply(); assert.equal(await page.evaluate(() => typedMotions.length), count);
      await panel.getByRole('button', { name: 'Restore previous motion', exact: true }).click();
      assert.equal(await page.evaluate(() => typedMotions.length), 1);
      console.log('PASS path export and replacement', name);
    }
    // Real pointer drawing must honor all three selected planes and update C# coordinates.
    for (const [plane, view, axis] of [['XZ','top','y'], ['XY','front','z'], ['YZ','right','x']]) {
      await panel.getByRole('button', { name: 'Clear points', exact: true }).click();
      await page.locator('#pathPlane').selectOption(plane);
      await page.locator('#pathPlaneOffset').fill('2'); await page.locator('#pathPlaneOffset').press('Tab');
      await panel.getByRole('button', { name: 'Start drawing', exact: true }).click();
      await page.evaluate(view => { isPlaying = false; setStudioView(view); controls.update(); }, view);
      const canvas = page.locator('#viewer canvas').first();
      // Use the actual renderer element: page layout can move after panel expansion.
      const rect = await page.evaluate(() => renderer.domElement.getBoundingClientRect().toJSON());
      await page.mouse.click(rect.x + rect.width * .48, rect.y + rect.height * .48);
      await page.waitForFunction(() => getDrawnPathPoints().length === 1);
      assert.ok(Math.abs((await page.evaluate(() => getDrawnPathPoints()))[0][axis] - 2) < .001);
      await page.waitForFunction(() => document.querySelector('#pathCoordinates').value.trim().length > 0);
      await panel.getByRole('button', { name: 'Stop drawing', exact: true }).click();
      console.log('PASS pointer drawing plane', plane);
    }
    // Invalid coordinates and incompatible paths must not mutate current motion.
    const before = await page.evaluate(() => JSON.stringify(currentPayload.motions));
    await coordinates('0 0 0\n16 0 0\n16 0 16');
    await page.locator('#pathUse').selectOption('Obstacle');
    await panel.getByRole('button', { name: 'Apply path', exact: true }).click();
    await page.getByText(/Obstacle paths need exactly two points/).waitFor();
    assert.equal(await page.evaluate(() => JSON.stringify(currentPayload.motions)), before);
    await page.locator('#pathCoordinates').fill('NaN 0 0');
    await panel.getByRole('button', { name: 'Use coordinates', exact: true }).click();
    assert.equal(await page.evaluate(() => getDrawnPathPoints().length), 3);
    await panel.getByRole('button', { name: 'Undo point', exact: true }).click();
    assert.equal(await page.evaluate(() => getDrawnPathPoints().length), 2);
    await panel.getByRole('button', { name: 'Clear points', exact: true }).click();
    assert.equal(await page.evaluate(() => getDrawnPathPoints().length), 0);
    await page.screenshot({ path: path.join(out, 'controls.png'), fullPage: true });
    // A static source can be converted, exported, reopened, and restored in-session.
    await open('getting_over_it_hammer');
    await page.getByRole('button', { name: 'WaypointType, Gameplay & Kinematics', exact: true }).click();
    await panel.locator('summary').click();
    await page.locator('#pathUse').selectOption('Obstacle');
    assert.equal(await page.evaluate(() => typedMotions.length), 0);
    await coordinates('0 0 0\n16 0 0'); await apply();
    await save('TMIS_Drawn_Static');
    await panel.getByRole('button', { name: 'Restore previous motion', exact: true }).click();
    assert.equal(await page.evaluate(() => typedMotions.length), 0);
    await page.getByLabel('Open item files', { exact: true }).setInputFiles(path.join(out, 'TMIS_Drawn_Static.Item.Gbx'));
    await page.waitForFunction(() => typedMotions.length === 1);
    assert.equal(await page.evaluate(() => getDrawnPathPoints().length), 0);
    assert.equal(await page.evaluate(() => drawPathEnabled), false);
    console.log('PASS static conversion, restore, export/reopen and document reset');
    assert.deepEqual(errors, []);
    console.log('PASS invalid input isolation, undo and clear');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
