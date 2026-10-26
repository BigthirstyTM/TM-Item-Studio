const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const { spawnSync } = require('node:child_process');
const os = require('node:os');
const path = require('node:path');
const zlib = require('node:zlib');

const root = path.resolve(__dirname, '../..');
const sourceFixture = process.env.STUDIO_WAVING_SOURCE_FIXTURE || path.join(root,
    'Tests/WavingFlagExport/bin/Debug/net8.0/fixtures/InlineStaticClothSource.Item.Gbx');
const unsupportedFixture = path.join(__dirname, 'Fixtures/Approved/TM2020/SnowCarTraffic_60kph.Item.gbx');
const hashes = {
    Swallowtail: '81B749218584A9CB399AF27088E049132C2E06AA029A21E24137298C0138376D',
    Long250: 'D4F44EB23EE07B0C59CC0E9A976EAE05405B4DDE139BB2673C429F327EAFE751',
    Round: 'C55638E2B2DE29A1A4ABED7994AB613A2B7ACFE850FEEFFF1E30243005E2A14B',
    'TMIS_MatRemap_Placeholder.Material.Gbx': 'CB3B703C2B5BED9F359919C59B24A08730EE833FC5E422C40062DA061CDA588A',
    'TMIS_MatRemap.GameSkin.Gbx': 'DC6118300D7C4FDBA9DAA737130B45249A5E46F1AA56AC17A6D7F658CFA6859E',
    'TMIS_MatRemap.TerrainModifier.Gbx': 'D276FA3E545347EC01533401B9A44A8EC2254BCDE9C2C7BFA4FAFC7CE20D942B'
};
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();

// Read the central directory and real downloaded payloads; no mocked export API.
function readZip(buffer) {
    let end = -1;
    for (let i = buffer.length - 22; i >= Math.max(0, buffer.length - 65557); i--)
        if (buffer.readUInt32LE(i) === 0x06054b50) { end = i; break; }
    assert.ok(end >= 0, 'Downloaded package must have a ZIP central directory');
    const entries = new Map();
    let central = buffer.readUInt32LE(end + 16);
    for (let i = 0, count = buffer.readUInt16LE(end + 10); i < count; i++) {
        assert.equal(buffer.readUInt32LE(central), 0x02014b50);
        const method = buffer.readUInt16LE(central + 10);
        const compressed = buffer.readUInt32LE(central + 20);
        const uncompressed = buffer.readUInt32LE(central + 24);
        const nameLength = buffer.readUInt16LE(central + 28);
        const name = buffer.subarray(central + 46, central + 46 + nameLength).toString('utf8');
        assert.ok(!entries.has(name), `Duplicate ZIP entry ${name}`);
        const local = buffer.readUInt32LE(central + 42);
        assert.equal(buffer.readUInt32LE(local), 0x04034b50);
        const start = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
        const payload = buffer.subarray(start, start + compressed);
        assert.ok(method === 0 || method === 8, `Unexpected ZIP method ${method}`);
        const bytes = method === 8 ? zlib.inflateRawSync(payload) : payload;
        assert.equal(bytes.length, uncompressed);
        entries.set(name, bytes);
        central += 46 + nameLength + buffer.readUInt16LE(central + 30) + buffer.readUInt16LE(central + 32);
    }
    return entries;
}

function inspectPackage(file, mode) {
    const entries = readZip(fs.readFileSync(file));
    assert.equal(entries.size, 6, 'One Item, three dependencies, manifest and English instructions');
    const manifestPath = [...entries.keys()].find(name => name.endsWith('/manifest.json'));
    assert.ok(manifestPath);
    const folder = manifestPath.slice(0, manifestPath.lastIndexOf('/'));
    assert.ok([...entries.keys()].every(name => name.startsWith(folder + '/') && name.split('/').length === 2));
    const manifest = JSON.parse(entries.get(manifestPath));
    assert.equal(manifest.mode, mode);
    assert.equal(manifest.requiresNativeFlag16m, true);
    assert.equal(manifest.standaloneSingleItem, false);
    assert.equal(manifest.nativeSavedMapRepairRequired, true);
    assert.equal(manifest.newSourceMeshNativeVerified, false);
    assert.equal(manifest.referenceLayout.ancestorLevel, 0);
    assert.equal(manifest.referenceLayout.gameSkinSourceUseFile, true);
    assert.equal(manifest.referenceLayout.itemMaterialUseFile, false);
    assert.equal(manifest.files.length, 4);
    for (const info of manifest.files) {
        const bytes = entries.get(folder + '/' + info.file);
        assert.ok(bytes, `Missing ${info.file}`);
        assert.equal(bytes.length, info.bytes);
        assert.equal(sha(bytes), info.sha256);
        if (hashes[info.file]) assert.equal(sha(bytes), hashes[info.file], 'Dependency bytes must match audited resources');
    }
    const item = entries.get(folder + '/' + manifest.itemFile);
    if (mode !== 'SourceMesh') {
        assert.equal(sha(item), hashes[mode], `${mode} must retain the exact proven Item bytes`);
        assert.equal(manifest.geometry.exactTestedBytes, true);
    } else {
        assert.equal(manifest.geometry.framesPerLod, 86);
        assert.equal(manifest.geometry.lods, 5);
        assert.equal(manifest.geometry.anchoredEdgeBitwiseFixed, true);
        assert.equal(manifest.geometry.loopEndpointsBitwiseEqual, true);
        assert.ok(manifest.geometry.verticesPerFrame > 0 && manifest.geometry.trianglesPerFrame > 0);
        assert.match(manifest.geometry.status, /not been tested in Trackmania/i);
    }
    return { mode, file: path.basename(file), zipSha256: sha(fs.readFileSync(file)), itemSha256: sha(item), manifest };
}

