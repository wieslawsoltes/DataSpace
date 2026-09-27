import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';
import { queryBrowserChecks } from './query-browser-checks.mjs';
import { crosstabBrowserChecks } from './crosstab-browser-checks.mjs';

const baseURL = process.env.DATASPACE_URL || 'http://127.0.0.1:4173/DataSpace/';
const screenshots = process.env.DATASPACE_SCREENSHOTS || 'artifacts/qa';
await mkdir(screenshots, { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
const context = await browser.newContext({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });
const first = await context.newPage();
const second = await context.newPage();
let checks = 0;
try {
    await first.goto(new URL('storage-test.html', baseURL).href);
    await second.goto(new URL('storage-test.html', baseURL).href);
    const databaseName = 'DataSpace.CI.' + Date.now();
    for (const page of [first, second]) {
        await page.evaluate(async ({ databaseName, baseURL }) => {
            const { IndexedWorkspaceStore } = await import(new URL('browser-storage.js', baseURL).href);
            globalThis.testStore = new IndexedWorkspaceStore(databaseName);
        }, { databaseName, baseURL });
    }
    assert.equal(await first.evaluate(() => testStore.load()), null); checks++;
    const payload = JSON.stringify({ Name: 'Żółć 日本語 😀', Revision: 1 });
    const version = await first.evaluate(payload => testStore.save(payload, null), payload);
    assert.equal((await second.evaluate(() => testStore.load())).json, payload); checks++;
    const races = await Promise.allSettled([
        first.evaluate(({ version }) => testStore.save('first writer', version), { version }),
        second.evaluate(({ version }) => testStore.save('second writer', version), { version })
    ]);
    assert.equal(races.filter(result => result.status === 'fulfilled').length, 1);
    assert.equal(races.filter(result => result.status === 'rejected').length, 1); checks++;
    const saved = await first.evaluate(() => testStore.load());
    assert.ok(['first writer', 'second writer'].includes(saved.json)); checks++;
    await assert.rejects(second.evaluate(() => testStore.save('stale writer', 'stale-version'))); checks++;
    assert.deepEqual(await second.evaluate(() => testStore.load()), saved); checks++;
    const corruptPreserved = await first.evaluate(async () => {
        const database = await testStore.open();
        await new Promise((resolve, reject) => {
            const tx = database.transaction('documents', 'readwrite'); tx.objectStore('documents').put({ invalid: true }, 'workspace');
            tx.oncomplete = resolve; tx.onerror = () => reject(tx.error);
        });
        let readFailed = false; let saveFailed = false;
        try { await testStore.load(); } catch { readFailed = true; }
        try { await testStore.save('replacement', null); } catch { saveFailed = true; }
        return readFailed && saveFailed;
    });
    assert.equal(corruptPreserved, true); checks++;
    await first.close(); await second.close();

    const page = await context.newPage();
    const failures = [];
    page.on('pageerror', error => failures.push(error.message));
    page.on('response', response => {
        if (response.status() >= 400 && /\.(wasm|js|json|dll)(\?|$)/i.test(response.url())) failures.push(response.status() + ' ' + response.url());
    });
    page.on('console', message => { if (message.type() === 'error') console.error('Browser:', message.text()); });
    async function ready() {
        await page.waitForFunction(() => document.documentElement.dataset.dataspaceReady === 'true' || document.documentElement.dataset.dataspaceError, null, { timeout: 180000 });
        const status = await page.evaluate(() => ({ ready: document.documentElement.dataset.dataspaceReady, storage: document.documentElement.dataset.dataspaceStorageLoaded, error: document.documentElement.dataset.dataspaceError }));
        assert.equal(status.error, undefined); assert.equal(status.ready, 'true'); assert.equal(status.storage, 'true');
        await page.waitForTimeout(1000);
    }
    async function cellInput(value) {
        await page.waitForFunction(value => [...document.querySelectorAll('input,textarea')].some(element => element.value === value && element.getBoundingClientRect().width > 0), value, { timeout: 20000 });
        const inputs = page.locator('input,textarea');
        for (let index = 0; index < await inputs.count(); index++) {
            const input = inputs.nth(index);
            if (await input.isVisible() && await input.inputValue() === value) return input;
        }
        throw new Error('The datasheet did not expose its native cell editor.');
    }
    try {
        await page.goto(baseURL, { waitUntil: 'domcontentloaded' });
        await ready(); checks++;
        assert.ok(await page.locator('canvas').count() > 0, 'The real Uno/Skia application must contain a drawing canvas.'); checks++;
        await page.screenshot({ path: screenshots + '/workspace.png', fullPage: true });
        await writeFile(screenshots + '/accessibility.txt', await page.locator('body').ariaSnapshot());

        // Fixed viewport and the shipped Northwind fixture: first row, Company column.
        // Real pointer/keyboard input catches a collapsed/blank document host that a startup marker cannot.
        await page.mouse.dblclick(420, 248);
        const input = await cellInput('Northwind Traders');
        const editedValue = 'DataSpace browser regression';
        await input.fill(editedValue);
        await input.press('Enter'); checks++;
        await page.keyboard.press('Control+s');
        await page.waitForFunction(async ({ baseURL, value }) => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href);
            return (await readWorkspace()).includes(value);
        }, { baseURL, value: editedValue }, { timeout: 20000 }); checks++;
        await page.reload({ waitUntil: 'domcontentloaded' });
        await ready();
        await page.mouse.dblclick(420, 248);
        assert.equal(await (await cellInput(editedValue)).inputValue(), editedValue); checks++;
        await page.keyboard.press('Escape');
        await page.screenshot({ path: screenshots + '/after-reload.png', fullPage: true });
        checks += await queryBrowserChecks(page, baseURL, screenshots, ready);
        checks += await crosstabBrowserChecks(page, baseURL, screenshots, ready);
        assert.deepEqual(failures, [], 'No unhandled browser exceptions or missing runtime assets.'); checks++;
        console.log(`PASS: ${checks} browser checks (IndexedDB races, corruption, Unicode, Uno startup, cell edit/save/reload and visual query workflows).`);
    } finally {
        await page.screenshot({ path: screenshots + '/last-state.png', fullPage: true }).catch(() => {});
        await writeFile(screenshots + '/browser-errors.json', JSON.stringify(failures, null, 2));
        console.log('UI input diagnostics:', await page.locator('input,textarea').evaluateAll(elements => elements.map(element => ({ value: element.value, width: element.getBoundingClientRect().width, height: element.getBoundingClientRect().height }))));
    }
} finally { await context.close(); await browser.close(); }
