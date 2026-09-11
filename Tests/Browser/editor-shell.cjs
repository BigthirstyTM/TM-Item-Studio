const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const item = Buffer.from('R0JYBgBCVUNSACAALgAAAAABAAAAAAAAAAQAAAAIAAAAFQHeyvoRAAA=', 'base64');
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        for (const theme of ['light', 'dark']) {
            for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
                const page = await browser.newPage({ viewport, colorScheme: theme });
                const errors = [];
                page.on('pageerror', error => errors.push(error.message));
                await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
                const input = page.getByLabel('Open item files');
                await input.waitFor();
                assert.equal(await page.locator('html').getAttribute('data-theme'), theme);
                assert.equal(await page.locator('#blazor-error-ui').isVisible(), false);
                assert.equal(await page.evaluate(() => getComputedStyle(document.body).fontFamily.includes('system-ui')), true);
                await input.setInputFiles({ name: 'empty.Item.Gbx', mimeType: 'application/octet-stream', buffer: item });
                await page.waitForFunction(() => typeof renderer !== 'undefined' && renderer?.domElement.isConnected);
                await documentFonts(page);
                assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Page must not overflow horizontally');
                await page.getByRole('button', { name: 'Export selected file', exact: false }).waitFor();
                if (process.env.STUDIO_SCREENSHOTS) {
                    fs.mkdirSync(process.env.STUDIO_SCREENSHOTS, { recursive: true });
                    await page.screenshot({ path: path.join(process.env.STUDIO_SCREENSHOTS, `editor-${theme}-${viewport.width}.png`), fullPage: true });
                }
                await page.getByRole('button', { name: 'Switch light or dark theme' }).click();
                assert.equal(await page.locator('html').getAttribute('data-theme'), theme === 'dark' ? 'light' : 'dark');
                assert.deepEqual(errors, []);
                await page.close();
            }
        }
        console.log('PASS: local editor styling, theme switch, runtime banner, upload and responsive loaded layout.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

async function documentFonts(page) { await page.evaluate(() => document.fonts.ready); }
