const { chromium } = require('playwright');
const assert = require('node:assert/strict');

// Empty CGameItemModel constructed and saved with the bundled GBX.NET + MiniLZO.
// Distinct names ensure InputFile dispatches a second change event.
const syntheticItem = Buffer.from('R0JYBgBCVUNSACAALgAAAAABAAAAAAAAAAQAAAAIAAAAFQHeyvoRAAA=', 'base64');

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => {
            window.testResizeHandlers = new Set();
            window.testCancelledFrames = new Set();
            const add = window.addEventListener.bind(window);
            const remove = window.removeEventListener.bind(window);
            const cancel = window.cancelAnimationFrame.bind(window);
            window.addEventListener = (name, handler, options) => {
                if (name === 'resize') testResizeHandlers.add(handler);
                return add(name, handler, options);
            };
            window.removeEventListener = (name, handler, options) => {
                if (name === 'resize') testResizeHandlers.delete(handler);
                return remove(name, handler, options);
            };
            window.cancelAnimationFrame = id => { testCancelledFrames.add(id); return cancel(id); };
        });
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        const input = page.locator('input[type=file]').first();
        await input.waitFor();
        const baselineHandlers = await page.evaluate(() => testResizeHandlers.size);
        await input.setInputFiles({ name: 'first.Item.Gbx', mimeType: 'application/octet-stream', buffer: syntheticItem });
        await page.waitForFunction(() => typeof renderer !== 'undefined' && renderer?.domElement.isConnected);
        assert.equal(await page.locator('#threeContainer canvas').count(), 1);

        // Re-rendering a scene must preserve the UI playback state.
        const playbackState = await page.evaluate(() => {
            renderStudioScene({ playing: false, parts: [], pivots: [], lights: [], sockets: [] });
            const paused = isPlaying;
            renderStudioScene({ playing: true, parts: [], pivots: [], lights: [], sockets: [] });
            return { paused, resumed: isPlaying };
        });
        assert.deepEqual(playbackState, { paused: false, resumed: true });

        const resources = await page.evaluate(() => {
            renderStudioScene({ parts: [], pivots: [{ x: 0, y: 0, z: 0 }], lights: [{ x: 0, y: 1, z: 0 }], sockets: [{ x: 0, y: 0, z: 0 }] });
            let expected = 0, disposed = 0;
            for (const group of [pivotsGroup, lightsGroup, socketsGroup]) {
                group.traverse(child => {
                    if (child.geometry) { expected++; child.geometry.addEventListener('dispose', () => disposed++); }
                });
            }
            renderStudioScene({ parts: [], pivots: [], lights: [], sockets: [] });
            return { expected, disposed };
        });
        assert.ok(resources.expected > 0);
        assert.equal(resources.disposed, resources.expected, 'Rebuild must dispose nested gizmo geometry');

        await page.evaluate(() => { window.firstCanvasForTest = renderer.domElement; toggleLayer('pivots'); });
        await input.setInputFiles({ name: 'second.Item.Gbx', mimeType: 'application/octet-stream', buffer: syntheticItem });
        await page.waitForFunction(() => renderer?.domElement.isConnected && renderer.domElement !== firstCanvasForTest);
        assert.equal(await page.locator('#threeContainer canvas').count(), 1);
        assert.equal(await page.evaluate(() => firstCanvasForTest.isConnected), false);
        assert.equal(await page.evaluate(() => testResizeHandlers.size), baselineHandlers + 1, 'Reload must replace its resize listener');
        assert.equal(await page.evaluate(() => pivotsGroup.visible), false, 'Reinitialization must retain layer visibility');

        // A failed parse must retire the old viewer, and a later good file must recover.
        await input.setInputFiles({ name: 'bad.Gbx', mimeType: 'application/octet-stream', buffer: Buffer.from('not a GBX') });
        await page.getByText('Error while processing the file:', { exact: false }).waitFor();
        assert.equal(await page.evaluate(() => renderer), null);
        assert.equal(await page.evaluate(() => testResizeHandlers.size), baselineHandlers);
        await input.setInputFiles({ name: 'third.Item.Gbx', mimeType: 'application/octet-stream', buffer: syntheticItem });
        await page.waitForFunction(() => renderer?.domElement.isConnected);

        // Route disposal tests the component's IAsyncDisposable hookup as well as JS cleanup.
        const cancellationsBeforeRoute = await page.evaluate(() => {
            window.routeCanvasForTest = renderer.domElement;
            const count = testCancelledFrames.size;
            Blazor.navigateTo('/missing-test-route');
            return count;
        });
        await page.waitForFunction(() => renderer === null);
        assert.equal(await page.evaluate(() => routeCanvasForTest.isConnected), false);
        assert.equal(await page.evaluate(() => testCancelledFrames.size), cancellationsBeforeRoute + 1, 'Route disposal must cancel its pending frame');
        assert.equal(await page.evaluate(() => testResizeHandlers.size), baselineHandlers);
        await page.evaluate(() => { dispose3DViewer(); dispose3DViewer(); });
        assert.deepEqual(errors, []);
        console.log('PASS: distinct-file reload, failed-load recovery, nested resources, resize listeners, layer state and route disposal.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
