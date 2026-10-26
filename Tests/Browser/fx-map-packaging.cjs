const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

// UI-only acceptance test. Point STUDIO_BASE_URL at an already built Studio.
// Run again with the first downloaded map to package a second FX item into it.
// FX_PACKAGE_REPORT=1 also clicks the optional JSON report download button.
// FX_PACKAGE_TARGET chooses an exact target if the same basename is placed
// under multiple directories; otherwise a single unambiguous target is required.
const args = process.argv.slice(2);
assert.equal(args.length, 3,
    'Usage: STUDIO_BASE_URL=<url> node fx-map-packaging.cjs <exact FX Item.Gbx> <native saved Map.Gbx> <unused output directory>');
assert.ok(process.env.STUDIO_BASE_URL, 'STUDIO_BASE_URL is required; this test does not launch a server');
const base = new URL(process.env.STUDIO_BASE_URL).href;
assert.ok(['http:', 'https:'].includes(new URL(base).protocol), 'STUDIO_BASE_URL must use HTTP or HTTPS');
const [itemPath, mapPath, output] = args.map(value => path.resolve(value));
assert.ok(/\.Item\.Gbx$/i.test(itemPath), 'The source must be the exact exported Item.Gbx');
assert.ok(/\.Map\.Gbx$/i.test(mapPath), 'The source map must end in .Map.Gbx');
for (const file of [itemPath, mapPath]) assert.ok(fs.statSync(file).isFile(), `Input must be a file: ${file}`);
assert.ok(!fs.existsSync(output), `Refusing to overwrite an existing output directory: ${output}`);
const itemHash = sha(fs.readFileSync(itemPath));
const mapHash = sha(fs.readFileSync(mapPath));
fs.mkdirSync(output, { recursive: true });

function sha(bytes) { return crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase(); }
function unused(name) {
    assert.equal(path.basename(name), name, 'A download must supply a filename without a directory');
    assert.ok(name.length > 0 && !name.includes('\\') && !name.includes(':') && !/[\x00-\x1f]/.test(name),
        'A download must supply an ordinary filename');
    const destination = path.join(output, name);
    assert.ok(!fs.existsSync(destination), `Refusing to overwrite output: ${destination}`);
    return destination;
}
function writeJson(name, value) {
    fs.writeFileSync(unused(name), JSON.stringify(value, null, 2), { encoding: 'utf8', flag: 'wx' });
}

