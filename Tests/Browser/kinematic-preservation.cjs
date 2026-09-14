const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const fixture = path.join(__dirname, 'Fixtures/Approved/TM2020/SnowCarTraffic_60kph.Item.gbx');
const inspect = (file, edits = []) => JSON.parse(execFileSync(process.env.DOTNET_HOST || 'dotnet',
    [path.join(__dirname, 'MotionArchive/bin/Debug/net8.0/MotionArchive.dll'), file, ...edits],
    { encoding: 'utf8', env: { ...process.env, DOTNET_ROLL_FORWARD: 'Major' } }));

(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-motion-preservation-'));
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const expected = inspect(fixture);
        assert.ok(expected.length > 0, 'SnowCar must contain authored constraints');
        const page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1440, height: 1000 } });
        page.setDefaultTimeout(45000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5184/', { waitUntil: 'networkidle' });
        // Replace only the clock; upload, parsing, binding and Three rendering stay real.
        await page.evaluate(() => {
            let next = 0;
            const frames = new Map();
            window.requestAnimationFrame = callback => { frames.set(++next, callback); return next; };
            window.cancelAnimationFrame = id => frames.delete(id);
            window.motionTestTick = time => {
                const callbacks = [...frames.values()]; frames.clear();
                callbacks.forEach(callback => callback(time));
            };
        });
        await page.getByLabel('Open item files').setInputFiles(fixture);
        await page.getByRole('button', { name: 'Export selected file' }).waitFor();
        await page.getByRole('button', { name: /^2\. SnowCarTraffic/ }).click();
        await page.getByRole('button', { name: /WaypointType, Gameplay & Kinematics/ }).click();
        const downloaded = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Export selected file' }).click();
        const exported = path.join(work, 'snowcar.Item.Gbx');
        await (await downloaded).saveAs(exported);
        assert.deepEqual(inspect(exported), expected, 'No-op UI export must preserve every authored constraint in every variant');
        console.log('PASS: SnowCar no-op export preserves all authored constraint bytes.');
        const displacement = await page.evaluate(() => {
            const meshes = [];
            scene.traverse(object => { if (object.isMesh && !object.userData.isCollision
                && object.geometry.attributes.position.count > 1000) meshes.push(object); });
            const mesh = meshes.sort((a, b) => b.geometry.attributes.position.count - a.geometry.attributes.position.count)[0];
            if (!mesh) throw new Error('Uploaded SnowCar geometry not found');
            const points = () => {
                scene.updateMatrixWorld(true);
                return [0, 100].map(i => new THREE.Vector3().fromBufferAttribute(mesh.geometry.attributes.position, i)
                    .applyMatrix4(mesh.matrixWorld));
            };
            motionTestTick(0);
            const before = points();
            for (let t = 40; t <= 960; t += 40) motionTestTick(t);
            return points().map((point, i) => point.sub(before[i]).toArray());
        });
        // Native duration mode: 0 -> 32m along Z in 1920ms, no rotation.
        // Two independently observed vertices must both translate 16m at 960ms.
        assert.equal(expected[0].translationIsDuration, true);
        assert.ok(displacement.every(([x, y, z]) => Math.abs(x) < .001 && Math.abs(y) < .001 && Math.abs(z - 16) < .001),
            `Authored duration-mode motion must translate without invented rotation: ${JSON.stringify(displacement)}`);
        assert.match(await page.locator('body').innerText(), /IsDuration=true.*preserved/);
        assert.equal(await page.getByLabel('Translation minimum (m)', { exact: true }).inputValue(), '0');
        assert.equal(await page.getByLabel('Translation maximum (m)', { exact: true }).inputValue(), '32');
        assert.equal(await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).inputValue(), '1920');
        assert.equal(await page.getByText('Using authored motion settings', { exact: true }).count(), 1,
            'Import must select original motion, not a replacement preset');
        console.log('PASS: authored duration-mode preview and exact imported controls. Reopening export.');
        await page.reload({ waitUntil: 'networkidle' });
        await page.getByLabel('Open item files').setInputFiles(exported);
        await page.getByRole('button', { name: /^2\./ }).click();
        await page.getByRole('button', { name: /WaypointType, Gameplay & Kinematics/ }).click();
        assert.equal(await page.getByText('Using authored motion settings', { exact: true }).count(), 1,
            'Reopened item must still use its authored motion');
        if (process.env.STUDIO_MOTION_EVIDENCE) {
            fs.mkdirSync(process.env.STUDIO_MOTION_EVIDENCE, { recursive: true });
            await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).scrollIntoViewIfNeeded();
            await page.evaluate(() => document.fonts.ready);
            await page.screenshot({ path: path.join(process.env.STUDIO_MOTION_EVIDENCE, 'snowcar-authored-controls.png'), fullPage: true });
        }
        // Variant changes must read that variant's controls, including descending ranges.
        for (let variant = 1; variant < 9; variant++) {
            console.log(`Checking SnowCar variant ${variant + 1}`);
            await page.getByRole('button', { name: new RegExp(`^${variant + 1}\\.`) }).click();
            const first = expected.find(c => c.path.includes(`/variant:${variant}/`));
            assert.ok(first);
            assert.equal(Number(await page.getByLabel('Translation maximum (m)', { exact: true }).first().inputValue()), first.TransMax);
            assert.equal(Number(await page.getByLabel('Rotation minimum (degrees)', { exact: true }).first().inputValue()), first.AngleMinDeg);
            assert.equal(Number(await page.getByLabel('Rotation maximum (degrees)', { exact: true }).first().inputValue()), first.AngleMaxDeg);
            assert.equal(Number(await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).first().inputValue()), first.translation[0].milliseconds);
        }
        await page.getByRole('button', { name: /^2\./ }).click();
        console.log('Checking no-op after variant switches and explicit edits.');
        const exportTo = async filename => {
            const event = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Export selected file' }).click();
            const file = path.join(work, filename);
            await (await event).saveAs(file);
            return file;
        };
        assert.deepEqual(inspect(await exportTo('after-variant-tour.Item.Gbx')), expected,
            'Switching every variant must not rewrite any constraint');
        await page.getByLabel('Translation maximum (m)', { exact: true }).fill('40');
        await page.getByLabel('Translation maximum (m)', { exact: true }).press('Tab');
        assert.equal(await page.evaluate(() => typedMotions[0].fields.translationMax), 40,
            'An authored edit must rebuild the live motion alongside invalidated shared geometry');
        await page.getByRole('button', { name: /^3\./ }).click();
        await page.getByRole('button', { name: /^2\./ }).click();
        assert.equal(await page.evaluate(() => typedMotions[0].fields.translationMax), 40,
            'Returning to an edited variant must not resurrect stale cached motion');
        assert.deepEqual(inspect(await exportTo('range-edit.Item.Gbx')), inspect(fixture, ['translation-max', '40']),
            'Changing the range must preserve all other serialized fields, timelines and constraints');
        await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).fill('1440');
        await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).press('Tab');
        const edited = await exportTo('duration-edit.Item.Gbx');
        assert.deepEqual(inspect(edited), inspect(fixture, ['translation-max', '40', 'translation-duration', '1440']),
            'Changing one key must preserve mode, easing, reversal, other keys and independent rotation');
        await page.reload({ waitUntil: 'networkidle' });
        await page.getByLabel('Open item files').setInputFiles(edited);
        await page.getByRole('button', { name: /^2\./ }).click();
        await page.getByRole('button', { name: /WaypointType, Gameplay & Kinematics/ }).click();
        assert.equal(await page.getByLabel('Translation maximum (m)', { exact: true }).inputValue(), '40');
        assert.equal(await page.getByLabel('Translation key 1 duration (ms)', { exact: true }).inputValue(), '1440');
        assert.deepEqual(errors, []);
        console.log('PASS: authored controls and motion, all SnowCar variants, exact range/key edits and reopen.');
    } finally {
        await browser.close();
        fs.rmSync(work, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
