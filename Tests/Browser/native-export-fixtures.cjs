// Produce reviewable browser exports for the separately controlled native MCP tests.
// STUDIO_BASE_URL and STUDIO_NATIVE_EXPORTS must point to a running build and output folder.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
(async () => {
    const output = process.env.STUDIO_NATIVE_EXPORTS;
    if (!output) throw new Error('Set STUDIO_NATIVE_EXPORTS to an evidence folder.');
    fs.mkdirSync(output, { recursive: true });
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    const manifest = [];
    try {
        const page = await browser.newPage({ acceptDownloads: true });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183', { waitUntil: 'networkidle' });
        const fixture = name => {
            const candidates = ['Fixtures/Approved/TM2020', 'Fixtures/Approved']
                .map(folder => path.join(__dirname, folder, name + '.Item.Gbx'));
            return candidates.find(file => fs.existsSync(file)) || candidates[0];
        };
        const open = async files => {
            await page.reload({ waitUntil: 'networkidle' });
            await page.getByLabel('Open item files', { exact: true }).setInputFiles(files);
            await page.getByRole('button', { name: 'Export selected file' }).waitFor();
        };
        const identity = async name => {
            name = name.replace('TMIS_Fixes', process.env.STUDIO_NATIVE_ID_PREFIX || 'TMIS_Fixes');
            const field = page.getByLabel(/^Ident.Id \/ File name$/);
            await field.fill(name); await field.press('Tab');
            const collection = page.getByLabel(/^Ident.Collection$/);
            await collection.fill('26'); await collection.press('Tab');
        };
        const save = async (label, name, inputs) => {
            const gate = page.locator('#acknowledge-reencoded-export');
            if (await gate.count()) await gate.check();
            const download = page.waitForEvent('download');
            await page.getByRole('button', { name: label }).click();
            const dest = path.join(output, name);
            await (await download).saveAs(dest);
            const bytes = fs.readFileSync(dest);
            manifest.push({ name, inputs, bytes: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex') });
            console.log('EXPORTED', name, bytes.length);
        };
        const support = fixture('SupportConnectorX6');
        await open(support);
        await identity('TMIS_Fixes_Export');
        await save('Export selected file', 'TMIS_Export.Item.Gbx', [support]);
        // Keep the same ID/path for the macroblock's external reference.
        await page.getByRole('button', { name: 'Macroblock export and item instancing', exact: true }).click();
        await page.getByLabel('Copies', { exact: true }).fill('2');
        await page.getByLabel('Columns', { exact: true }).fill('2');
        await page.getByLabel('Spacing m', { exact: true }).fill('16');
        await page.getByRole('button', { name: 'Fill grid', exact: false }).click();
        await page.getByLabel('Instance 2 yaw', { exact: true }).fill('90');
        await page.getByLabel('Instance 2 yaw', { exact: true }).press('Tab');
        await save('Export .Macroblock.Gbx', 'TMIS_Pair.Macroblock.Gbx', ['TMIS_Export.Item.Gbx']);
        await save('Export multi-instance .Item.Gbx', 'TMIS_Multi.Item.Gbx', [support]);
        await open([support, fixture('ObstaclePusher4mLevel1')]);
        await identity('TMIS_Fixes_Variants');
        await save('Combine loaded files as variants', 'TMIS_Variants.Item.Gbx', [support, 'ObstaclePusher4mLevel1']);
        await identity('TMIS_Fixes_Merged');
        await save('Merge loaded files into one item', 'TMIS_Merged.Item.Gbx', [support, 'ObstaclePusher4mLevel1']);
        await open(fixture('ObstaclePusher4mLevel1'));
        await identity('TMIS_Fixes_Motion');
        await save('Export selected file', 'TMIS_Motion.Item.Gbx', ['ObstaclePusher4mLevel1']);
        if (process.env.STUDIO_NATIVE_EXTENDED) {
            await open(support); await identity('TMIS_Fixes_Icon');
            const png = await page.evaluate(() => {
                const canvas = document.createElement('canvas'); canvas.width = canvas.height = 64;
                const ctx = canvas.getContext('2d');
                for (const [x, y, color] of [[0,0,'red'],[32,0,'#00ff00'],[0,32,'blue'],[32,32,'yellow']]) {
                    ctx.fillStyle = color; ctx.fillRect(x,y,32,32);
                }
                return canvas.toDataURL().split(',')[1];
            });
            await page.getByLabel('Replace item icon', { exact: true }).setInputFiles({
                name: 'quadrants.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64'),
            });
            await page.getByRole('status').filter({ hasText: 'Icon normalized to 64x64' }).waitFor();
            await save('Export selected file', 'TMIS_Icon.Item.Gbx', [support, 'quadrants.png']);
            await open(support); await identity('TMIS_Fixes_Flag');
            await page.getByRole('button', { name: 'Flag item conversion', exact: true }).click();
            await save('Convert to flag item (.Item.Gbx)', 'TMIS_Flag.Item.Gbx', [support]);
        }
        if (errors.length) throw new Error(errors.join('\n'));
        fs.writeFileSync(path.join(output, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
