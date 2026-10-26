const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

// Uses a fresh ephemeral server by default so an already running Studio and
// the Trackmania runtime remain untouched.
const repo = path.resolve(__dirname, '../..');
const fixtures = process.argv.slice(2);
assert.ok(fixtures.length, 'Usage: fx-mesh-preview.cjs <FX Item.Gbx> [...]');
const output = path.join(repo, 'artifacts/fx-clones/fx-mesh-preview-20261025');
const numberSetting = (name, fallback) => {
    const value = Number(process.env[name] ?? fallback);
    assert.ok(Number.isFinite(value), `${name} must be finite`);
    return value;
};
const targetPoint = {
    x: numberSetting('FX_EMITTER_X', 0),
    y: numberSetting('FX_EMITTER_Y', 3.5),
    z: numberSetting('FX_EMITTER_Z', 0)
};
const expectedMeshMaxY = numberSetting('FX_EXPECTED_MESH_MAX_Y', 3);
const pointMatches = (point, expected) => ['x', 'y', 'z'].every(axis =>
    Number.isFinite(point?.[axis]) && Math.abs(point[axis] - expected[axis]) < .0001);
const gizmoMatches = (report, expected) => {
    const point = report.viewer?.runtime?.fxEmitters?.[0]?.itemPosition;
    return ['x', 'y', 'z'].every((axis, index) =>
        Number.isFinite(point?.[index]) && Math.abs(point[index] - expected[axis]) < .0001);
};
fs.mkdirSync(output, { recursive: true });

