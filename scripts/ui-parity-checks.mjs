import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { activateDialogButton } from './native-dialog-actions.mjs';

/** Native Uno interactions; model reads are assertions only, never application-command backdoors. */
export async function uiParityChecks(page, baseURL, screenshots, ready) {
    let checks = 0;
    const peer = (role, name) => page.getByRole(role, { name, exact: true }).last();
    async function enableAccessibility() {
        const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await enable.count()) await enable.press('Enter');
    }
    async function button(name, dialog = false) {
        const target = peer('button', name);
        if (dialog) await activateDialogButton(page, target, name); else await target.press('Enter');
        await page.waitForTimeout(200);
    }
    async function focus(target) {
        await target.waitFor({ state: 'attached' });
        for (const key of ['Tab', 'Shift+Tab']) for (let i = 0; i < 55; i++) {
            if (await target.evaluate(e => e.ownerDocument.activeElement === e)) return;
            await page.keyboard.press(key); await page.waitForTimeout(70);
        }
        throw new Error('Native UI element was not keyboard reachable.');
    }
    async function edit(name, text) {
        const input = peer('textbox', name); await focus(input);
        await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace'); await page.keyboard.insertText(text); await page.keyboard.press('Tab');
        await page.waitForTimeout(150); assert.equal(await input.inputValue(), text);
    }
    async function toggle(name) { const box = peer('checkbox', name); await focus(box); await page.keyboard.press('Space'); await page.waitForTimeout(150); }
    async function select(name, delta) {
        const combo = peer('combobox', name); await focus(combo); await page.keyboard.press('Space'); await page.waitForTimeout(150);
        for (let i = 0; i < Math.abs(delta); i++) { await page.keyboard.press(delta < 0 ? 'ArrowUp' : 'ArrowDown'); await page.waitForTimeout(70); }
        await page.keyboard.press('Enter'); await page.waitForTimeout(200);
    }
    async function database() {
        return page.evaluate(async baseURL => {
            const { readWorkspace } = await import(new URL('browser-storage.js', baseURL).href);
            const data = await readWorkspace(); return JSON.parse(data.slice(data.indexOf('\n') + 1));
        }, baseURL);
    }
    async function saved(predicate) {
        await page.keyboard.press('Control+s');
        for (let i = 0; i < 100; i++) { const db = await database(); if (predicate(db)) return db; await page.waitForTimeout(100); }
        throw new Error('Saved UI operation did not reach the expected state.');
    }
    try {
        await enableAccessibility();
        // Use the small mapped fixture created through the existing real import workflow.
        await button('Mapped_Import_Test'); await button('Table Fields');
        await page.screenshot({ path: screenshots + '/table-context-ribbon.png', fullPage: true }); checks++;
        await button('Unhide Fields');
        await edit('Row height', '36'); await edit('Datasheet font size', '15');
        await toggle('Bold text');
        await toggle('Keep field visible while scrolling'); // RowID selected initially.
        await select('Datasheet field', 1); // ExternalID
        await toggle('Show field');
        await button('Apply Layout', true);
        const layout = (await saved(db => db.Tables.find(t => t.Name === 'Mapped_Import_Test').Datasheet.RowHeight === 36)).Tables.find(t => t.Name === 'Mapped_Import_Test').Datasheet;
        assert.deepEqual(layout.HiddenFields, ['ExternalID']); assert.deepEqual(layout.FrozenFields, ['RowID']); assert.equal(layout.Bold, true); checks++;
        await button('Unhide Fields'); await edit('Row height', '0'); await button('Apply Layout', true);
        assert.match(await page.locator('body').ariaSnapshot(), /Row height must be/); await button('Cancel', true); checks++;
        await page.keyboard.press('Control+z'); await saved(db => db.Tables.find(t => t.Name === 'Mapped_Import_Test').Datasheet.RowHeight === 27); checks++;
        await page.keyboard.press('Control+y'); await saved(db => db.Tables.find(t => t.Name === 'Mapped_Import_Test').Datasheet.RowHeight === 36); checks++;
        await page.reload({ waitUntil: 'domcontentloaded' }); await ready(); await enableAccessibility();
        await button('Mapped_Import_Test'); await button('Table Fields');
        await page.screenshot({ path: screenshots + '/saved-datasheet-layout.png', fullPage: true });
        assert.equal((await database()).Tables.find(t => t.Name === 'Mapped_Import_Test').Datasheet.HiddenFields[0], 'ExternalID'); checks++;
        // A raw literal replace across displayed fields; explicit two-step Replace All.
        await button('Replace'); await edit('Find what', 'JSON'); await edit('Replace with', 'Access UI');
        await select('Look in', -1); // selected RowID -> All displayed fields
        await button('Find Next', true);
        assert.match(await page.locator('body').ariaSnapshot(), /Found title/); checks++;
        await button('Replace All', true);
        assert.match(await page.locator('body').ariaSnapshot(), /Confirm Replace All/);
        await button('Confirm Replace All', true);
        assert.match(await page.locator('body').ariaSnapshot(), /Replaced 2 cell/);
        await page.screenshot({ path: screenshots + '/find-replace.png', fullPage: true });
        await button('Close', true);
        await saved(db => db.Tables.find(t => t.Name === 'Mapped_Import_Test').Records[0].Values.title === 'Access UI Żółć 😀'); checks++;
        await page.keyboard.press('Control+z'); await saved(db => db.Tables.find(t => t.Name === 'Mapped_Import_Test').Records[0].Values.title === 'JSON Żółć 😀'); checks++;
        // Native context switching and property-sheet commands.
        await button('Table Fields'); await button('Design View'); await button('Table Design');
        assert.ok(await peer('button', 'Primary Key').count());
        await button('Property Sheet'); await button('Property Sheet');
        await page.screenshot({ path: screenshots + '/table-design-ribbon.png', fullPage: true });
        await button('Datasheet View'); checks++;
        await button('Help'); await button('Navigation Pane'); await button('Navigation Pane'); checks++;
        const objectButtons = await page.getByRole('button', { name: 'Mapped_Import_Test', exact: true }).count();
        await button('All Access Objects ▾'); await peer('menuitem', 'Queries').press('Enter'); await page.waitForTimeout(200);
        assert.equal(await page.getByRole('button', { name: 'Mapped_Import_Test', exact: true }).count(), objectButtons - 1); checks++;
        await button('Queries ▾'); await peer('menuitem', 'All Access Objects').press('Enter'); await page.waitForTimeout(200);
        await button('JSON_Import_Test'); await button('Mapped_Import_Test');
        await page.keyboard.press('Control+Tab'); await page.keyboard.press('Control+Shift+Tab'); checks++;
        await button('Help'); await button('Close All');
        assert.match(await page.locator('body').ariaSnapshot(), /Build your database/); checks++;
        await button('Mapped_Import_Test');
        await writeFile(screenshots + '/ui-parity-checks.json', JSON.stringify({ checks, persistedLayout: true, atomicReplacement: true, placeholders: false }, null, 2));
        return checks;
    } catch (error) {
        await writeFile(screenshots + '/ui-parity-accessibility.txt', await page.locator('body').ariaSnapshot()); throw error;
    }
}
