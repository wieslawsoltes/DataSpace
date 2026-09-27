import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';

/** Real keyboard and input events through Uno accessibility peers, plus physical source-card dragging. */
export async function queryBrowserChecks(page, baseURL, screenshots, ready) {
    let checks = 0;
    async function enableAccessibility() {
        const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await enable.count()) await enable.press('Enter');
    }
    async function target(role, name, last = false) {
        const matches = page.getByRole(role, { name, exact: true });
        const match = last ? matches.last() : matches.first();
        await match.waitFor({ state: 'attached', timeout: 20000 });
        return match;
    }
    async function button(name, last = false) {
        const match = await target('button', name, last);
        await match.press('Enter'); await page.waitForTimeout(250);
    }
    async function edit(name, value) {
        // The accessible text input itself routes input events to the Uno TextBox.
        // Do not reinterpret peer-local rectangles as browser viewport coordinates.
        const input = await target('textbox', name);
        await input.fill(value); await input.press('Tab');
    }
    async function database() {
        return page.evaluate(async baseURL => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href);
            const envelope = await readWorkspace(); return JSON.parse(envelope.slice(envelope.indexOf('\n') + 1));
        }, baseURL);
    }
    async function saveQuery(name, fragment) {
        await page.keyboard.press('Control+s');
        await page.waitForFunction(async ({ baseURL, name, fragment }) => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href);
            const envelope = await readWorkspace(); if (!envelope) return false;
            const document = JSON.parse(envelope.slice(envelope.indexOf('\n') + 1));
            return document.Queries.find(query => query.Name === name)?.Sql.includes(fragment);
        }, { baseURL, name, fragment }, { timeout: 20000 });
    }
    async function exportRows() {
        await button('External Data');
        const downloading = page.waitForEvent('download', { timeout: 20000 });
        await button('Text File', true);
        const download = await downloading;
        return (await readFile(await download.path(), 'utf8')).trim().split(/\r?\n/);
    }
    const queryName = 'Customers in the UK';
    try {
        await enableAccessibility(); await button(queryName);
        assert.equal((await exportRows()).length, 5); checks++;
        await button('Design View'); await target('textbox', 'Criteria column 1'); checks++;
        await edit('Criteria column 1', "Like 'B*'");
        await button('Run'); const first = await exportRows();
        assert.equal(first.length, 2); assert.ok(first[1].includes('Blue Yonder Airlines')); checks++;
        await button('Design View'); await edit('Or 1 column 1', "Like 'T*'");
        // The fixed 1600x1000 fixture has the first source-card header at (330,330).
        await page.mouse.move(330, 330); await page.mouse.down();
        await page.mouse.move(450, 370, { steps: 8 }); await page.mouse.up();
        await page.screenshot({ path: screenshots + '/query-design.png', fullPage: true });
        await button('Run'); const alternatives = await exportRows();
        assert.equal(alternatives.length, 4); assert.ok(alternatives.some(row => row.includes('Trey Research'))); checks++;
        await saveQuery(queryName, ' OR ');
        const stored = (await database()).Queries.find(query => query.Name === queryName);
        const design = JSON.parse(stored.DesignerState);
        assert.deepEqual(design.Columns[0].Criteria.slice(0, 2), ["Like 'B*'", "Like 'T*'"]);
        assert.ok(design.Sources[0].X > 24); checks++;
        await page.reload({ waitUntil: 'domcontentloaded' }); await ready(); await enableAccessibility();
        await button(queryName); assert.equal((await exportRows()).length, 4); checks++;
        await button('Design View'); await button('SQL View');
        await edit('SQL statement', 'DELETE FROM Customers;'); await button('Design View');
        await target('textbox', 'SQL statement'); await button('Run'); await button('Cancel');
        await edit('SQL statement', stored.Sql); await saveQuery(queryName, ' OR ');
        assert.equal((await database()).Tables.find(table => table.Name === 'Customers').Records.length, 18); checks++;
        await button('Open Orders'); assert.equal((await exportRows()).length, 17); checks++;
        await button('Design View'); await target('button', 'c.*'); await target('button', 'o.*');
        await page.screenshot({ path: screenshots + '/query-joins.png', fullPage: true }); checks++;
        await button('Sales by Customer'); assert.equal((await exportRows()).length, 19);
        await button('Design View'); await target('combobox', 'Total column 2');
        await page.screenshot({ path: screenshots + '/query-totals.png', fullPage: true }); checks++;
        return checks;
    } finally {
        await writeFile(screenshots + '/query-accessibility.txt', await page.locator('body').ariaSnapshot());
        await writeFile(screenshots + '/query-peer-bounds.json', JSON.stringify(await page.getByRole('textbox').evaluateAll(elements => elements.map(element => ({html: element.outerHTML, rect: element.getBoundingClientRect().toJSON()}))), null, 2));
    }
}