(async () => {
    let server;
    const base = process.env.STUDIO_BASE_URL || await new Promise(resolve => {
        const roots = [path.join(repo, 'bin/Debug/net8.0/wwwroot'), path.join(repo, 'wwwroot')];
        const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
            '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp' };
        server = http.createServer((request, response) => {
            const requested = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            const relative = requested === '/' ? 'index.html' : requested.slice(1);
            const file = roots.map(root => path.resolve(root, relative))
                .find((candidate, index) => candidate.startsWith(roots[index] + path.sep) && fs.existsSync(candidate) && fs.statSync(candidate).isFile());
            if (!file) { response.writeHead(404); response.end(); return; }
            response.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
            response.setHeader('Cache-Control', 'no-store');
            fs.createReadStream(file).pipe(response);
        }).listen(0, '127.0.0.1', () => resolve(`http://127.0.0.1:${server.address().port}/`));
    });
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        page.setDefaultTimeout(120000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(base, { waitUntil: 'networkidle' });
        const reports = [];
        for (const [index, item] of fixtures.entries()) {
            await page.getByLabel('Open item files', { exact: true }).setInputFiles(item);
            await page.getByText('Embedded FX mesh', { exact: true }).waitFor();
            await page.waitForFunction(async selection => {
                const report = await window.studioDebug?.getReport();
                return report?.status === 'loaded' && report?.selection === selection && report?.viewer?.runtime?.meshes > 0;
            }, path.basename(item).replace(/\.Gbx$/i, ''));
            const report = await page.evaluate(() => window.studioDebug.getReport());
            console.log(`Loaded mesh: ${path.basename(item)}`);
            assert.ok(report.viewer.meshParts > 0 && report.viewer.vertices > 0 && report.viewer.triangles > 0,
                `${path.basename(item)} must display its actual embedded geometry`);
            assert.ok(!report.scene?.diagnostics?.some(diagnostic => diagnostic.code === 'no-mesh-geometry'));
            await page.screenshot({ path: path.join(output, `mesh-${index + 1}.png`), fullPage: true });
            reports.push({ file: path.basename(item), viewer: report.viewer, scene: report.scene });
            if (process.env.FX_ITEM_SPACE_ROUNDTRIP === '1' && index === 0) {
                const chosenId = process.env.FX_EXPORT_ID || null;
                const sourceHeaderId = report.identity.id ?? null;
                const sourceHasNullId = sourceHeaderId == null || sourceHeaderId === '';
                if (chosenId) {
                    if (!await page.locator('#ident-id').isVisible())
                        await page.getByRole('button', { name: 'Item Identification & Collector Metadata', exact: true }).click();
                    await page.locator('#ident-id').fill(chosenId);
                    await page.locator('#ident-id').press('Tab');
                    const internalName = page.locator('label[data-help-key="internalitemname"]').locator('..').locator('input');
                    await internalName.fill(chosenId);
                    await internalName.press('Tab');
                }
                assert.equal(report.viewer.fxEmitterPointSpace, 'item-local metres');
                const initialY = numberSetting('FX_EXPECTED_INITIAL_Y', .69592285);
                assert.ok(Math.abs(report.viewer.fxEmitterPoint.y - initialY) < .0001,
                    'Loaded FX origin includes its actual owner translation');
                assert.ok(Math.abs(report.viewer.runtime.meshBounds.max[1] - expectedMeshMaxY) < .0001,
                    'Custom mesh has its expected maximum item-space Y');
                assert.ok(Math.abs(report.viewer.runtime.fxEmitters[0].itemPosition[1] - initialY) < .0001,
                    'Rendered cyan gizmo uses item coordinates, including owner translation');
                assert.ok(gizmoMatches(report, report.viewer.fxEmitterPoint),
                    'Loaded cyan gizmo agrees with all three item-space coordinates');
                await page.getByRole('button', { name: 'Audio, CPlugFx & MediaTracker Triggers', exact: true }).click();
                const y = page.getByLabel('FX emission point Y', { exact: true });
                await y.fill('3.25');
                await page.waitForFunction(async () => Math.abs((await window.studioDebug.getReport()).viewer.runtime.fxEmitters[0].itemPosition[1] - 3.25) < .0001);
                await y.fill(String(targetPoint.y));
                await page.getByLabel('FX emission point X', { exact: true }).fill(String(targetPoint.x));
                const z = page.getByLabel('FX emission point Z', { exact: true });
                await z.fill(String(targetPoint.z));
                await z.press('Tab');
                await page.waitForFunction(async expected => {
                    const current = await window.studioDebug.getReport();
                    const gizmo = current.viewer?.runtime?.fxEmitters?.[0]?.itemPosition;
                    return ['x', 'y', 'z'].every((axis, index) =>
                        Number.isFinite(current.viewer?.fxEmitterPoint?.[axis])
                        && Math.abs(current.viewer.fxEmitterPoint[axis] - expected[axis]) < .0001
                        && Number.isFinite(gizmo?.[index]) && Math.abs(gizmo[index] - expected[axis]) < .0001);
                }, targetPoint);
                const moved = await page.evaluate(() => window.studioDebug.getReport());
                assert.ok(pointMatches(moved.viewer.fxEmitterPoint, targetPoint),
                    'All three edited item-space coordinates are reflected in Studio');
                assert.ok(gizmoMatches(moved, targetPoint),
                    'Cyan gizmo reflects all three requested coordinates');
                assert.ok(Math.abs(moved.viewer.runtime.fxEmitters[0].itemPosition[1] - moved.viewer.runtime.meshBounds.max[1]
                    - (targetPoint.y - expectedMeshMaxY)) < .0001,
                    'Cyan emitter has the requested height relative to the actual custom mesh');
                console.log(`Exporting item-space (${targetPoint.x}, ${targetPoint.y}, ${targetPoint.z}) through Studio...`);
                assert.equal(await page.getByLabel('Include FX diagnostics', { exact: true }).isChecked(), false);
                const defaultDownloads = [];
                const recordDownload = download => defaultDownloads.push(download);
                page.on('download', recordDownload);
                const downloadPromise = page.waitForEvent('download', { timeout: 120000 });
                await page.getByRole('button', { name: 'Export FX item (.Item.Gbx)', exact: true }).click();
                let download;
                try { download = await downloadPromise; }
                catch (error) {
                    console.error(JSON.stringify(await page.evaluate(() => window.studioDebug.getReport()), null, 2));
                    await page.screenshot({ path: path.join(output, 'export-failure.png'), fullPage: true });
                    throw error;
                }
                const suggestedFilename = download.suggestedFilename();
                if (chosenId) assert.equal(suggestedFilename, chosenId + '.Item.Gbx');
                const savedFilename = suggestedFilename;
                const expectedNativeId = sourceHasNullId ? null : (chosenId ? chosenId + '.Item.Gbx' : suggestedFilename);
                const saved = path.join(output, savedFilename);
                await download.saveAs(saved);
                page.off('download', recordDownload);
                assert.equal(defaultDownloads.length, 1, 'Default FX export downloads only one Item.Gbx');
                console.log(`Saved Studio export: ${saved}`);
                await page.getByLabel('Open item files', { exact: true }).setInputFiles(saved);
                let reopened;
                const deadline = Date.now() + 120000;
                while (Date.now() < deadline) {
                    const candidate = await page.evaluate(() => window.studioDebug.getReport());
                    if (candidate.selection === savedFilename.replace(/\.Gbx$/i, '') && candidate.status === 'loaded'
                        && candidate.viewer?.runtime?.meshes > 0 && candidate.viewer.runtime.meshBounds
                        && pointMatches(candidate.viewer.fxEmitterPoint, targetPoint)
                        && gizmoMatches(candidate, targetPoint)) {
                        reopened = candidate;
                        break;
                    }
                    await page.waitForTimeout(50);
                }
                assert.ok(reopened, 'The exported item must finish reopening with mesh and item-space origin');
                if (sourceHasNullId) {
                    assert.ok(reopened.identity.id == null || reopened.identity.id === '',
                        'A source with native null/empty ID reopens with native null/empty ID');
                } else {
                    assert.equal(reopened.identity.id, expectedNativeId,
                        'A source with an explicit ID reopens with the exported native filename ID');
                }
                assert.ok(pointMatches(reopened.viewer.fxEmitterPoint, targetPoint),
                    'All three requested native coordinates survive item-only reopen');
                assert.ok(gizmoMatches(reopened, targetPoint),
                    'Reopened cyan gizmo reflects all three exported coordinates');
                assert.ok(Math.abs(reopened.viewer.runtime.meshBounds.max[1] - expectedMeshMaxY) < .0001,
                    'Item-only reopen retains the custom embedded mesh');
                const evidenceName = savedFilename.replace(/\.Item\.Gbx$/i, '');
                await page.screenshot({ path: path.join(output, `${evidenceName}-gizmo.png`), fullPage: true });
                fs.writeFileSync(path.join(output, `${evidenceName}-roundtrip-report.json`), JSON.stringify({ itemFile: saved,
                    requestedEmissionPoint: targetPoint, expectedMeshMaxY,
                    exportMapping: { chosenId, suggestedFilename, savedFilename, downloadCount: defaultDownloads.length,
                        sourceHeaderId, expectedNativeId, actualHeaderId: reopened.identity.id ?? null },
                    before: report, moved, reopened }, null, 2));
            }
        }
        assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(output, 'browser-report.json'), JSON.stringify({ passed: fixtures.length, reports }, null, 2));
        console.log(JSON.stringify({ passed: fixtures.length, reports: reports.map(report => ({ file: report.file, viewer: report.viewer })) }, null, 2));
    } finally {
        await browser.close();
        if (server) await new Promise(resolve => server.close(resolve));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
