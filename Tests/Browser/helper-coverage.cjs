const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader'] });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.goto(process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5183/', { waitUntil: 'networkidle' });
        await page.getByLabel('Open item files', { exact: true }).setInputFiles(path.join(__dirname, 'Fixtures/Approved/TM2020/SupportConnectorX6.Item.Gbx'));
        await page.getByRole('button', { name: 'Helper', exact: true }).click();
        await page.getByRole('note', { name: 'Getting Started' }).waitFor();
        const panels = await page.locator('.studio-inspector-tabs button').evaluateAll(buttons => buttons.map(b => b.getAttribute('aria-label')));
        const missing = [];
        for (const panel of panels) {
            const button = page.getByRole('button', { name: panel, exact: true });
            if (await button.getAttribute('aria-expanded') !== 'true') await button.click();
            await page.waitForFunction(() => !!document.querySelector('.studio-inspector-tabs button[aria-expanded="true"]'));
            await page.evaluate(() => studioHelp.bind());
            missing.push(...await page.locator('.studio-inspector').evaluate((root, panel) => [...root.querySelectorAll('input,select,textarea,button')]
                .filter(e => !e.disabled && e.getBoundingClientRect().width > 2)
                .map(e => ({ panel, name: e.getAttribute('aria-label') || e.labels?.[0]?.innerText || e.innerText || '', html: e.outerHTML.slice(0, 220), help: helperDescription(e) }))
                .filter(e => !e.name.trim() || !e.help.trim()), panel));
        }
        assert.deepEqual(missing, [], 'Every available setting needs a name and specific help');
        await page.getByRole('button', { name: 'Item Identification & Collector Metadata', exact: true }).click();
        const pageName = page.getByLabel('PageName', { exact: true });
        await pageName.focus();
        await page.getByRole('tooltip').filter({ hasText: 'catalog page/category' }).waitFor();
        assert.match(await pageName.getAttribute('aria-describedby'), /studio-helper-tooltip/);
        await page.getByLabel('CopperPrice', { exact: true }).focus();
        await page.getByRole('tooltip').filter({ hasText: 'historical copper-price' }).waitFor();
        await page.keyboard.press('Escape');
        assert.equal(await page.getByRole('tooltip').isVisible(), false);
        await page.getByRole('button', { name: 'Helper on', exact: true }).click();
        assert.equal(await page.getByRole('note', { name: 'Getting Started' }).count(), 0);
        assert.equal(await page.evaluate(() => document.body.classList.contains('studio-helper-mode')), false);
        for (const width of [390, 768, 1280, 1440]) {
            await page.setViewportSize({ width, height: 900 });
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `No horizontal overflow at ${width}px`);
        }
        assert.deepEqual(errors, []);
        console.log('PASS: every sampled setting has a name and help; specific metadata explanations, focus/ARIA/Escape/off behavior, responsive widths 390–1440.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
