import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';

/** Runs after the existing 24 checks, in the same isolated demonstration database. */
export async function crosstabBrowserChecks(page, baseURL, screenshots, ready) {
    let checks = 0;
    const peer = (role, name) => page.getByRole(role, { name, exact: true }).first();
    async function button(name, last = false) {
        const matches = page.getByRole('button', { name, exact: true });
        await (last ? matches.last() : matches.first()).press('Enter'); await page.waitForTimeout(250);
    }
    async function edit(name, value) {
        const input = peer('textbox', name); await input.focus(); await page.waitForTimeout(150);
        await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace'); await page.keyboard.insertText(value);
        await page.keyboard.press('Tab'); await page.waitForTimeout(150);
        assert.equal((await input.inputValue()).replace(/\r\n?/g, '\n'), value.replace(/\r\n?/g, '\n'));
    }
    async function database() {
        return page.evaluate(async baseURL => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href);
            const text = await readWorkspace(); return JSON.parse(text.slice(text.indexOf('\n') + 1));
        }, baseURL);
    }
    async function saveUntil(predicate) {
        await page.keyboard.press('Control+s');
        for (let i = 0; i < 80; i++) { if (predicate(await database())) return; await page.waitForTimeout(100); }
        throw new Error('The crosstab workflow did not persist its current state.');
    }
    async function csv() {
        await button('External Data'); const downloading = page.waitForEvent('download'); await button('Text File', true);
        return (await readFile(await (await downloading).path(), 'utf8')).trim().split(/\r?\n/);
    }
    const queryName = 'Customers in the UK';
    const previous = (await database()).Queries.find(q => q.Name === queryName).Sql;
    try {
        await button(queryName); await button('SQL View'); await button('Crosstab Builder');
        await button('Cancel'); assert.equal((await peer('textbox', 'SQL statement').inputValue()).replace(/\r\n?/g, '\n'), previous.replace(/\r\n?/g, '\n')); checks++;
        await button('Crosstab Builder');
        // The builder starts with Customers. Select Country as the row axis and City as columns.
        await peer('checkbox', 'Row heading Country').press('Space');
        await edit('Column heading expression', 'City'); await edit('Value expression', '*');
        await edit('Fixed column headings', "'London', 'Warsaw', 'No matching city'");
        await peer('checkbox', 'Include row totals').press('Space');
        await page.screenshot({ path: screenshots + '/crosstab-builder.png', fullPage: true });
        await button('Generate SQL');
        assert.ok((await peer('textbox', 'SQL statement').inputValue()).includes('TRANSFORM COUNT(*)')); checks++;
        await button('Run'); const rows = await csv();
        const columns = rows[0].split(',').map(v => v.replace(/^"|"$/g, ''));
        assert.deepEqual(columns, ['Country', 'Row Total', 'London', 'Warsaw', 'No matching city']);
        const uk = rows.find(row => /^"?UK"?,/.test(row)); assert.ok(uk); assert.ok(uk.includes(',4,1,')); checks++;
        await page.screenshot({ path: screenshots + '/crosstab-results.png', fullPage: true });
        await saveUntil(doc => doc.Queries.find(q => q.Name === queryName).Sql.startsWith('TRANSFORM'));
        const crosstab = (await database()).Queries.find(q => q.Name === queryName).Sql;
        await page.reload({ waitUntil: 'domcontentloaded' }); await ready();
        const enable = peer('button', 'Enable accessibility'); if (await enable.count()) await enable.press('Enter');
        await button(queryName); assert.deepEqual(await csv(), rows); checks++;
        await button('Crosstab Builder'); assert.equal(await peer('checkbox', 'Row heading Country').isChecked(), true);
        assert.equal(await peer('checkbox', 'Include row totals').isChecked(), true); await button('Cancel'); checks++;
        // Make-table action cancellation, execution, undo/redo and persisted result.
        await button('SQL View'); await edit('SQL statement', 'SELECT TOP 3 ID, Company INTO BrowserArchive FROM Customers ORDER BY ID;');
        await button('Run'); await button('Cancel'); await saveUntil(doc => !doc.Tables.some(t => t.Name === 'BrowserArchive'));
        assert.equal((await database()).Tables.some(t => t.Name === 'BrowserArchive'), false); checks++;
        await button('Run'); await button('Continue'); await saveUntil(doc => doc.Tables.some(t => t.Name === 'BrowserArchive'));
        assert.equal((await database()).Tables.find(t => t.Name === 'BrowserArchive').Records.length, 3); checks++;
        await button('Home'); await button('Undo'); await saveUntil(doc => !doc.Tables.some(t => t.Name === 'BrowserArchive')); checks++;
        await button('Redo'); await saveUntil(doc => doc.Tables.some(t => t.Name === 'BrowserArchive')); checks++;
        await button('SQL View'); await edit('SQL statement', crosstab); await button('Run'); await saveUntil(doc => doc.Queries.find(q => q.Name === queryName).Sql.startsWith('TRANSFORM'));
        return checks;
    } finally { await writeFile(screenshots + '/crosstab-accessibility.txt', await page.locator('body').ariaSnapshot()); }
}