async function checkWavingFlags(baseUrl, work) {
    const fixtureRoot = process.env.STUDIO_WAVING_MAP_FIXTURES;
    assert.ok(fs.existsSync(sourceFixture), `Generate the source fixture first: ${sourceFixture}`);
    assert.ok(fixtureRoot, 'STUDIO_WAVING_MAP_FIXTURES must name the offline map fixture directory');
    for (const name of ['source-package.zip', 'native-saved.Map.Gbx', 'no-native.Map.Gbx', 'expected-repaired.Map.Gbx'])
        assert.ok(fs.existsSync(path.join(fixtureRoot, name)), `Missing map fixture ${name}`);
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    const results = [];
    const errors = [];
    const downloads = [];
    let page;
    try {
        page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1440, height: 1000 } });
        page.setDefaultTimeout(45000);
        page.on('pageerror', error => errors.push(error.message));
        page.on('download', download => downloads.push(download.suggestedFilename()));
        const open = async fixture => {
            await page.goto(baseUrl, { waitUntil: 'networkidle' });
            await page.getByLabel('Open item files', { exact: true }).setInputFiles(fixture);
            await page.getByRole('button', { name: /Export selected file/ }).waitFor();
            await page.getByRole('button', { name: 'Flag item conversion', exact: true }).click();
            await page.getByRole('region', { name: 'Waving flag converter', exact: true }).waitFor();
        };
        await open(unsupportedFixture);
        let panel = page.getByRole('region', { name: 'Waving flag converter', exact: true });
        const exporting = () => panel.getByRole('button', { name: 'Export waving flag package (.zip)', exact: true });
        assert.equal(await panel.getByLabel('Cloth source', { exact: true }).inputValue(), 'SourceMesh');
        assert.equal(await exporting().isDisabled(), true, 'Unsupported dynamic source must refuse SourceMesh conversion');
        assert.match(await panel.innerText(), /unsupported|not supported|static|dynamic|kinematic/i);
        assert.equal(downloads.length, 0, 'Unsupported source upload/refusal must not create a download');

        const presetNames = new Set();
        for (const mode of ['Swallowtail', 'Long250', 'Round']) {
            await panel.getByLabel('Cloth source', { exact: true }).selectOption(mode);
            assert.equal(await exporting().isEnabled(), true, 'Preset export remains available for an unsupported source');
            const pending = page.waitForEvent('download');
            await exporting().click();
            const file = path.join(work, `browser-${mode}.zip`);
            const download = await pending;
            assert.ok(!presetNames.has(download.suggestedFilename()), 'Default preset packages need distinct folder/file names');
            assert.ok(download.suggestedFilename().includes(mode));
            presetNames.add(download.suggestedFilename());
            await download.saveAs(file);
            results.push({ ...inspectPackage(file, mode), suggestedFilename: download.suggestedFilename() });
            await panel.getByRole('status').filter({ hasText: /Exported .*ready for the map packaging step/ }).waitFor();
        }
        console.log('PASS: three browser preset downloads retain exact Items and audited dependency hashes.');

        await open(sourceFixture);
        panel = page.getByRole('region', { name: 'Waving flag converter', exact: true });
        await panel.getByRole('status').filter({ hasText: 'Supported inline static triangle mesh' }).waitFor();

        // The Flag Conversion tab must accept a viewport/list selection without
        // sending the user back to Materials & LOD. The exported manifest is
        // the ground-truth that only the selected visual path was authored.
        await page.getByRole('button', { name: 'VisModel, Materials & LOD', exact: true }).click();
        const firstPart = page.locator('.mesh-config-table tbody tr').first();
        await firstPart.waitFor();
        const selectedPartText = await firstPart.innerText();
        const selectedPartPath = selectedPartText.split(/\n/).map(value => value.trim()).filter(value => value.includes('/')).at(-1);
        assert.ok(selectedPartPath, 'Fixture must expose a selectable mesh path');
        await firstPart.click();
        await page.getByRole('button', { name: 'Flag item conversion', exact: true }).click();
        panel = page.getByRole('region', { name: 'Waving flag converter', exact: true });
        await panel.getByLabel('Geometry to convert', { exact: true }).selectOption('selected');
        await panel.getByRole('status').filter({ hasText: 'Selected visual:' }).waitFor();
        assert.match(await panel.innerText(), new RegExp(selectedPartPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
        await page.locator('#wavingFlagAnchorX').fill('1.25');
        await page.locator('#wavingFlagAnchorX').press('Tab');
        assert.equal(await page.locator('#wavingFlagAnchorX').inputValue(), '1.25');
        await panel.getByLabel('Export name', { exact: true }).fill('Browser_Selected_Cloth');
        const selectedDownload = page.waitForEvent('download');
        await panel.getByRole('button', { name: 'Export waving flag package (.zip)', exact: true }).click();
        const selectedFile = path.join(work, 'browser-selected-source-mesh.zip');
        await (await selectedDownload).saveAs(selectedFile);
        const selectedResult = inspectPackage(selectedFile, 'SourceMesh');
        assert.deepEqual(selectedResult.manifest.geometry.selectionPaths, [selectedPartPath]);
        assert.equal(selectedResult.manifest.geometry.anchor.X, 1.25);
        results.push(selectedResult);
        console.log('PASS: viewport mesh selection and editable flag anchor reach the SourceMesh export manifest.');

        await open(sourceFixture);
        panel = page.getByRole('region', { name: 'Waving flag converter', exact: true });
        await panel.getByRole('status').filter({ hasText: 'Supported inline static triangle mesh' }).waitFor();
        await panel.getByLabel('Export name', { exact: true }).fill('Browser_Custom_Cloth');
        await panel.getByLabel('Wave amplitude (m)', { exact: true }).fill('0.4');
        await panel.getByLabel('Wavelength (m)', { exact: true }).fill('3');
        await panel.getByLabel('Loop period (s)', { exact: true }).fill('10');
        await panel.getByLabel('Fixed edge in item coordinates', { exact: true }).selectOption('MaximumX');
        const sourceDownload = Promise.race([
            page.waitForEvent('download'),
            panel.getByRole('alert').filter({ hasText: 'Flag export refused:' }).waitFor().then(async () => {
                throw new Error(await panel.getByRole('alert').innerText());
            })
        ]);
        await exporting().click();
        const converted = path.join(work, 'browser-SourceMesh.zip');
        await (await sourceDownload).saveAs(converted);
        const sourceResult = inspectPackage(converted, 'SourceMesh');
        assert.equal(sourceResult.manifest.geometry.edge, 'MaximumX');
        assert.equal(sourceResult.manifest.geometry.Amplitude, 0.4);
        assert.equal(sourceResult.manifest.geometry.Wavelength, 3);
        assert.equal(sourceResult.manifest.geometry.PeriodSeconds, 10);
        results.push(sourceResult);
        await panel.getByRole('status').filter({ hasText: /Exported .*ready for the map packaging step/ }).waitFor();
        await panel.getByLabel('Export name', { exact: true }).scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(work, 'headless-studio-source-mesh-export.png') });
        console.log('PASS: supported inline source uploads and exports an explicitly unverified 86-frame/five-LOD package.');

        for (const [name, success] of [['native-saved.Map.Gbx', true], ['no-native.Map.Gbx', false]]) {
            await open(sourceFixture);
            panel = page.getByRole('region', { name: 'Waving flag converter', exact: true });
            const packageButton = panel.getByRole('button', { name: 'Download embedded flag map (.Map.Gbx)', exact: true });
            assert.equal(await packageButton.isDisabled(), true);
            await panel.getByLabel('Native-saved map (.Map.Gbx)', { exact: true }).setInputFiles(path.join(fixtureRoot, name));
            await panel.getByLabel('Flag package ZIP', { exact: true }).setInputFiles(path.join(fixtureRoot, 'source-package.zip'));
            await panel.getByRole('status').filter({ hasText: 'Flag package selected:' }).waitFor();
            await packageButton.waitFor({ state: 'visible' });
            await page.waitForFunction(() => document.querySelector('[aria-label="Waving flag converter"] button:last-of-type')?.disabled === false);
            const downloadCount = downloads.length;
            if (success) {
                const event = Promise.race([
                    page.waitForEvent('download'),
                    panel.getByRole('alert').filter({ hasText: 'Map packaging refused:' }).waitFor().then(async () => {
                        throw new Error(await panel.getByRole('alert').innerText());
                    })
                ]);
                await packageButton.click();
                const output = path.join(work, 'browser-repaired.Map.Gbx');
                await (await event).saveAs(output);
                const preservationFile = path.join(work, 'offline-preservation-verification.json');
                const preservation = spawnSync('dotnet', ['run', '--project', 'Tests/WavingFlagMapPackaging', '--no-build', '--',
                    '--verify-output', output, path.join(fixtureRoot, 'expected-repaired.Map.Gbx'),
                    path.join(fixtureRoot, name), path.join(fixtureRoot, 'source-package.zip'), preservationFile], {
                    cwd: path.resolve(__dirname, '../..'), encoding: 'utf8', timeout: 120000
                });
                assert.equal(preservation.status, 0, `Independent browser map audit failed: ${preservation.error?.message || ''}\n${preservation.stdout}\n${preservation.stderr}`);
                const mapAudit = JSON.parse(fs.readFileSync(preservationFile, 'utf8'));
                assert.equal(mapAudit.allChecksPassed, true);
                assert.equal(mapAudit.browser.sha256, sha(fs.readFileSync(output)));
                await panel.getByRole('status').filter({ hasText: 'Test this exact file after restarting Trackmania.' }).waitFor();
                const reportDownload = page.waitForEvent('download');
                await panel.getByRole('button', { name: 'Download map packaging report (.json)', exact: true }).click();
                const reportPath = path.join(work, 'browser-map-packaging.json');
                await (await reportDownload).saveAs(reportPath);
                const mapReport = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
                assert.equal(mapReport.operation, 'waving-flag-native-map-export-repair');
                assert.equal(mapReport.outputSha256, sha(fs.readFileSync(output)));
                assert.equal(mapReport.sourceMapSha256, sha(fs.readFileSync(path.join(fixtureRoot, name))));
                assert.equal(mapReport.sourcePackageSha256, sha(fs.readFileSync(path.join(fixtureRoot, 'source-package.zip'))));
                assert.ok(mapReport.nativeFlag16mCount >= 1);
                assert.equal(mapReport.nativeFlagAdded, false);
                results.push({ repairedMapSha256: sha(fs.readFileSync(output)), downloadedReportHashesMatch: true,
                    independentMapPreservationAudit: mapAudit });
                await panel.getByRole('button', { name: 'Download map packaging report (.json)', exact: true }).scrollIntoViewIfNeeded();
                await page.screenshot({ path: path.join(work, 'headless-studio-map-package.png') });
            } else {
                await packageButton.click();
                await panel.getByRole('alert').filter({ hasText: /Map packaging refused:.*(?:Flag16m|native.*flag)/i }).waitFor();
                assert.equal(downloads.length, downloadCount, 'No-native refusal must produce no map download');
                assert.equal(await panel.getByRole('button', { name: 'Download map packaging report (.json)', exact: true }).count(), 0);
            }
        }
        assert.deepEqual(errors, [], 'Real upload/export/map packaging must cause no browser page errors');
        const report = { scope: 'Headless browser authoring workflow only; no native game test',
            checks: { exactThreePresetPayloads: true, supportedSourceMeshDownload: true,
                unsupportedSourceRefused: true, nativeSavedMapDecodedPayloadsAndRawPreservationMatchBackend: true,
                noNativeFlagMapRefusedWithoutDownload: true, noPageErrors: true }, results };
        fs.writeFileSync(path.join(work, 'waving-flag-browser-validation.json'), JSON.stringify(report, null, 2) + '\n');
        console.log('PASS: native-saved map has exact decoded payloads/raw preservation; missing native Flag16m refuses without download; no page errors.');
        return report;
    } catch (error) {
        if (page) {
            fs.writeFileSync(path.join(work, 'failure-page.txt'), await page.locator('body').innerText().catch(() => 'Body unavailable'));
            await page.screenshot({ path: path.join(work, 'failure-headless-studio.png') }).catch(() => {});
        }
        fs.writeFileSync(path.join(work, 'failure.json'), JSON.stringify({ message: error.message, stack: error.stack, pageErrors: errors, downloads }, null, 2) + '\n');
        throw error;
    } finally { await browser.close(); }
}

module.exports = checkWavingFlags;
if (require.main === module) {
    const work = process.env.STUDIO_WAVING_EVIDENCE || fs.mkdtempSync(path.join(os.tmpdir(), 'studio-waving-browser-'));
    fs.mkdirSync(work, { recursive: true });
    checkWavingFlags(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', work)
        .catch(error => { console.error(error); process.exitCode = 1; });
}
