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
        await page.getByLabel('Open item files', { exact: true }).setInputFiles(
            path.join(__dirname, 'Fixtures/Approved/TM2020/SupportConnectorX6.Item.Gbx'));
        await page.getByRole('button', { name: 'VisModel, Materials & LOD', exact: true }).click();
        const picker = page.locator('.material-slot-editor .searchable-select').first();
        const search = picker.getByRole('searchbox');
        const select = picker.getByRole('combobox');
        await search.waitFor();
        const initial = await select.inputValue();
        assert.ok(await select.locator('option').count() <= 202, 'Material DOM must stay bounded');
        await search.fill('no-such-material-782349');
        await picker.getByText('No matching options.').waitFor();
        assert.equal(await select.inputValue(), initial, 'Filtering must not mutate the selected material');
        await search.fill('platform');
        await page.waitForFunction(() => [...document.querySelectorAll('.material-slot-editor select option')].some(o => /platform/i.test(o.value)));
        const choices = await select.locator('option').evaluateAll(options => options.map(o => o.value));
        assert.ok(choices.every(value => !value || value === initial || /platform/i.test(value)));
        const chosen = choices.find(value => value && value !== initial);
        assert.ok(chosen, 'Library must offer a matching material');
        await select.selectOption(chosen);
        await page.waitForFunction(value => document.querySelector('.material-slot-editor input[id^="material-link-"]')?.value === value, chosen);
        assert.deepEqual(errors, []);
        console.log('PASS: bounded material choices, full-path search, no-match state, selection preservation and authored edit.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
