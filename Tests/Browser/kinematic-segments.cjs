const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const fixture = path.join(__dirname, 'Fixtures/Approved/TM2020/SnowCarTraffic_60kph.Item.gbx');
const inspect = (file, edits = []) => JSON.parse(execFileSync('dotnet',
    [path.join(__dirname, 'MotionArchive/bin/Debug/net8.0/MotionArchive.dll'), file, ...edits],
    { encoding: 'utf8', env: { ...process.env, DOTNET_ROLL_FORWARD: 'Major' } }));

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-segments-'));
    try {
        const page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5184/', { waitUntil: 'networkidle' });
        await page.getByLabel('Open item files').setInputFiles(fixture);
        await page.getByRole('button', { name: /^2\. SnowCarTraffic/ }).click();
        await page.getByRole('button', { name: /WaypointType, Gameplay & Kinematics/ }).click();
        assert.equal(await page.getByLabel('Translation segment count', { exact: true }).count(), 1, 'Translation must expose its actual segment count');
        const original = inspect(fixture);
        for (const name of ['Translation', 'Rotation'])
            assert.equal(Number(await page.getByLabel(`${name} segment count`, { exact: true }).inputValue()), original[0][name.toLowerCase()].length);
        async function download() {
            // SnowCar re-encodes on save in this WASM writer; the export gate added with
            // standalone exports requires the acknowledgement checkbox before exporting.
            const gate = page.locator('#acknowledge-reencoded-export');
            if (await gate.count()) await gate.check();
            const event = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Export selected file' }).click();
            const file = path.join(work, 'segments.Item.Gbx');
            await (await event).saveAs(file);
            return file;
        }
        assert.deepEqual(inspect(await download()), original, 'Showing count controls must not alter imports');
        const edits = [];
        for (const [name, count] of [['Translation', 1], ['Translation', 3], ['Rotation', 1], ['Rotation', 4], ['Translation', 2]]) {
            await page.getByLabel(`${name} segment count`, { exact: true }).selectOption(String(count));
            assert.equal(await page.getByRole('spinbutton', { name: new RegExp(`^${name} key \\d+ (duration|end time)`) }).count(), count);
            edits.push(`${name.toLowerCase()}-count`, String(count));
            assert.deepEqual(inspect(await download()), inspect(fixture, edits), 'Count edits must preserve retained keys, the other timeline, and other constraints');
        }
        const exported = await download();
        await page.getByLabel('Open item files').setInputFiles(exported);
        await page.getByRole('button', { name: /^2\./ }).click();
        assert.equal(await page.getByLabel('Translation segment count', { exact: true }).inputValue(), '2');
        assert.equal(await page.getByLabel('Rotation segment count', { exact: true }).inputValue(), '4');
        assert.equal(await page.getByLabel('Translation key 2 duration (ms)', { exact: true }).inputValue(), '1000');
        assert.deepEqual(inspect(await download()), inspect(fixture, edits));
        await page.getByLabel('Translation segment count', { exact: true }).selectOption('1');
        await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).fill('2147483647');
        await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).press('Tab');
        const beforeRejected = inspect(await download());
        await page.getByLabel('Translation segment count', { exact: true }).selectOption('2');
        await page.getByRole('alert').filter({ hasText: 'Total duration exceeds' }).waitFor();
        assert.equal(await page.getByLabel('Translation segment count', { exact: true }).inputValue(), '1', 'Rejected count must reset the visible selector');
        assert.deepEqual(inspect(await download()), beforeRejected, 'Rejected count must leave the archive unchanged');
        assert.deepEqual(errors, []);
        console.log('PASS: real upload, segment counts, retained functions, independent timelines, exact archive comparison, export and reopen.');
    } finally { await browser.close(); fs.rmSync(work, { recursive: true, force: true }); }
})().catch(error => { console.error(error); process.exitCode = 1; });
