const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');

// The embedded Sparkler fixture is supplied at runtime because it lives in the
// Trackmania user-data tree, not in the repository.  The dependency directory
// is uploaded first so the Studio can resolve its local texture wrappers.
const item = process.argv[2];
const dependencies = process.argv[3];
assert.ok(item && dependencies, 'Usage: fx-visibility.cjs <embedded Item.Gbx> <embedded dependency folder>');

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        await page.locator('#vanilla-folder-upload').setInputFiles(dependencies);
        await page.waitForFunction(() => document.body.innerText.includes('Textures and moods indexed'));
        await page.getByLabel('Open item files', { exact: true }).setInputFiles(item);
        await page.waitForFunction(() => document.querySelector('.studio-variant-filename')?.textContent?.includes('TestItem_Sparkler'));
        const effects = page.getByRole('button', { name: 'Audio, CPlugFx & MediaTracker Triggers', exact: true });
        await effects.click();
        await assertText(page, 'Particle system / CPlugFx', 'Present');
        await assertText(page, 'Effect carrier references');
        await assertText(page, 'CPlugFxSystem');
        await assertText(page, 'ParticleTexture');
        await assertText(page, 'native-field-ready');
        await page.getByText('FX emission point', { exact: true }).waitFor();
        await page.getByLabel('FX emission point X', { exact: true }).waitFor();
        await page.getByLabel('FX emission point Y', { exact: true }).waitFor();
        await page.getByLabel('FX emission point Z', { exact: true }).waitFor();
        await page.getByText('Native status: validated LocalOffsetExpr write', { exact: false }).waitFor();
        await page.getByLabel('Hidden FX catalog routes', { exact: true }).waitFor();
        const carrierPreset = page.getByLabel('FX carrier preset', { exact: true });
        await carrierPreset.waitFor();
        assert.deepEqual(await carrierPreset.locator('option').allTextContents(), [
            'Embedded Sparkler (8 m / 16 m)',
            'Embedded Fogger (8 m / 16 m)',
            'Fogger with light (8 m / 16 m)',
            'Torch / small Torch'
        ]);
        await carrierPreset.selectOption({ label: 'Embedded Fogger (8 m / 16 m)' });
        await page.getByRole('status').filter({ hasText: 'Embedded Fogger (8 m / 16 m)' }).waitFor();
        const particleCandidate = page.getByLabel('Particle model to emit', { exact: true });
        await particleCandidate.waitFor();
        assert.deepEqual(await particleCandidate.locator('option').allTextContents(), [
            'PodiumSparkler / PodiumSparklerEnd',
            'WaterSpray / Thin / AndSteam',
            'WaterSplash / bubbles / wakes',
            'Leaves, fog, smoke, fire, boost, sparks and light trails'
        ]);
        await particleCandidate.selectOption({ label: 'WaterSpray / Thin / AndSteam' });
        await page.getByRole('status').filter({ hasText: 'WaterSpray / Thin / AndSteam' }).waitFor();
        const exactParticle = page.getByLabel('Exact particle model (83-row native audit)', { exact: true });
        await exactParticle.waitFor();
        assert.equal(await exactParticle.locator('option').count(), 83);
        await exactParticle.selectOption('WaterSprayThin');
        await page.getByRole('status').filter({ hasText: 'WaterSprayThin' }).first().waitFor();
        await page.getByLabel('Complete FX particle inventory', { exact: true }).screenshot({
            path: process.env.STUDIO_FX_CATALOG_SCREENSHOT || path.resolve(__dirname, '../../artifacts/fx-clones/fx-catalog-selector.png')
        });
        await page.getByText('Add FX / mixed graph', { exact: false }).waitFor();
        const x = page.getByLabel('FX emission point X', { exact: true });
        const y = page.getByLabel('FX emission point Y', { exact: true });
        const z = page.getByLabel('FX emission point Z', { exact: true });
        await x.fill('1.25'); await y.fill('2.5'); await z.fill('-0.75');
        await page.waitForFunction(() => {
            const value = label => document.querySelector(`input[aria-label="${label}"]`)?.value;
            return value('FX emission point X') === '1.25'
                && value('FX emission point Y') === '2.5'
                && value('FX emission point Z') === '-0.75';
        });
        await page.getByText('Native status:', { exact: false }).first().waitFor();
        const exportButton = page.getByRole('button', { name: 'Export FX item (.Item.Gbx)', exact: true });
        await exportButton.waitFor();
        const diagnostics = page.getByLabel('Include FX diagnostics', { exact: true });
        assert.equal(await diagnostics.isChecked(), false);
        const defaultDownloads = [];
        const recordDefaultDownload = download => defaultDownloads.push(download);
        page.on('download', recordDefaultDownload);
        const firstDownloadPromise = page.waitForEvent('download');
        await exportButton.click();
        const itemDownload = await firstDownloadPromise;
        await page.waitForFunction(() => document.body.innerText.includes('validated native FX fields written'));
        await page.waitForTimeout(150);
        page.off('download', recordDefaultDownload);
        assert.equal(defaultDownloads.length, 1, 'default export must download one Item.Gbx without a required JSON sidecar');
        assert.ok(itemDownload.suggestedFilename().endsWith('.Item.Gbx'));
        const itemBytes = await (await itemDownload.createReadStream()).toArray().then(chunks => Buffer.concat(chunks));
        // Reopen only the downloaded Item: its coordinates must come from its
        // native fields rather than from a JSON sidecar or retained form state.
        await page.getByLabel('Open item files', { exact: true }).setInputFiles({
            name: itemDownload.suggestedFilename(), mimeType: 'application/octet-stream', buffer: itemBytes
        });
        await page.waitForFunction(() => document.querySelector('.studio-variant-filename')?.textContent?.includes('TestItem_Sparkler'));
        if (await page.getByLabel('FX emission point X', { exact: true }).count() === 0) await effects.click();
        await page.waitForFunction(() => {
            const value = label => document.querySelector(`input[aria-label="${label}"]`)?.value;
            return value('FX emission point X') === '1.25'
                && value('FX emission point Y') === '2.5'
                && value('FX emission point Z') === '-0.75';
        });
        await diagnostics.check();
        const downloads = [];
        const downloadHandler = download => downloads.push(download);
        page.on('download', downloadHandler);
        await exportButton.click();
        await page.waitForFunction(() => document.body.innerText.includes('validated native FX fields'));
        await page.waitForTimeout(250);
        page.off('download', downloadHandler);
        assert.equal(downloads.length, 2, `expected item and settings downloads, got ${downloads.length}`);
        assert.ok(downloads.some(download => download.suggestedFilename().endsWith('.Item.Gbx')));
        assert.ok(downloads.some(download => download.suggestedFilename().endsWith('.fxsettings.json')));
        const settingsDownload = downloads.find(download => download.suggestedFilename().endsWith('.fxsettings.json'));
        const settingsText = await (await settingsDownload.createReadStream()).toArray().then(chunks => Buffer.concat(chunks).toString('utf8'));
        const settings = JSON.parse(settingsText);
        assert.deepEqual(settings.emissionPoint, { x: 1.25, y: 2.5, z: -0.75 });
        assert.equal(settings.nativeOffsetWritten, true);
        assert.equal(settings.diagnosticOnly, true);
        assert.equal(settings.requiredForGame, false);
        assert.equal(settings.metadataSaveBodyByteExact, true);
        assert.equal(settings.standaloneRuntimeVerified, false);
        assert.equal(settings.nativeEditProof.originalBodyRestorableExactly, true);
        await page.screenshot({
            path: process.env.STUDIO_FX_SCREENSHOT || path.resolve(__dirname, '../../artifacts/fx-clones/fx-visibility.png'),
            fullPage: true
        });
        assert.deepEqual(errors, []);
        console.log('PASS: FX inventory, native XYZ export, Item-only reopen, optional diagnostics and truthful proof status.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });

async function assertText(page, text, expected) {
    const locator = page.getByText(text, { exact: true });
    await locator.first().waitFor();
    if (expected !== undefined)
        assert.equal((await locator.first().locator('..').innerText()).includes(expected), true, `${text} status`);
}
