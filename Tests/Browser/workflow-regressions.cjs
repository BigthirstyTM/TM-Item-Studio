const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

// Regressions for real item selection and keyboard focus, not just direct camera calls.
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, acceptDownloads: true });
        page.setDefaultTimeout(30000);
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        await page.getByLabel('Open item files', { exact: true }).setInputFiles([
            path.join(__dirname, 'Fixtures/Approved/TM2020/SupportConnectorX6.Item.Gbx'),
            path.join(__dirname, 'Fixtures/Approved/TM2020/getting_over_it_hammer.Item.Gbx')
        ]);
        await page.waitForFunction(() => renderer?.domElement.isConnected && staticGroup.children.length > 0);

        for (const name of ['Wire', 'Day', 'Top', 'Select getting_over_it_hammer.Item']) {
            await page.getByRole('button', { name, exact: true }).click();
            await page.keyboard.press('Numpad7');
            await page.waitForFunction(() => activeStudioView === 'top');
            await page.keyboard.press('Numpad1');
            await page.waitForFunction(() => activeStudioView === 'front');
            await page.keyboard.press('Numpad3');
            await page.waitForFunction(() => activeStudioView === 'right');
            await page.keyboard.press('Numpad5');
            await page.waitForFunction(() => activeStudioView === 'perspective');
        }
        const id = page.getByLabel('Ident.Id / File name', { exact: true });
        if (!await id.isVisible()) await page.getByRole('button', { name: 'Item Identification & Collector Metadata', exact: true }).click();
        await id.focus();
        await page.keyboard.press('Numpad7');
        assert.equal(await page.evaluate(() => activeStudioView), 'perspective', 'Editing a field must not move the camera');
        await page.getByRole('button', { name: 'Wire', exact: true }).focus();
        await page.keyboard.press('Home');
        assert.equal(await page.evaluate(() => activeStudioView), 'perspective', 'Ordinary Home is not a numpad shortcut');

        // Find a real screen-space hit and a real empty point, avoiding reliance on a guessed pixel.
        const points = await page.evaluate(() => {
            isPlaying = false;
            const rect = renderer.domElement.getBoundingClientRect();
            const ray = new THREE.Raycaster();
            scene.updateMatrixWorld(true); camera.updateMatrixWorld(true);
            const groups = [staticGroup, movingGroup].filter(Boolean);
            let item, empty;
            for (let y = .1; y < .95; y += .035) for (let x = .1; x < .95; x += .035) {
                ray.setFromCamera(new THREE.Vector2(x * 2 - 1, 1 - y * 2), camera);
                const hits = ray.intersectObjects(groups.flatMap(g => g.children), true).filter(h => isSelectableMesh(h.object));
                const gizmos = ray.intersectObjects([pivotsGroup, lightsGroup, waypointsGroup].filter(g => g?.visible), true).filter(h => isVisibleInScene(h.object));
                const point = { x: rect.left + x * rect.width, y: rect.top + y * rect.height };
                if (document.elementFromPoint(point.x, point.y) !== renderer.domElement) continue;
                if (hits.length && !gizmos.length) item ??= point;
                if (!hits.length && !gizmos.length && y > .55) empty ??= point;
            }
            return { item, empty };
        });
        assert.ok(points.item && points.empty, 'Real item and empty space must both be testable');
        await page.mouse.click(points.item.x, points.item.y);
        await page.waitForFunction(() => selectedMeshPath !== null, null, { timeout: 5000 }).catch(async error => {
            console.error('Selection diagnostics', { points, errors, state: await page.evaluate(() => ({ path: selectedMeshPath, selected: [...selectedCompositeSources], canvas: renderer.domElement.getBoundingClientRect().toJSON(), tab: document.activeElement?.tagName, overlay: document.elementFromPoint(...[window.innerWidth / 4, window.innerHeight / 2])?.outerHTML.slice(0, 200) })) });
            throw error;
        });
        await page.mouse.click(points.empty.x, points.empty.y);
        await page.waitForFunction(() => selectedMeshPath === null && selectedCompositeSources.size === 0 && !transformControls.object);
        // Force a normal payload refresh: the C# composite selection must also have cleared.
        await page.evaluate(() => dotNetHelper.invokeMethodAsync('OnGizmoDragEnded', 'composite-offset', 0));
        const cleared = await page.evaluate(() => {
            const pink = [];
            for (const group of [staticGroup, movingGroup]) group.traverse(mesh => {
                if (mesh.isMesh && mesh.material?.emissive?.getHex() === 0xff2ea6) pink.push(mesh.userData.path);
            });
            return { selected: [...selectedCompositeSources], path: selectedMeshPath, pink };
        });
        assert.deepEqual(cleared, { selected: [], path: null, pink: [] });
        await page.getByLabel('Open item files', { exact: true }).setInputFiles(path.join(__dirname, 'Fixtures/animation-static-first.Item.Gbx'));
        await page.getByRole('button', { name: 'WaypointType, Gameplay & Kinematics', exact: true }).click();
        const counts = page.getByLabel('Translation segment count', { exact: true });
        await counts.waitFor();
        assert.deepEqual(await counts.locator('option:not([disabled])').evaluateAll(options => options.map(o => o.value)), ['1', '2', '3', '4']);
        await page.getByRole('button', { name: 'Flag item conversion', exact: true }).click();
        const downloading = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Convert to flag item (.Item.Gbx)', exact: true }).click();
        const exported = await downloading;
        if (process.env.STUDIO_FLAG_EXPORT) await exported.saveAs(process.env.STUDIO_FLAG_EXPORT);
        assert.ok(exported.suggestedFilename().endsWith('_Flag.Item.Gbx'));
        await page.getByLabel('Open item files', { exact: true }).setInputFiles({
            name: exported.suggestedFilename(), mimeType: 'application/octet-stream', buffer: fs.readFileSync(await exported.path())
        });
        await page.locator('.studio-documents').getByText(exported.suggestedFilename().replace(/\.Gbx$/i, ''), { exact: true }).waitFor();
        const signature = page.getByText('This item carries the measured flag signature.', { exact: false });
        if (!await signature.isVisible()) await page.getByRole('button', { name: 'Flag item conversion', exact: true }).click();
        await signature.waitFor();
        assert.deepEqual(errors, []);
        console.log('PASS: numpad focus/input guards; pointer deselection survives refresh; supported segment choices; absent-placement flag export/reopen.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
