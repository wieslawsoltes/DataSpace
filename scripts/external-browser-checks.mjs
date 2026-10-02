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
                const unreserved = await client.call('export', { name: 'sqliteX', columns: ['Value'], rows: [['kept']] });
                const visible = await client.open(unreserved);
                return { tables, exact, first, last, denied, copy, unreservedName: visible[0].name };
            } finally { client.close(); }
        }, { baseURL, fixture });
        assert.equal(result.tables.length, 2); checks++;
        assert.equal(result.exact.rows[0][0], '9223372036854775807'); checks++;
        assert.equal(result.first.rows.length, 200); assert.equal(result.first.hasMore, true); assert.equal(result.first.rows[0][4], 'hex:0001FF'); checks++;
        assert.equal(result.last.rows.length, 3); assert.equal(result.last.hasMore, false); checks++;
        assert.equal(result.denied, true); checks++;
        assert.deepEqual(result.copy.rows, [['9223372036854775807', 'Unicode 😀'], ['2', null]]); checks++;
        assert.equal(result.unreservedName, 'sqliteX'); checks++;
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
    async function selectIndex(name, index, currentIndex = 0, verifyHeldEnter = false) {
        const combo = peer('combobox', name); await focusNative(combo, name);
        // Alt+Down explicitly opens a native selector even during type-ahead.
        // Space can be consumed as search text; a second Enter must not activate
        // the containing dialog's default action after a failed open.
        await page.keyboard.press('Alt+ArrowDown'); await page.waitForTimeout(200);
        assert.equal(await combo.getAttribute('aria-expanded'), 'true', name + ' popup must actually open.');
        // Home does not move the focused native popup item on every Uno backend.
        // Navigate from the known selected item rather than assuming it reset to zero.
        const delta = index - currentIndex;
        for (let step = 0; step < Math.abs(delta); step++) { await page.keyboard.press(delta < 0 ? 'ArrowUp' : 'ArrowDown'); await page.waitForTimeout(80); }
        if (verifyHeldEnter) {
            await page.keyboard.down('Enter');
            try {
                await page.waitForTimeout(120);
                assert.equal(await peer('button', 'Cancel operation').isEnabled(), false);
                await status(/records 1–1.*1 page reads \/ 0 cache hits/);
                checks++;
            } finally { await page.keyboard.up('Enter'); }
        } else await page.keyboard.press('Enter');
        await page.waitForTimeout(200);
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
        // Dismissing a chooser without accepting a new value must neither run
        // another page request nor leave keyboard focus outside the source modal.
        await focusNative(peer('combobox', 'Tables'), 'Tables');
        await page.keyboard.press('Space'); await page.waitForTimeout(200);
        assert.equal(await peer('combobox', 'Tables').getAttribute('aria-expanded'), 'true');
        await page.keyboard.press('Escape'); await page.waitForTimeout(200);
        assert.equal(await peer('combobox', 'Tables').getAttribute('aria-expanded'), 'false');
        await status(/records 1–1.*1 page reads \/ 0 cache hits/); checks++;
        await selectIndex('Tables', 1, 0, true);
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
            try {
                const tables = await client.open(new Uint8Array(bytes).buffer);
                return { tables, page: await client.call('page', { table: tables[0].id, offset: 400, limit: 200 }) };
            } finally { client.close(); }
        }, { baseURL, bytes: [...bytes] });
        assert.equal(verified.tables[0].name, 'DataSpace_SQLite_Import_Test');
        assert.equal(verified.page.rows.length, 3); checks++;
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
        // Reuse the source dialog's field-properties editor through native keyboard input.
        await button('External Data'); await button('JSON File');
        await edit('JSON Pointer', '/data/items');
        const choosingMapped = page.waitForEvent('filechooser'); await button('Browse / Connect', true); await (await choosingMapped).setFiles(json);
        await status(/records 1–3/); await edit('Local table name', 'Mapped_Import_Test');
        await button('Field Options', true);
        assert.equal(await page.getByRole('textbox', { name: 'Destination field name', exact: true }).count(), 1); checks++;
        await edit('Destination field name', 'ExternalID');
        await selectIndex('Import data type', 0, 2); // Explicitly preserve IDs as Short Text.
        await edit('Short Text maximum length', '20');
        const generated = peer('checkbox', 'Add AutoNumber primary key'); await focusNative(generated, 'Add AutoNumber primary key'); await page.keyboard.press('Space');
        await edit('Generated key field name', 'RowID');
        await selectIndex('Source field', 4); // Drop the fixture's all-null "missing" column.
        const skip = peer('checkbox', 'Do not import field (Skip)'); await focusNative(skip, 'Skip field'); await page.keyboard.press('Space');
        await button('Data Preview', true); await button('Field Options', true);
        await page.screenshot({ path: screenshots + '/import-field-options.png', fullPage: true });
        await button('Import table', true); const mapped = await saveTable('Mapped_Import_Test', 3);
        await page.getByRole('textbox', { name: 'Destination field name', exact: true }).waitFor({ state: 'detached', timeout: 5000 }); checks++;
        assert.deepEqual(mapped.Fields.map(field => field.Name), ['RowID', 'ExternalID', 'title', 'active', 'empty']); checks++;
        assert.equal(mapped.Fields[0].Type, 'AutoNumber'); assert.equal(mapped.Fields[0].PrimaryKey, true); assert.equal(mapped.NextAutoNumber, 4); checks++;
        assert.equal(mapped.Fields[1].Type, 'ShortText'); assert.equal(mapped.Records[2].Values.ExternalID, '3'); assert.equal(mapped.Records[2].Values.RowID, '3'); checks++;
        // Append the current imported datasheet to itself: omit the AutoNumber key by default.
        await button('External Data'); await button('Append Records');
        // Native ContentDialog initially focuses its first input. A ComboBox
        // consumes Enter to open its list even when DefaultButton is Close.
        // Reach the safe footer through real Tab traversal before testing Enter.
        await focusNative(peer('button', 'Cancel'), 'Cancel');
        await page.keyboard.press('Enter');
        await peer('combobox', 'Destination table').waitFor({ state: 'detached', timeout: 5000 });
        assert.equal((await saveTable('Mapped_Import_Test', 3)).Records.length, 3); checks++;
        await button('Append Records');
        await page.screenshot({ path: screenshots + '/append-records.png', fullPage: true });
        await button('Append records', true);
        const appended = await saveTable('Mapped_Import_Test', 6);
        assert.deepEqual(appended.Records.map(row => row.Values.RowID), ['1', '2', '3', '4', '5', '6']);
        assert.equal(appended.NextAutoNumber, 7); assert.equal(appended.Records[5].Values.ExternalID, '3'); checks++;
        await page.keyboard.press('Control+z'); await saveTable('Mapped_Import_Test', 3); checks++;
        await page.keyboard.press('Control+y'); await saveTable('Mapped_Import_Test', 6); checks++;
        await button('External Data'); await button('Append Records');
        await selectIndex('Append to field', 1); // Explicitly map RowID, which must reject duplicate keys.
        await button('Append records', true); await status(/Duplicate value in unique index/);
        await button('Cancel', true); await saveTable('Mapped_Import_Test', 6); checks++;
        await page.reload({ waitUntil: 'domcontentloaded' }); await ready();
        const saved = await database(); assert.equal(saved.Tables.find(t => t.Name === 'SQLite_Import_Test').Records.length, 403); checks++;
        assert.ok(!JSON.stringify(saved).includes('DataSpace_browser_test_token_only_46')); checks++;
        assert.equal(saved.Tables.find(t => t.Name === 'Mapped_Import_Test').Fields.length, 5); checks++;
        await writeFile(screenshots + '/external-source-checks.json', JSON.stringify({ checks, sqliteRows: 403, jsonRows: 3, workerInt64Exact: true, sourceWrites: false }, null, 2));
        return checks;
    } catch (error) {
        console.error('External native focus:', await page.evaluate(() => ({ role: document.activeElement?.getAttribute('role'), name: document.activeElement?.getAttribute('aria-label'), expanded: document.activeElement?.getAttribute('aria-expanded') })));
        await writeFile(screenshots + '/external-source-accessibility.txt', await page.locator('body').ariaSnapshot()); throw error;
    }
}
