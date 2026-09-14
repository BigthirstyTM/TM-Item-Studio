const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

module.exports = async (url, work) => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } });
        const errors = [], warnings = [];
        page.on('pageerror', e => errors.push(e.message));
        page.on('console', m => { if (m.type() === 'warning') warnings.push(m.text()); });
        await page.goto(url, { waitUntil: 'networkidle' });
        await page.getByRole('button', { name: 'Download debug report', exact: true }).waitFor();
        const report = () => page.evaluate(() => window.studioDebug.getReport());
        async function exported(name) {
            const event = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Export selected file' }).click();
            const file = path.join(work, name);
            await (await event).saveAs(file);
            return fs.readFileSync(file);
        }
        assert.equal((await report()).status, 'empty');
        await page.getByLabel('Open item files').setInputFiles(path.join(__dirname, 'Fixtures/animation-static-first.Item.Gbx'));
        await page.getByRole('button', { name: 'Export selected file' }).waitFor();
        await page.locator('#threeContainer canvas').waitFor();
        const loaded = await report();
        assert.equal(loaded.schemaVersion, 1);
        assert.match(loaded.parser.moduleVersionId, /^[0-9a-f-]{36}$/);
        assert.equal(loaded.status, 'loaded');
        assert.ok(loaded.viewer.meshParts > 0);
        assert.equal(loaded.viewer.materialMode, 'neutral-untextured');
        assert.equal(loaded.viewer.runtime.initialized, true);
        assert.ok(loaded.viewer.runtime.meshes > 0);
        assert.deepEqual(loaded.viewer.runtime.materialColors, ['b8bdc6']);
        assert.ok(loaded.diagnostics.some(d => d.code === 'textures-not-loaded'));
        await page.getByText('Neutral preview — textures are not loaded.', { exact: true }).waitFor();
        assert.ok(warnings.some(w => w.includes('[TMIS] textures-not-loaded')));
        await page.getByLabel('Ident.Author', { exact: true }).fill('DebugReportEditedAuthor');
        await page.getByLabel('Ident.Author', { exact: true }).press('Tab');
        const beforeDebug = await exported('before-debug.Item.Gbx');
        assert.equal((await report()).identity.author, 'DebugReportEditedAuthor', 'Report must describe current edits');
        const event = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Download debug report', exact: true }).click();
        const download = await event;
        const dest = path.join(work, 'report.json');
        await download.saveAs(dest);
        const downloaded = JSON.parse(fs.readFileSync(dest, 'utf8'));
        assert.equal(downloaded.identity.author, 'DebugReportEditedAuthor');
        assert.ok(!JSON.stringify(downloaded).includes('localPositions'), 'No vertex buffers in diagnostic export');
        assert.deepEqual(await exported('after-debug.Item.Gbx'), beforeDebug, 'Diagnostics must not mutate the item archive');
        if (process.env.STUDIO_DEBUG_EVIDENCE) {
            fs.mkdirSync(process.env.STUDIO_DEBUG_EVIDENCE, { recursive: true });
            await page.evaluate(() => document.fonts.ready);
            await page.screenshot({ path: path.join(process.env.STUDIO_DEBUG_EVIDENCE, 'neutral-preview.png'), fullPage: true });
        }
        await page.getByLabel('Open item files').setInputFiles(path.join(__dirname, 'Fixtures/Approved/Cat.Item.gbx'));
        await page.getByRole('button', { name: 'Export selected file' }).waitFor();
        const external = await report();
        assert.equal(external.viewer.meshParts, 0);
        assert.ok(external.dependencies.some(d => d.kind === 'mesh' && d.reference === 'Meshes\\Cat.Mesh.gbx'));
        assert.ok(external.diagnostics.some(d => d.code === 'no-mesh-geometry'));
        await page.getByRole('status').filter({ hasText: 'No mesh geometry available.' }).waitFor();
        if (process.env.STUDIO_DEBUG_EVIDENCE) {
            fs.writeFileSync(path.join(process.env.STUDIO_DEBUG_EVIDENCE, 'cat-debug-report.json'), JSON.stringify(external, null, 2) + '\n');
            await page.locator('.studio-diagnostics summary').click();
            await page.evaluate(() => document.fonts.ready);
            await page.screenshot({ path: path.join(process.env.STUDIO_DEBUG_EVIDENCE, 'external-mesh-diagnostics.png'), fullPage: true });
        }
        await page.getByLabel('Open item files').setInputFiles({ name: 'invalid.Item.Gbx', mimeType: 'application/octet-stream', buffer: Buffer.from('invalid') });
        await page.locator('.alert-danger').waitFor();
        const failed = await report();
        assert.equal(failed.status, 'error');
        assert.equal(failed.identity, null);
        assert.equal(failed.viewer.meshParts, 0);
        assert.equal(failed.viewer.runtime.initialized, false);
        assert.deepEqual(failed.dependencies, []);
        assert.deepEqual(errors, []);
        console.log('PASS: real upload, neutral fallback, current debug API/download, external mesh diagnostics and failed-upload reset');
    } finally { await browser.close(); }
};
