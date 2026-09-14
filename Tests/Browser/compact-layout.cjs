const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1895, height: 1000 }, colorScheme: 'dark' });
        page.setDefaultTimeout(60000);
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5184/', { waitUntil: 'networkidle' });
        await page.getByLabel('Open item files').setInputFiles(path.join(__dirname, 'Fixtures/Approved/TM2020/SnowCarTraffic_60kph.Item.gbx'));
        await page.getByRole('button', { name: /^2\. SnowCarTraffic/ }).click();
        await page.getByRole('button', { name: /WaypointType, Gameplay & Kinematics/ }).click();
        await page.waitForFunction(() => document.querySelector('[aria-label="Item variants"]').getAttribute('aria-busy') === 'false');
        await page.evaluate(() => setAnimationPlaying(false));
        async function measure() {
            return page.evaluate(() => {
                const rect = selector => { const r = document.querySelector(selector).getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height, bottom: r.bottom }; };
                return { canvas: rect('#threeContainer'), inspector: rect('.studio-inspector'),
                    viewport: { width: innerWidth, height: innerHeight }, overflow: document.documentElement.scrollWidth > innerWidth,
                    documentBottom: document.documentElement.getBoundingClientRect().bottom };
            });
        }
        const initial = await measure();
        console.log(JSON.stringify(initial));
        async function capture(name) {
            if (!process.env.STUDIO_LAYOUT_EVIDENCE) return;
            fs.mkdirSync(process.env.STUDIO_LAYOUT_EVIDENCE, { recursive: true });
            await page.evaluate(() => document.fonts.ready);
            await page.screenshot({ path: path.join(process.env.STUDIO_LAYOUT_EVIDENCE, `${name}.png`), fullPage: true });
        }
        await capture('desktop-dark');
        if (process.env.STUDIO_LAYOUT_EVIDENCE) {
            await page.locator('.studio-header').screenshot({ path: path.join(process.env.STUDIO_LAYOUT_EVIDENCE, 'header-detail.png') });
            await page.locator('.motion-constraint').first().screenshot({ path: path.join(process.env.STUDIO_LAYOUT_EVIDENCE, 'kinematics-detail.png') });
        }
        assert.ok(initial.canvas.y <= 120, `Viewport starts too low: ${initial.canvas.y}`);
        assert.ok(initial.canvas.height >= 780, `Viewport too short: ${initial.canvas.height}`);
        assert.ok(initial.inspector.height >= 850, `Inspector too short: ${initial.inspector.height}`);
        assert.ok(initial.inspector.bottom <= 1000, 'Inspector/export must fit the desktop height');
        assert.equal(await page.locator('.studio-viewport-toolbar [aria-label="Item variants"]').count(), 1);
        assert.equal(await page.locator('.studio-header .studio-variant-filename').count(), 1);
        const containerSize = await page.locator('#threeContainer').boundingBox();
        await page.getByRole('button', { name: 'Switch light or dark theme' }).click();
        await capture('desktop-light');
        await page.setViewportSize({ width: 1280, height: 720 });
        await page.waitForFunction(() => renderer.domElement.clientWidth === document.querySelector('#threeContainer').clientWidth);
        const laptop = await measure();
        assert.ok(laptop.canvas.height >= 500 && laptop.inspector.bottom <= 720, JSON.stringify(laptop));
        assert.equal(laptop.overflow, false);
        assert.ok(laptop.canvas.width !== containerSize.width, 'Viewport did not adapt');
        await capture('laptop');
        await page.setViewportSize({ width: 390, height: 844 });
        const mobile = await measure();
        assert.equal(mobile.overflow, false, 'Narrow layout must not overflow horizontally');
        for (const button of await page.getByRole('button', { name: /^\d+\. SnowCarTraffic/ }).all()) assert.ok(await button.isVisible());
        await capture('narrow');
        // Long names and many files must not push the canvas/export off-screen.
        await page.setViewportSize({ width: 1280, height: 720 });
        const cube = fs.readFileSync(path.join(__dirname, 'Fixtures/Approved/TM2020/Item.Item.Gbx'));
        await page.getByLabel('Open item files').setInputFiles(Array.from({ length: 16 }, (_, i) => ({
            name: `File${i}-${'long-name-'.repeat(20)}.Item.Gbx`, mimeType: 'application/octet-stream', buffer: cube
        })));
        await page.getByRole('button', { name: /^16\. File15/ }).click();
        const multiple = await measure();
        assert.equal(multiple.overflow, false, 'Long filenames must not overflow the page');
        assert.ok(multiple.canvas.height >= 400 && multiple.inspector.bottom <= 720, JSON.stringify(multiple));
        assert.ok(await page.getByRole('button', { name: 'Export selected file' }).isVisible());
        await capture('multiple-files');
        assert.deepEqual(errors, []);
        console.log('PASS: compact desktop header, toolbar variants, growing viewport/inspector, responsive canvas and narrow layout.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
