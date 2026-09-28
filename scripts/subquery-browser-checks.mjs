import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';

/** Exercises actual native Uno builders and SQL results; no app command/test backdoor. */
export async function subqueryBrowserChecks(page, baseURL, screenshots, ready) {
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
    async function toggle(name, expected) {
        const input = peer('checkbox', name);
        for (let i = 0; i < 100; i++) {
            if (await input.evaluate(element => document.activeElement === element)) break;
            await page.keyboard.press('Tab'); await page.waitForTimeout(50);
        }
        assert.ok(await input.evaluate(element => document.activeElement === element), name + ' must be keyboard reachable.');
        await page.keyboard.press('Space');
        for (let i = 0; i < 50; i++) { if (await input.isChecked() === expected) return; await page.waitForTimeout(100); }
        assert.equal(await input.isChecked(), expected);
    }
    async function database() {
        return page.evaluate(async baseURL => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href);
            const value = await readWorkspace(); return JSON.parse(value.slice(value.indexOf('\n') + 1));
        }, baseURL);
    }
    async function save(fragment) {
        await page.keyboard.press('Control+s');
        for (let i = 0; i < 80; i++) {
            if ((await database()).Queries.find(q => q.Name === 'Customers in the UK').Sql.includes(fragment)) return;
            await page.waitForTimeout(100);
        }
        throw new Error('Find query did not persist.');
    }
    async function csv() {
        await button('External Data'); const downloading = page.waitForEvent('download'); await button('Text File', true);
        return (await readFile(await (await downloading).path(), 'utf8')).trim().split(/\r?\n/);
    }
    try {
        await button('Customers in the UK'); await button('SQL View');
        const previous = await peer('textbox', 'SQL statement').inputValue();
        await button('Find Duplicates'); await button('Cancel');
        assert.equal(await peer('textbox', 'SQL statement').inputValue(), previous); checks++;
        await button('Find Duplicates'); await button('Generate SQL');
        assert.ok(await peer('checkbox', 'Match field Country').count(), 'An empty selection must retain the builder for correction.'); checks++;
        await toggle('Match field Country', true);
        await page.screenshot({ path: screenshots + '/find-duplicates-builder.png', fullPage: true });
        await button('Generate SQL'); assert.ok((await peer('textbox', 'SQL statement').inputValue()).includes('HAVING COUNT(*) > 1')); checks++;
        await button('Run'); const summary = await csv(); assert.equal(summary.length, 5);
        assert.ok(summary.some(row => /^"?UK"?,4$/.test(row))); checks++;
        await page.screenshot({ path: screenshots + '/find-duplicates-results.png', fullPage: true });
        await button('Find Duplicates'); await toggle('Match field Country', true);
        await toggle('Show duplicate groups and counts', false); await toggle('Output field ID', true);
        await button('Generate SQL'); await button('Run'); const detail = await csv(); assert.equal(detail.length, 12); checks++;
        await button('Find Unmatched'); await toggle('Match field ID', true); await toggle('Output field ID', true);
        // Defaults: Customers against Products, whose IDs are 1..12.
        await page.screenshot({ path: screenshots + '/find-unmatched-builder.png', fullPage: true });
        await button('Generate SQL'); assert.ok((await peer('textbox', 'SQL statement').inputValue()).includes('NOT IN (SELECT')); checks++;
        await button('Run'); const unmatched = await csv();
        assert.deepEqual(unmatched.slice(1), ['13', '14', '15', '16', '17', '18']); checks++;
        await save('NOT IN (SELECT'); await page.reload({ waitUntil: 'domcontentloaded' }); await ready();
        const enable = peer('button', 'Enable accessibility'); if (await enable.count()) await enable.press('Enter');
        await button('Customers in the UK'); assert.deepEqual(await csv(), unmatched); checks++;
        await button('SQL View');
        const sql = 'SELECT c.ID,(SELECT Count(*) FROM Orders o WHERE o.[Customer ID]=c.ID) AS OrderCount FROM Customers c WHERE c.ID<=2 ORDER BY c.ID;';
        await edit('SQL statement', sql); await button('Run'); assert.deepEqual((await csv()).slice(1), ['1,3', '2,3']); checks++;
        await page.screenshot({ path: screenshots + '/correlated-subquery-results.png', fullPage: true });
        await button('SQL View'); const before = JSON.stringify((await database()).Tables);
        await edit('SQL statement', 'UPDATE BrowserArchive SET Company=(SELECT Company FROM Customers);');
        await button('Run'); await button('Continue'); await save('UPDATE BrowserArchive');
        assert.equal(JSON.stringify((await database()).Tables), before, 'A multirow scalar action must roll back all data edits.'); checks++;
        await edit('SQL statement', sql); await button('Run'); await save('OrderCount');
        return checks;
    } finally { await writeFile(screenshots + '/subquery-accessibility.txt', await page.locator('body').ariaSnapshot()); }
}
