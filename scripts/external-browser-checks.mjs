import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { activateDialogButton } from './native-dialog-actions.mjs';

/** Real local SQLite WASM worker, HTTP gateway and native Uno source workflows. */
export async function externalBrowserChecks(page, baseURL, screenshots, ready) {
    let checks = 0;
    const sqlite = resolve('artifacts/external/items.sqlite');
    const json = resolve('artifacts/external/items.json');
    const fixture = [...await readFile(sqlite)];
    // Public test fixture on the already-running same-origin static test server.
    await writeFile(resolve('artifacts/server/DataSpace/source-fixture.json'), await readFile(json));
    const worker = await page.context().newPage();
    try {
        await worker.goto(new URL('storage-test.html', baseURL).href);
        const result = await worker.evaluate(async ({ baseURL, fixture }) => {
            const { SqliteWorkerClient } = await import(new URL('browser-sqlite.js', baseURL).href);
            const client = new SqliteWorkerClient();
            try {
                const tables = await client.open(new Uint8Array(fixture).buffer);
                const exact = await client.call('page', { table: 'exact_values', offset: 0, limit: 10 });
                const first = await client.call('page', { table: 'items', offset: 0, limit: 200 });
                const last = await client.call('page', { table: 'items', offset: 400, limit: 200 });
                let denied = false; try { await client.call('page', { table: 'items; DROP TABLE items', offset: 0, limit: 1 }); } catch { denied = true; }
                const exported = await client.call('export', { name: 'Copy', columns: ['ID', 'Title'], rows: [['9223372036854775807', 'Unicode 😀'], ['2', null]] });
                await client.open(exported);
                const copy = await client.call('page', { table: 'Copy', offset: 0, limit: 10 });
                return { tables, exact, first, last, denied, copy };
            } finally { client.close(); }
        }, { baseURL, fixture });
        assert.equal(result.tables.length, 2); checks++;
        assert.equal(result.exact.rows[0][0], '9223372036854775807'); checks++;
        assert.equal(result.first.rows.length, 200); assert.equal(result.first.hasMore, true); assert.equal(result.first.rows[0][4], 'hex:0001FF'); checks++;
        assert.equal(result.last.rows.length, 3); assert.equal(result.last.hasMore, false); checks++;
        assert.equal(result.denied, true); checks++;
        assert.deepEqual(result.copy.rows, [['9223372036854775807', 'Unicode 😀'], ['2', null]]); checks++;
        const gateway = await worker.evaluate(async () => {
            const headers = { Authorization: 'Bearer DataSpace_browser_test_token_only_46' };
            const denied = await fetch('http://127.0.0.1:5099/v1/sources');
            const sources = await (await fetch('http://127.0.0.1:5099/v1/sources', { headers })).json();
            const data = await (await fetch('http://127.0.0.1:5099/v1/sources/demo/rows?table=items&offset=400&limit=200', { headers })).json();
            return { status: denied.status, sources, data };
        });
        assert.equal(gateway.status, 401); assert.equal(gateway.sources[0].id, 'demo'); assert.equal(gateway.data.rows.length, 3); checks++;
    } finally { await worker.close(); }

    const peer = (role, name) => page.getByRole(role, { name, exact: true }).first();
    async function button(name, dialog = false) {
        const target = peer('button', name);
        if (dialog) await activateDialogButton(page, target, name); else await target.press('Enter');
        await page.waitForTimeout(200);
    }
    async function focusNative(target, name) {
        await target.waitFor({ state: 'attached' });
        for (const key of ['Tab', 'Shift+Tab']) {
            for (let step = 0; step < 35; step++) {
                await page.keyboard.press(key); await page.waitForTimeout(80);
                if (await target.evaluate(e => e.ownerDocument.activeElement === e)) return;
            }
        }
        throw new Error(name + ' is not reachable through native keyboard navigation.');
    }
    async function edit(name, value) {
        const input = peer('textbox', name); await focusNative(input, name);
        await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace'); await page.keyboard.insertText(value); await page.keyboard.press('Tab'); await page.waitForTimeout(150);
        assert.equal(await input.inputValue(), value);
    }
    async function selectIndex(name, index) {
        const combo = peer('combobox', name); await focusNative(combo, name);
        // Open the native popup before accepting an item. Enter on a collapsed
        // selector can activate the parent dialog's default Close button.
        await page.keyboard.press('Space'); await page.waitForTimeout(200);
        await page.keyboard.press('Home');
        for (let step = 0; step < index; step++) { await page.keyboard.press('ArrowDown'); await page.waitForTimeout(80); }
        await page.keyboard.press('Enter'); await page.waitForTimeout(200);
    }
    async function status(pattern) {
        let snapshot = '';
        for (let attempt = 0; attempt < 120; attempt++) {
            snapshot = await page.locator('body').ariaSnapshot(); if (pattern.test(snapshot)) return;
            await page.waitForTimeout(100);
        }
        throw new Error('External data state was not reached: ' + pattern + '\n' + snapshot);
    }
    async function database() {
        return page.evaluate(async baseURL => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href); const value = await readWorkspace(); return JSON.parse(value.slice(value.indexOf('\n') + 1));
        }, baseURL);
    }
    async function saveTable(name, count) {
        await page.keyboard.press('Control+s');
        for (let attempt = 0; attempt < 100; attempt++) { const table = (await database()).Tables.find(t => t.Name === name); if (table?.Records.length === count) return table; await page.waitForTimeout(100); }
        throw new Error('Imported table was not saved: ' + name);
    }
    try {
        await button('External Data'); await page.screenshot({ path: screenshots + '/external-data-ribbon.png', fullPage: true });
        await button('JSON File');
        await edit('JSON Pointer', '/data/items');
        const choosingJson = page.waitForEvent('filechooser'); await button('Browse / Connect', true); await (await choosingJson).setFiles(json);
        await status(/records 1–3/); await edit('Local table name', 'JSON_Import_Test'); await page.keyboard.press('Tab');
        await page.screenshot({ path: screenshots + '/json-source-preview.png', fullPage: true });
        await button('Import table', true); const imported = await saveTable('JSON_Import_Test', 3);
        assert.equal(imported.Records[0].Values.title, 'JSON Żółć 😀'); assert.equal(imported.Records[0].Values.empty, ''); assert.equal(imported.Records[0].Values.missing, null); checks++;
        await button('External Data'); const jsonDownload = page.waitForEvent('download'); await button('JSON');
        const exported = JSON.parse(await readFile(await (await jsonDownload).path(), 'utf8')); assert.equal(exported[0].id, 1); assert.equal(exported[0].active, true); checks++;
        await button('SQLite');
        const choosingSqlite = page.waitForEvent('filechooser'); await button('Browse / Connect', true); await (await choosingSqlite).setFiles(sqlite);
        // Alphabetical SQLite table order begins with exact_values. Choose items through the native combo.
        await status(/records 1–1/);
        await selectIndex('Tables', 1);
        await status(/records 1–200/); checks++;
        await button('Next ▶', true); await status(/records 201–400/);
        await button('◀ Previous', true); await status(/3 page reads \/ 1 cache hits/); checks++;
        await edit('Local table name', 'SQLite_Import_Test'); await page.keyboard.press('Tab'); await page.screenshot({ path: screenshots + '/sqlite-source-preview.png', fullPage: true });
        await button('Import table', true); const sqlTable = await saveTable('SQLite_Import_Test', 403);
        assert.equal(sqlTable.Records[0].Values.amount, '12345678901234567890.123456789'); assert.equal(sqlTable.Records[0].Values.bytes, 'hex:0001FF'); checks++;
        await button('External Data'); const download = page.waitForEvent('download');
        // Import and export each have a SQLite command; export is the final one.
        await page.getByRole('button', { name: 'SQLite', exact: true }).last().press('Enter');
        const bytes = await readFile(await (await download).path()); assert.equal(bytes.subarray(0, 16).toString(), 'SQLite format 3\0'); checks++;
        const verified = await page.evaluate(async ({ baseURL, bytes }) => {
            const { SqliteWorkerClient } = await import(new URL('browser-sqlite.js', baseURL).href); const client = new SqliteWorkerClient();
            try { await client.open(new Uint8Array(bytes).buffer); return await client.call('page', { table: 'SQLite_Import_Test', offset: 400, limit: 200 }); }
            finally { client.close(); }
        }, { baseURL, bytes: [...bytes] });
        assert.equal(verified.rows.length, 3); checks++;
        // Exercise the .NET HTTP adapters through the actual Uno dialog, not
        // just a JavaScript fetch. The CI gateway exposes a disposable SQLite table.
        await button('External Data'); await button('Online Database');
        await edit('Endpoint URL', 'http://127.0.0.1:5099/');
        const token = peer('textbox', 'Gateway access token'); await focusNative(token, 'Gateway access token');
        await page.keyboard.insertText('DataSpace_browser_test_token_only_46'); await page.keyboard.press('Tab');
        await button('Browse / Connect', true); await status(/CI_SQLite.*records 1–200/); checks++;
        await edit('Local table name', 'Gateway_Import_Test'); await page.keyboard.press('Tab');
        await page.screenshot({ path: screenshots + '/online-source-preview.png', fullPage: true });
        await button('Import table', true); const remoteTable = await saveTable('Gateway_Import_Test', 403);
        assert.equal(remoteTable.Records[0].Values.title, 'External row 1 Żółć 😀'); checks++;
        await button('External Data'); await button('New Data Source');
        await selectIndex('Source type', 2);
        await edit('Endpoint URL', new URL('source-fixture.json', baseURL).href); await edit('JSON Pointer', '/data/items');
        await button('Browse / Connect', true); await status(/records 1–3/); checks++;
        await edit('Local table name', 'JSON_URL_Import_Test'); await button('Import table', true);
        assert.equal((await saveTable('JSON_URL_Import_Test', 3)).Records[0].Values.title, 'JSON Żółć 😀'); checks++;
        await page.reload({ waitUntil: 'domcontentloaded' }); await ready();
        const saved = await database(); assert.equal(saved.Tables.find(t => t.Name === 'SQLite_Import_Test').Records.length, 403); checks++;
        assert.ok(!JSON.stringify(saved).includes('DataSpace_browser_test_token_only_46')); checks++;
        await writeFile(screenshots + '/external-source-checks.json', JSON.stringify({ checks, sqliteRows: 403, jsonRows: 3, workerInt64Exact: true, sourceWrites: false }, null, 2));
        return checks;
    } catch (error) {
        await writeFile(screenshots + '/external-source-accessibility.txt', await page.locator('body').ariaSnapshot()); throw error;
    }
}