(async () => {
    let browser, page;
    const pageErrors = [], downloads = [];
    try {
        browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
        page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
        page.setDefaultTimeout(120000);
        page.setDefaultNavigationTimeout(120000);
        page.on('pageerror', error => pageErrors.push(error.message));
        page.on('download', download => downloads.push(download));
        await page.goto(base, { waitUntil: 'networkidle' });

        await page.getByLabel('Open item files', { exact: true }).setInputFiles(itemPath);
        await page.waitForFunction(async selection => {
            const report = await window.studioDebug?.getReport?.();
            return report?.status === 'loaded' && report.selection === selection
                && report.fx?.nativeOffsetExportSupported === true && report.viewer?.runtime?.meshes > 0;
        }, path.basename(itemPath).replace(/\.Gbx$/i, ''));
        const loaded = await page.evaluate(() => window.studioDebug.getReport());
        const panel = page.locator('section[aria-label="FX map packaging"]');
        if (!await panel.isVisible())
            await page.getByRole('button', { name: 'Audio, CPlugFx & MediaTracker Triggers', exact: true }).click();
        await panel.waitFor({ state: 'visible' });

        await page.getByLabel('FX item for map packaging', { exact: true }).setInputFiles(itemPath);
        await page.waitForFunction(() => {
            const input = document.getElementById('fxPackageMap');
            return input && !input.disabled;
        });
        await page.getByLabel('Saved map for FX packaging', { exact: true }).setInputFiles(mapPath);
        await page.waitForFunction(() => {
            const section = document.querySelector('section[aria-label="FX map packaging"]');
            return section?.querySelector('#fxPackageTarget') || section?.querySelector('[role="alert"]');
        });
        const alert = panel.getByRole('alert');
        if (await alert.count()) throw new Error((await alert.innerText()).trim());

        const targetSelect = page.getByLabel('Placed FX item to package', { exact: true });
        const targets = await targetSelect.locator('option').evaluateAll(options => options.map(option => ({
            path: option.value, text: option.textContent.trim()
        })));
        assert.ok(targets.length > 0, 'The saved map must have a matching placed item');
        for (const target of targets) {
            assert.equal(target.path.split('/').at(-1), path.basename(itemPath), 'Target basename must match the exact uploaded file');
            assert.match(target.text, /\([1-9]\d* placed\)$/, 'A target must represent at least one existing placement');
        }
        const requestedTarget = process.env.FX_PACKAGE_TARGET?.replaceAll('\\', '/');
        if (requestedTarget) {
            assert.ok(targets.some(target => target.path === requestedTarget), 'FX_PACKAGE_TARGET must match an offered target exactly');
            await targetSelect.selectOption(requestedTarget);
        } else assert.equal(targets.length, 1, 'Multiple matching paths require an explicit FX_PACKAGE_TARGET');
        const selectedTarget = await targetSelect.inputValue();
        if (loaded.identity?.id)
            assert.equal(selectedTarget, loaded.identity.id.replaceAll('\\', '/'), 'The selected path must match the stored native source ID');

        const packageButton = page.getByRole('button', { name: 'Download packaged FX map (.Map.Gbx)', exact: true });
        await page.waitForFunction(() => {
            const section = document.querySelector('section[aria-label="FX map packaging"]');
            return section && !section.querySelector('button')?.disabled;
        });
        assert.equal(await packageButton.isEnabled(), true, 'Packaging must be enabled for the matching placement');
        assert.equal(downloads.length, 0, 'File selection must not trigger downloads');
        const mapDownloadPromise = page.waitForEvent('download');
        await packageButton.click();
        const mapDownload = await mapDownloadPromise;
        const suggestedFilename = mapDownload.suggestedFilename();
        assert.match(suggestedFilename, /\.Map\.Gbx$/i, 'Packaging must download a map');
        const savedMap = unused(suggestedFilename);
        await mapDownload.saveAs(savedMap);
        assert.equal(await mapDownload.failure(), null, 'The packaged map download must complete');
        const reportButton = page.getByRole('button', { name: 'Download FX map report (.json)', exact: true });
        await reportButton.waitFor({ state: 'visible' });
        assert.equal(downloads.length, 1, 'Default packaging must download only one Map.Gbx');
        const outputBytes = fs.readFileSync(savedMap);
        assert.ok(outputBytes.length > 25 && outputBytes.subarray(0, 3).toString('ascii') === 'GBX', 'Downloaded output must contain GBX bytes');
        const outputHash = sha(outputBytes);
        const defaultDownloadCount = downloads.length;

        let packagingReport = null, savedReport = null;
        if (process.env.FX_PACKAGE_REPORT === '1') {
            const reportDownloadPromise = page.waitForEvent('download');
            await reportButton.click();
            const reportDownload = await reportDownloadPromise;
            assert.match(reportDownload.suggestedFilename(), /\.json$/i);
            savedReport = unused(reportDownload.suggestedFilename());
            await reportDownload.saveAs(savedReport);
            assert.equal(await reportDownload.failure(), null, 'The optional report download must complete');
            packagingReport = JSON.parse(fs.readFileSync(savedReport, 'utf8'));
            assert.equal(packagingReport.sourceMapSha256, mapHash);
            assert.equal(packagingReport.sourceItemSha256, itemHash);
            assert.equal(packagingReport.outputSha256, outputHash);
            assert.equal(packagingReport.target.ItemRelativePath, selectedTarget);
            assert.equal(packagingReport.preservation.exactOriginalFxItemBytesEmbedded, true);
            assert.equal(packagingReport.preservation.inputBuffersUnchanged, true);
            assert.equal(packagingReport.preservation.nativeRenderingTested, false);
            assert.equal(downloads.length, 2, 'The optional report is a second explicit download');
        }
        assert.equal(sha(fs.readFileSync(itemPath)), itemHash, 'Original item file must remain unchanged');
        assert.equal(sha(fs.readFileSync(mapPath)), mapHash, 'Original saved map must remain unchanged');
        assert.deepEqual(pageErrors, [], 'The browser must not report page errors');
        await panel.scrollIntoViewIfNeeded();
        await page.screenshot({ path: unused('packaging.png'), fullPage: true });
        const report = {
            passed: true, studioBaseUrl: base,
            inputs: { item: itemPath, itemSha256: itemHash, map: mapPath, mapSha256: mapHash },
            targets, selectedTarget,
            output: { map: savedMap, suggestedFilename, bytes: outputBytes.length, sha256: outputHash, report: savedReport },
            defaultDownloadCount, totalDownloadCount: downloads.length, pageErrors,
            sourceIdentity: loaded.identity, loadedFx: loaded.fx, packagingReport,
            verification: 'Browser UI upload, exact target selection and completed download; native playback requires separate verification.',
            nativeOrStagingActionsPerformed: false
        };
        writeJson('browser-report.json', report);
        console.log(JSON.stringify(report, null, 2));
    } catch (error) {
        if (page) await page.screenshot({ path: unused('failure.png'), fullPage: true }).catch(() => {});
        writeJson('failure-report.json', { error: String(error.stack || error), pageErrors,
            inputs: { item: itemPath, map: mapPath }, nativeOrStagingActionsPerformed: false });
        throw error;
    } finally {
        if (browser) await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
