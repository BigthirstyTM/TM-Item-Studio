const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, acceptDownloads: true });
        page.setDefaultTimeout(60000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'error') console.error(message.text()); });
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5184/', { waitUntil: 'networkidle' });
        // Observe the real viewer boundary; do not substitute geometry or rendering.
        await page.evaluate(() => {
            const render = window.renderStudioScene;
            window.variantRenders = [];
            window.geometryTransfers = [];
            window.renderStudioScene = payload => {
                const result = render(payload);
                variantRenders.push(performance.now());
                geometryTransfers.push({ definitions: payload.geometryDefinitions?.length ?? 0,
                    definitionIds: (payload.geometryDefinitions ?? []).map(definition => definition.geometryId),
                    shared: payload.parts.filter(p => p.geometryId != null).length,
                    parts: payload.parts.length });
                return result;
            };
        });
        const fixture = path.join(__dirname, 'Fixtures/Approved/TM2020/SnowCarTraffic_60kph.Item.gbx');
        await page.getByLabel('Open item files').setInputFiles(fixture);
        await page.waitForFunction(() => variantRenders.length > 0 || document.querySelector('.alert-danger') || document.querySelector('#blazor-error-ui')?.style.display === 'block');
        assert.equal(await page.locator('.alert-danger').count(), 0, await page.locator('body').innerText());
        assert.ok(await page.evaluate(() => variantRenders.length > 0), await page.locator('body').innerText());
        const variants = page.getByRole('button', { name: /^\d+\. SnowCarTraffic/ });
        assert.equal(await variants.count(), 9);
        const layout = await variants.evaluateAll(buttons => ({
            labels: buttons.map(b => b.innerText.trim()),
            rows: new Set(buttons.map(b => Math.round(b.getBoundingClientRect().top))).size
        }));
        const durations = [];
        for (const n of [2, 3, 2, 1, 3]) {
            const before = await page.evaluate(() => ({ count: variantRenders.length, time: performance.now() }));
            await page.getByRole('button', { name: new RegExp(`^${n}\\. SnowCarTraffic`) }).click();
            await page.waitForFunction(count => variantRenders.length > count, before.count);
            await page.waitForFunction(() => document.querySelector('[aria-label="Item variants"]').getAttribute('aria-busy') === 'false');
            durations.push(await page.evaluate(start => performance.now() - start, before.time));
        }
        console.log(JSON.stringify({ layout, switchMilliseconds: durations }));
        const transfers = await page.evaluate(() => geometryTransfers.filter(t => t.shared > 0));
        console.log(JSON.stringify({ geometryTransfers: transfers }));
        // Native SnowCar structure (verified against the pinned parser): variant 1 is the
        // static reference variant — a single CPlugStaticObjectModel with seven authored
        // visuals and no dyna graph. Variants 2-9 are CPlugDynaObjectModel entities with a
        // kinematic constraint; their dynaShape collision surface is one additional shared
        // geometry, the same authored CPlugSurface node in every variant this navigation
        // visits. (The file also authors a second car from distinct nodes — first
        // referenced by variant 6 — so extending navigation there legitimately transfers
        // again.) So the first switch to a dyna variant transfers exactly that one new
        // definition, and every later switch in this navigation must transfer nothing.
        assert.ok(transfers[0].shared > 0, 'Real upload must use GBX reference identities');
        assert.equal(transfers[0].definitions, 7, 'The static reference variant authors seven shared visuals');
        assert.equal(transfers[1].definitions, 1, 'The first dyna variant introduces its dynaShape collision geometry once');
        assert.equal(transfers[2].definitions, 0, 'Further SnowCar variants must reuse their shared GBX geometry');
        assert.equal(transfers[3].definitions, 0, 'Revisiting a variant must not upload geometry again');
        assert.equal(transfers[4].definitions, 0, 'Visiting the static variant must retain the shared pool');
        assert.equal(transfers.at(-1).definitions, 0, 'Returning to a dyna variant must still reuse the shared pool');
        const sentIds = transfers.flatMap(transfer => transfer.definitionIds);
        assert.equal(new Set(sentIds).size, sentIds.length, 'No shared geometry definition may be uploaded twice');
        assert.equal(layout.rows, 1, 'Desktop variant buttons must fit on one row');
        assert.deepEqual(layout.labels, ['1', '2', '3', '4', '5', '6', '7', '8', '9'], 'Filename/ordinal repeated in each visible button');
        // A generous ceiling for loaded CI machines; the previous dev build takes ~15s.
        const budget = Number(process.env.STUDIO_VARIANT_BUDGET_MS || 5000);
        assert.ok(durations.every(ms => ms < budget), `Variant switch exceeds ${budget}ms: ${durations}`);
        await page.waitForFunction(() => document.querySelector('[aria-label="Item variants"]').getAttribute('aria-busy') === 'false');
        const focusRetained = await page.getByRole('button', { name: /^3\. SnowCarTraffic/ }).evaluate(button => button === document.activeElement);
        async function screenshot(name) {
            if (!process.env.STUDIO_VARIANT_EVIDENCE) return;
            fs.mkdirSync(process.env.STUDIO_VARIANT_EVIDENCE, { recursive: true });
            await page.evaluate(() => document.fonts.ready);
            await page.screenshot({ path: path.join(process.env.STUDIO_VARIANT_EVIDENCE, `${name}.png`) });
            await page.locator('.studio-variants').screenshot({ path: path.join(process.env.STUDIO_VARIANT_EVIDENCE, `${name}-controls.png`) });
        }
        await page.evaluate(() => setAnimationPlaying(false));
        await screenshot('desktop');
        await page.setViewportSize({ width: 390, height: 844 });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Variant navigation causes horizontal page overflow');
        for (const button of await variants.all()) assert.ok(await button.isVisible(), 'A variant is inaccessible at narrow width');
        await screenshot('narrow');
        await page.setViewportSize({ width: 1440, height: 1000 });
        // OBJ remains available on demand after switching. Inspect the actual download.
        const event = page.waitForEvent('download');
        await page.getByRole('button', { name: '.OBJ', exact: true }).click();
        const download = await event;
        const stream = await download.createReadStream();
        const chunks = [];
        for await (const chunk of stream) chunks.push(chunk);
        const obj = Buffer.concat(chunks).toString('utf8');
        assert.match(obj, /^# Trackmania Item Studio OBJ Export/);
        assert.ok(obj.split('\n').filter(l => l.startsWith('v ')).length > 1000, 'OBJ lost selected geometry');
        assert.ok(obj.split('\n').some(l => /^f \d+ \d+ \d+$/.test(l)), 'OBJ lost triangle indices');
        // Multiple documents retain separate identities while their visible choices stay short.
        await page.getByLabel('Open item files').setInputFiles([fixture, path.join(__dirname, 'Fixtures/Approved/TM2020/Item.Item.Gbx')]);
        const cube = page.getByRole('button', { name: /^10\. Item/ });
        await cube.click();
        assert.equal(await cube.getAttribute('aria-pressed'), 'true');
        assert.equal(await cube.innerText(), '1');
        assert.equal(await page.locator('.studio-variant-file').count(), 2);
        assert.equal(await page.getByRole('button', { name: /^1\. SnowCarTraffic/ }).innerText(), '1');
        await page.getByLabel('Open item files').setInputFiles([
            { name: 'same.Item.Gbx', mimeType: 'application/octet-stream', buffer: fs.readFileSync(fixture) },
            { name: 'same.Item.Gbx', mimeType: 'application/octet-stream', buffer: fs.readFileSync(path.join(__dirname, 'Fixtures/Approved/TM2020/Item.Item.Gbx')) }
        ]);
        const otherDocument = page.getByRole('button', { name: /^10\. same/ });
        await otherDocument.click();
        const headings = await page.locator('.studio-variant-filename').allTextContents();
        console.log(JSON.stringify({ focusRetained, duplicateFileHeadings: headings }));
        assert.ok(focusRetained, 'Variant selection must retain keyboard focus on its initiating button');
        assert.equal(new Set(headings).size, 2, 'Distinct documents with equal filenames need distinct visible headings');
        assert.deepEqual(errors, []);
        console.log('PASS: compact responsive variants, bounded switching, and on-demand OBJ export.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
