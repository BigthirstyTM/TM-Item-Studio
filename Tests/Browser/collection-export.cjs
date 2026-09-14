const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');

module.exports = async function checkCollection(baseUrl, work) {
    const archive = (...args) => execFileSync('dotnet',
        [path.join(__dirname, 'CollectionArchive/bin/Debug/net8.0/CollectionArchive.dll'), ...args],
        { encoding: 'utf8', env: { ...process.env, DOTNET_ROLL_FORWARD: 'Major' } });
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const cases = [
            { name: 'numeric no-op', kind: 'number', value: '26', number: 26, text: null },
            { name: 'numeric ID and author edits', kind: 'number', value: '26', number: 26, text: null, editIdentity: true },
            { name: 'unknown numeric ID', kind: 'number', value: '123456', number: 123456, text: null },
            { name: 'custom string ID', kind: 'text', value: 'BespokeCollection', number: null, text: 'BespokeCollection' },
            { name: 'numeric-looking string ID', kind: 'text', value: '26', number: null, text: '26' },
            { name: 'known name string ID', kind: 'text', value: 'Stadium2020', number: null, text: 'Stadium2020' },
            // GBX encodes an empty string as the empty-ID sentinel (both fields null).
            { name: 'empty ID', kind: 'text', value: '', number: null, text: null },
            { name: 'explicit numeric repair', kind: 'text', value: 'Stadium2020', editCollection: '26', number: 26, text: null },
            { name: 'lookback marker is not a numeric collection', kind: 'number', value: '26', editCollection: '1073741824', number: null, text: '1073741824' },
            { name: 'largest numeric ID', kind: 'number', value: '26', editCollection: '1073741823', number: 1073741823, text: null },
            { name: 'change to custom string', kind: 'number', value: '26', editCollection: 'BespokeCollection', number: null, text: 'BespokeCollection' },
            { name: 'clear collection', kind: 'number', value: '26', editCollection: '', number: null, text: null },
            { name: 'public static reproduction repair', source: 'CustomItem_Static.Item.Gbx', editCollection: '26', number: 26, text: null, id: '', author: '-oTBhm4_S1-UxnlnBizUDA' },
            { name: 'public kinematic reproduction repair', source: 'CustomItem_Kinematic.Item.Gbx', editCollection: '26', number: 26, text: null, id: '', author: 'BigthirstyTM' },
        ];
        for (const test of cases) {
            const page = await browser.newPage();
            const errors = [];
            page.on('pageerror', error => errors.push(error.message));
            await page.goto(baseUrl, { waitUntil: 'networkidle' });
            const input = test.source ? path.resolve(__dirname, '../../Test Exported items', test.source) : path.join(work, 'input.Item.Gbx');
            if (!test.source)
                archive('prepare', path.join(__dirname, 'Fixtures/animation-static-first.Item.Gbx'), input, test.kind, test.value);
            await page.getByLabel('Open item files').setInputFiles(input);
            const exportButton = page.getByRole('button', { name: 'Export selected file' });
            await exportButton.waitFor();
            const field = label => page.getByLabel(label);
            if (test.editIdentity) {
                await field(/^Ident.Id \/ File name$/).fill('RenamedBespoke');
                await field(/^Ident.Author$/).fill('EditedAuthor');
                await field(/^Ident.Author$/).press('Tab');
            }
            if (test.editCollection !== undefined) {
                await field(/^Ident.Collection$/).fill(test.editCollection);
                await field(/^Ident.Collection$/).press('Tab');
            }
            const downloadPromise = Promise.race([
                page.waitForEvent('download'),
                page.locator('.alert-danger').waitFor().then(async () => {
                    throw new Error(`${test.name}: ${await page.locator('.alert-danger').innerText()}`);
                }),
            ]);
            await exportButton.click();
            const download = await downloadPromise;
            const exported = path.join(work, 'exported.Item.Gbx');
            await download.saveAs(exported);
            // Observe the downloaded archive, not component internals. Display names
            // alone cannot distinguish a numeric collection from a string-defined ID.
            assert.deepEqual(JSON.parse(archive('inspect', exported)), {
                number: test.number, text: test.text,
                id: test.editIdentity ? 'RenamedBespoke' : test.id ?? 'BespokeAnimation',
                author: test.editIdentity ? 'EditedAuthor' : test.author ?? 'FixtureGenerator',
            }, test.name);
            await page.reload({ waitUntil: 'networkidle' });
            await page.getByLabel('Open item files').setInputFiles(exported);
            await exportButton.waitFor();
            await page.waitForFunction(() => typeof renderer !== 'undefined' && renderer?.domElement.isConnected);
            assert.equal(await field(/^Ident.Id \/ File name$/).inputValue(), test.editIdentity ? 'RenamedBespoke' : test.id ?? 'BespokeAnimation');
            assert.equal(await field(/^Ident.Author$/).inputValue(), test.editIdentity ? 'EditedAuthor' : test.author ?? 'FixtureGenerator');
            assert.equal(await page.locator('.alert-danger').count(), 0, 'Reopen must succeed');
            assert.deepEqual(errors, [], 'Upload/export/reopen must not raise page errors');
            if (test.source && process.env.STUDIO_COLLECTION_EXPORTS) {
                fs.mkdirSync(process.env.STUDIO_COLLECTION_EXPORTS, { recursive: true });
                fs.copyFileSync(exported, path.join(process.env.STUDIO_COLLECTION_EXPORTS, test.source));
            }
            await page.close();
            console.log(`PASS: upload/export/reopen: ${test.name}`);
        }
    } finally { await browser.close(); }
};
