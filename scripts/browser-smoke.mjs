import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const baseURL = process.env.DATASPACE_URL || 'http://127.0.0.1:4173/DataSpace/';
const screenshots = process.env.DATASPACE_SCREENSHOTS || 'artifacts/qa';
await mkdir(screenshots, { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
const context = await browser.newContext({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });
const first = await context.newPage();
const second = await context.newPage();
let checks = 0;
try {
    // A separate, same-origin blank page tests real IndexedDB, not a JavaScript storage mock.
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
    try {
        await page.goto(baseURL, { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => document.documentElement.dataset.dataspaceReady === 'true' || document.documentElement.dataset.dataspaceError, null, { timeout: 180000 });
        const status = await page.evaluate(() => ({ ready: document.documentElement.dataset.dataspaceReady, storage: document.documentElement.dataset.dataspaceStorageLoaded, error: document.documentElement.dataset.dataspaceError }));
        assert.equal(status.error, undefined); assert.equal(status.ready, 'true'); assert.equal(status.storage, 'true'); checks++;
        assert.ok(await page.locator('canvas').count() > 0, 'The real Uno/Skia application must contain a drawing canvas.'); checks++;
        await page.waitForTimeout(1500);
        await page.screenshot({ path: screenshots + '/workspace.png', fullPage: true });
        assert.deepEqual(failures, [], 'No unhandled browser exceptions or missing runtime assets.'); checks++;
        console.log(`PASS: ${checks} browser checks (IndexedDB concurrency, corruption protection, Unicode and actual Uno startup).`);
    } finally {
        await page.screenshot({ path: screenshots + '/last-state.png', fullPage: true }).catch(() => {});
        await writeFile(screenshots + '/browser-errors.json', JSON.stringify(failures, null, 2));
    }
} finally { await context.close(); await browser.close(); }
