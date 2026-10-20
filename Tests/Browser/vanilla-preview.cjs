const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Real local GameData only. No fabricated geometry or texture payloads.
const root = process.argv[2];
assert.ok(root, 'Usage: vanilla-preview.cjs <extracted GameData> [dependency folder]');
const dependencies = process.argv[3] || root;
const output = path.resolve(process.env.STUDIO_EVIDENCE_DIR || 'artifacts/vanilla-evidence');
fs.mkdirSync(output, { recursive: true });

(async () => {
    const browser = await chromium.launch({ headless: true,
        ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}),
        args: ['--use-angle=d3d11'] });
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
    page.setDefaultTimeout(60000);
    const errors = [], evidence = [];
    page.on('pageerror', error => errors.push(error.message));
    async function settle() {
        await page.waitForFunction(() => window.getStudioViewerDebugInfo?.().meshes > 0);
        // Freeze only the test camera between captures to avoid wasting GPU/CPU
        // while .NET parsing/tests run. All model/material functions stay real.
        await page.evaluate(() => { cancelAnimationFrame(animationFrameId); animationFrameId = null; });
        await page.waitForFunction(() => {
            const visible = [];
            staticGroup?.traverse(mesh => { if (mesh.isMesh && mesh.visible) visible.push(mesh); });
            return visible.length && visible.every(mesh => mesh.material.map);
        });
        await page.evaluate(() => { controls.update(); renderer.render(scene, camera); });
    }
    async function capture(name) {
        await page.evaluate(() => renderer.render(scene, camera));
        await page.screenshot({ path: path.join(output, name + '.png') });
        evidence.push(await page.evaluate(name => ({ name, viewer: getStudioViewerDebugInfo(),
            textures: getStudioTexturePreviewInfo(), mood: getStudioMoodInfo(),
            visibleMeshes: staticGroup.children.filter(mesh => mesh.isMesh && mesh.visible).length }), name));
        console.log('CAPTURE ' + name);
    }
    async function load(relative, name) {
        const file = path.join(root, relative);
        const input = await page.locator('#item-upload').count() ? '#item-upload' : '#empty-item-upload';
        await page.locator(input).setInputFiles(file);
        await page.waitForFunction(label => document.querySelector('.studio-item-tab-name')?.textContent.includes(label), path.basename(file).replace(/\.gbx$/i, '')).catch(async error => {
            console.error(await page.locator('body').innerText()); throw error;
        });
        await settle();
        if (name) await capture(name);
    }
    try {
        await page.goto(process.env.STUDIO_BASE_URL || 'http://localhost:5183/', { waitUntil: 'networkidle' });
        await page.locator('#vanilla-folder-upload').setInputFiles(dependencies);
        await page.waitForFunction(() => document.body.innerText.includes('Textures and moods indexed'));
        if (process.env.STUDIO_EVIDENCE_CHARACTER_ONLY === '1') {
            await load('ShootMania/Items/Characters/CharacterPilot.Item.gbx', '03-character-pilot');
            assert.ok(await page.getByText('Partial native archive;', { exact: false }).count());
            return;
        }
        await load('Stadium/Items/CactusMedium.Item.Gbx');
        await page.getByRole('button', { name: 'VisModel, Materials & LOD', exact: true }).click();
        await page.locator('#texture-folder-upload').setInputFiles(dependencies);
        await page.waitForFunction(() => document.body.innerText.includes('files indexed).'));
        await settle();
        await page.locator('.studio-inspector-content').evaluate(element => { element.scrollTop = element.scrollHeight; });
        await capture('01-vanilla-textures-materials');
        await page.getByRole('button', { name: 'VisModel, Materials & LOD', exact: true }).click();
        await capture('02-vanilla-grid');
        if (process.env.STUDIO_EVIDENCE_TEXTURE_ONLY === '1') return;
        for (const car of ['Sport', 'Snow', 'Rally', 'Desert'])
            await load(`Vehicles/Items/Cars/Car${car}.Item.Gbx`, `03-car-${car.toLowerCase()}`);
        await load('ShootMania/Items/Characters/CharacterPilot.Item.gbx', '03-character-pilot');
        await load('Skins/Models/CharacterPilot/Stadium/Female/Player.Mesh.gbx', '03-character-female-mesh');
        await load('Stadium/Media/Static/Vegetation/Cactus.StaticObject.Gbx', '03-static-solid');
        await load('Vehicles/Items/Cars/CarSport.Item.Gbx');
        await page.getByRole('button', { name: 'VisModel, Materials & LOD', exact: true }).click();
        assert.ok(await page.getByRole('heading', { name: 'VisModel, Materials & LOD ?' }).count(), 'Read-only materials inspector should open');
        await page.getByRole('button', { name: 'VisModel, Materials & LOD', exact: true }).click();
        for (const mood of ['Morning', 'Day', 'Evening', 'Night']) {
            await page.getByRole('button', { name: mood, exact: true }).click();
            await page.waitForFunction(name => getStudioMoodInfo().name === name, mood.toLowerCase());
            await capture('04-mood-' + mood.toLowerCase());
        }
        await page.getByRole('button', { name: 'Neutral', exact: true }).click();
        await page.waitForFunction(() => getStudioMoodInfo().name === 'neutral');
        assert.equal(errors.length, 0, errors.join('\n'));
        fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ evidence, errors }, null, 2));
        console.log(`${evidence.length} real browser captures; no page errors.`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
