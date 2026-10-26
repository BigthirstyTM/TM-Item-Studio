const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const source = path.join(__dirname, 'Fixtures/animation-static-first.Item.Gbx');
const archive = (...args) => execFileSync('dotnet', [path.join(root, 'Tests/ItemIcons/bin/Debug/net8.0/ItemIcons.dll'), ...args],
    { encoding: 'utf8', env: { ...process.env, DOTNET_ROLL_FORWARD: 'Major' } });
(async () => {
    const work = fs.mkdtempSync(path.join(os.tmpdir(), 'studio-icons-'));
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ acceptDownloads: true, viewport: { width: 1440, height: 1000 } });
        const errors = []; page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183', { waitUntil: 'networkidle' });
        const raw = path.join(work, 'raw.Item.Gbx'); archive('prepare', source, raw);
        const open = async file => {
            await page.getByLabel('Open item files', { exact: true }).setInputFiles(file);
            await page.getByRole('button', { name: 'Export selected file', exact: false }).waitFor();
            const tab = page.getByRole('button', { name: 'Item Identification & Collector Metadata', exact: true });
            if (await tab.getAttribute('aria-expanded') !== 'true') await tab.click();
            await page.locator('#iconPreviewCanvas').waitFor();
        };
        const corners = () => page.locator('#iconPreviewCanvas').evaluate(c => {
            const ctx = c.getContext('2d');
            return [[.25,.25],[.75,.25],[.25,.75],[.75,.75]].map(([x,y]) => [...ctx.getImageData(Math.floor(c.width*x), Math.floor(c.height*y),1,1).data]);
        });
        const expected = [[255,0,0,255],[0,255,0,255],[0,0,255,255],[255,255,0,255]];
        const checkCorners = async () => {
            await page.waitForFunction(() => {
                const c = document.getElementById('iconPreviewCanvas');
                return c && c.getContext('2d').getImageData(c.width/4,c.height/4,1,1).data[0] > 240;
            });
            const actual = await corners();
            assert.ok(actual.every((rgba, i) => rgba.every((v, j) => Math.abs(v-expected[i][j]) < 8)), JSON.stringify(actual));
        };
        await open(raw); await checkCorners();
        assert.deepEqual(await page.locator('#iconPreviewCanvas').evaluate(c => [c.width,c.height]), [32,16]);
        await page.getByLabel('IconQuarterRotationY', { exact: true }).fill('3');
        await page.getByLabel('IconQuarterRotationY', { exact: true }).press('Tab');
        await checkCorners();
        const normalized = await page.evaluate(async () => {
            const c = document.createElement('canvas'); c.width=c.height=64;
            const ctx = c.getContext('2d');
            for (const [x,y,color] of [[0,0,'red'],[32,0,'#00ff00'],[0,32,'blue'],[32,32,'yellow']]) {
                ctx.fillStyle=color; ctx.fillRect(x,y,32,32);
            }
            const png = [...new Uint8Array(await (await fetch(c.toDataURL())).arrayBuffer())];
            return { png, ...(await normalizeIcon(new Uint8Array(png),64)) };
        });
        const webp = path.join(work, 'stored.webp'); fs.writeFileSync(webp, Buffer.from(normalized.webPBase64,'base64'));
        const encoded = path.join(work, 'webp.Item.Gbx'); archive('prepare',source,encoded,webp);
        await open(encoded); await checkCorners();
        const before = await corners();
        await page.getByLabel('Replace item icon', { exact: true }).setInputFiles({ name:'broken.png',mimeType:'image/png',buffer:Buffer.from('not an image') });
        await page.getByRole('status').filter({hasText:'Could not decode this image'}).waitFor();
        assert.deepEqual(await corners(),before,'Invalid replacement must retain previous icon');
        // This source has no icon header: replacement must add it, not disappear on export.
        await open(source);
        await page.getByRole('status').filter({hasText:'No stored icon'}).waitFor();
        assert.equal(await page.locator('#iconPreviewCanvas').evaluate(c => c.getContext('2d').getImageData(1,1,1,1).data[3]),0,'Missing icon must clear stale pixels');
        await page.evaluate(png => {
            const files = new DataTransfer(); files.items.add(new File([new Uint8Array(png)],'quadrants.png',{type:'image/png'}));
            document.getElementById('icon-dropzone').dispatchEvent(new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:files}));
        },normalized.png);
        await page.getByRole('status').filter({hasText:'Icon normalized to 64x64'}).waitFor();
        await checkCorners();
        const download = page.waitForEvent('download');
        await page.getByRole('button',{name:'Export selected file',exact:false}).click();
        const output = process.env.STUDIO_ICON_EXPORT || path.join(work,'quadrants.Item.Gbx');
        fs.mkdirSync(path.dirname(output),{recursive:true}); await (await download).saveAs(output);
        await open(output); await checkCorners();
        // Browser decoder failures and genuine black/transparent pixels have distinct states.
        assert.equal(await page.evaluate(() => renderIconPreviewFromBytes('iconPreviewCanvas',new Uint8Array([1,2,3]))),'decode-failed');
        assert.equal(await page.evaluate(() => renderIconPreview('iconPreviewCanvas',[0,0,0,255],1,1)),'black');
        assert.equal(await page.evaluate(() => renderIconPreview('iconPreviewCanvas',[0,0,0,0],1,1)),'transparent');
        assert.deepEqual(errors,[]);
        console.log('PASS: raw rectangular orientation; metadata leaves pixels unchanged; GBX WebP flip; invalid upload atomicity; missing icon clears; actual drop/export/reopen; black/transparent/decode states.');
    } finally { await browser.close(); fs.rmSync(work,{recursive:true,force:true}); }
})().catch(e => { console.error(e); process.exitCode=1; });
