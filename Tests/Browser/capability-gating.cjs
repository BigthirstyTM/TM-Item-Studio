const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

// Bespoke light fixture contract: a static ornament with one serialized
// CPlugLightUserModel (color .25/.5/.75, intensity 4, distance 12) owned by a
// prefab entry at (1,2,3). Upload the archive normally; never inject scene data.
const fixture = process.argv[2];
assert.ok(fixture, 'Usage: node capability-gating.cjs <light-capability.Item.Gbx>');

(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-capability-gating-'));
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1440, height: 1000 } });
        page.setDefaultTimeout(45000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        await page.getByLabel('Open item files').setInputFiles(fixture);
        await page.getByRole('button', { name: 'Export selected file' }).waitFor();
        await page.waitForFunction(() => typeof renderer !== 'undefined' && renderer?.domElement.isConnected
            && lightsGroup?.children.some(child => child.userData.type === 'light'));

        // Socket gizmos no longer exist, so the sockets layer toggle must be gone.
        assert.equal(await page.getByRole('button', { name: /^Sockets \(/ }).count(), 0, 'Socket layer toggle still present');
        assert.equal(await page.evaluate(() => typeof socketsGroup), 'undefined', 'Socket gizmo group still exists in the viewer');

        // Lights tab: read-only position, no add/remove, honest labels.
        await page.getByRole('button', { name: 'Dynamic Real-Time Lights (1)' }).click();
        await page.getByText('Existing serialized lights only').waitFor();
        assert.equal(await page.getByRole('button', { name: '+ Add Light' }).count(), 0, 'Add Light button still present');
        assert.equal(await page.getByRole('button', { name: '✕', exact: true }).count(), 0, 'Light remove button still present');
        for (const axis of ['X', 'Y', 'Z']) {
            const input = page.getByLabel(`Light 1 ${axis} (read-only)`, { exact: true });
            assert.ok(await input.evaluate(el => el.readOnly), `Light ${axis} input is not read-only`);
        }
        assert.equal(await page.getByLabel('Light 1 X (read-only)', { exact: true }).inputValue(), '1');
        assert.equal(await page.getByLabel('Light 1 Y (read-only)', { exact: true }).inputValue(), '3.2');
        assert.equal(await page.getByLabel('Light 1 Z (read-only)', { exact: true }).inputValue(), '3');

        // App-level gizmo gating: the C# payload must mark light gizmos uneditable,
        // so selection works but transform handles never attach. (Selecting a light
        // gizmo also focuses the lights tab, so this runs after the tab checks.)
        const gizmo = await page.evaluate(() => {
            selectGizmoFromUI('light', 0);
            const group = lightsGroup.children.find(child => child.userData.index === 0);
            return { editable: group.userData.editable, attached: transformControls.object !== undefined };
        });
        assert.equal(gizmo.editable, false, 'Light gizmo payload must be marked not editable');
        assert.equal(gizmo.attached, false, 'Light gizmo must not attach drag handles');

        // Sockets tab: read-only capability note, no authoring controls.
        await page.getByRole('button', { name: 'Socket authoring capability (read-only)' }).click();
        await page.getByText('Socket authoring is not supported.').waitFor();
        assert.equal(await page.getByRole('button', { name: '+ Add Socket' }).count(), 0, 'Add Socket button still present');
        assert.equal(await page.getByRole('button', { name: 'Select in 3D' }).count(), 0, 'Socket select-in-3D control still present');

        // Physics tab: read-only diagnostic, no user toggle.
        await page.getByRole('button', { name: 'PhyModel, CPlugSurface & Collision' }).click();
        await page.getByText('Enabling or disabling collision is not supported').waitFor();
        assert.equal(await page.locator('#collisionInfo').count(), 0, 'CollisionEnabled checkbox still present');

        // Gameplay tab: conversion buttons disabled with honest wording. A prefab-backed
        // entity model reads as a moving item, so this fixture exposes Convert to Static.
        await page.getByRole('button', { name: 'WaypointType, Gameplay & Kinematics' }).click();
        const convertStatic = page.getByRole('button', { name: /Convert to StaticObject/ });
        await convertStatic.waitFor();
        assert.ok(await convertStatic.isDisabled(), 'Convert to StaticObject must be disabled');
        await page.getByText('Removing the dynamic graph is not supported').waitFor();

        // Persisted light controls still round-trip: color, intensity, radius.
        await page.getByRole('button', { name: 'Dynamic Real-Time Lights (1)' }).click();
        await page.getByLabel('Light 1 intensity', { exact: true }).fill('7.5');
        await page.getByLabel('Light 1 intensity', { exact: true }).press('Tab');
        await page.getByLabel('Light 1 radius', { exact: true }).fill('21');
        await page.getByLabel('Light 1 radius', { exact: true }).press('Tab');
        await page.getByLabel('Light 1 color', { exact: true }).fill('#00ff00');
        const exportTo = async filename => {
            const event = page.waitForEvent('download');
            await page.getByRole('button', { name: 'Export selected file' }).click();
            const file = path.join(work, filename);
            await (await event).saveAs(file);
            return file;
        };
        const edited = await exportTo('light-edit.Item.Gbx');
        await page.reload({ waitUntil: 'networkidle' });
        await page.getByLabel('Open item files').setInputFiles(edited);
        await page.getByRole('button', { name: 'Dynamic Real-Time Lights (1)' }).click();
        assert.equal(await page.getByLabel('Light 1 intensity', { exact: true }).inputValue(), '7.5', 'Light intensity edit did not round-trip');
        assert.equal(await page.getByLabel('Light 1 radius', { exact: true }).inputValue(), '21', 'Light radius edit did not round-trip');
        assert.equal(await page.getByLabel('Light 1 color', { exact: true }).inputValue(), '#00ff00', 'Light color edit did not round-trip');
        for (const [axis, expected] of [['X', '1'], ['Y', '3.2'], ['Z', '3']]) {
            const input = page.getByLabel(`Light 1 ${axis} (read-only)`, { exact: true });
            assert.ok(await input.evaluate(el => el.readOnly), `Reopened light ${axis} input is not read-only`);
            assert.equal(await input.inputValue(), expected, `Reopened light ${axis} display position changed`);
        }
        console.log('PASS: gated controls read-only/absent; light color/intensity/radius round-trip.');

        // Preview-only interactions must not change exported bytes.
        const baseline = await exportTo('preview-baseline.Item.Gbx');
        await page.getByRole('button', { name: /^Pivots \(/ }).click();
        await page.getByRole('button', { name: /^Lights \(\d+\)/ }).click();
        await page.getByRole('button', { name: 'Socket authoring capability (read-only)' }).click();
        await page.getByRole('button', { name: 'PhyModel, CPlugSurface & Collision' }).click();
        await page.getByRole('button', { name: 'WaypointType, Gameplay & Kinematics' }).click();
        await page.getByRole('button', { name: 'Dynamic Real-Time Lights (1)' }).click();
        await page.getByRole('button', { name: 'Select in 3D' }).click();
        const afterPreview = await exportTo('preview-after.Item.Gbx');
        assert.deepEqual(fs.readFileSync(afterPreview), fs.readFileSync(baseline),
            'Preview-only interactions changed exported bytes');
        console.log('PASS: export bytes unchanged after preview-only interactions.');

        // A document without a dynamic prefab shows the static-object branch instead:
        // Convert to KinematicObject disabled, honest wording, no trajectory promise.
        const emptyItem = Buffer.from('R0JYBgBCVUNSACAALgAAAAABAAAAAAAAAAQAAAAIAAAAFQHeyvoRAAA=', 'base64');
        await page.getByLabel('Open item files').setInputFiles({ name: 'empty.Item.Gbx', mimeType: 'application/octet-stream', buffer: emptyItem });
        await page.getByRole('button', { name: 'Export selected file' }).waitFor();
        await page.getByRole('button', { name: 'WaypointType, Gameplay & Kinematics' }).click();
        const convertKinematic = page.getByRole('button', { name: /Convert to KinematicObject/ });
        await convertKinematic.waitFor();
        assert.ok(await convertKinematic.isDisabled(), 'Convert to KinematicObject must be disabled');
        await page.getByText('this editor cannot convert it').waitFor();
        assert.equal((await page.locator('body').innerText()).match(/trajectory/ig), null, 'Trajectory promise still present');
        console.log('PASS: both conversion branches disabled with honest wording.');
        assert.deepEqual(errors, []);
    } finally {
        await browser.close();
        fs.rmSync(work, { recursive: true, force: true });
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
