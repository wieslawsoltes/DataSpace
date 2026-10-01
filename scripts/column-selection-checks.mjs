import assert from 'node:assert/strict';

/** Real header/pointer/keyboard input at the fixed 1600×1000 CI workspace size. */
export async function columnSelectionChecks(page, screenshots, database, saved, ready) {
    let checks = 0;
    const name = 'Mapped_Import_Test';
    const table = db => db.Tables.find(item => item.Name === name);
    const button = async label => {
        await page.getByRole('button', { name: label, exact: true }).last().press('Enter');
        await page.waitForTimeout(200);
    };
    const command = async id => {
        await page.locator(`[xamlautomationid="Command_${id}"]`).press('Enter');
        await page.waitForTimeout(200);
    };
    const visible = t => [...new Set([...t.Datasheet.FrozenFields, ...t.Datasheet.ColumnOrder, ...t.Fields.map(f => f.Name)])]
        .filter(field => !t.Datasheet.HiddenFields.includes(field));
    function header(t, field) {
        const fields = visible(t); let x = 220 + 29; // Navigation pane and row selector; no test changes their width.
        for (const current of fields) {
            const width = t.Fields.find(f => f.Name === current).Width;
            if (current === field) return { x: x + width / 2, y: 219 };
            x += width;
        }
        throw new Error('Expected displayed field not found: ' + field);
    }
    async function click(t, field, options) { const { x, y } = header(t, field); await page.mouse.click(x, y, options); await page.waitForTimeout(100); }
    await button(name); await button('Table Fields');
    const original = table(await database());
    assert.deepEqual(visible(original), ['RowID', 'title', 'active', 'empty']);
    await click(original, 'title'); await page.keyboard.down('Shift');
    try { await click(original, 'active'); } finally { await page.keyboard.up('Shift'); }
    // Right-clicking inside the selected range must retain the entire selection.
    await click(original, 'title', { button: 'right' });
    await page.getByRole('menuitem', { name: 'Freeze Fields', exact: true }).press('Enter');
    const frozen = table(await saved(db => table(db).Datasheet.FrozenFields.includes('active')));
    assert.deepEqual(frozen.Datasheet.FrozenFields, ['RowID', 'title', 'active']);
    assert.deepEqual(frozen.Records, original.Records); assert.deepEqual(frozen.Fields, original.Fields); checks++;
    // The equivalent range can be selected with Ctrl+Space and Shift+Right.
    await click(frozen, 'title'); await page.keyboard.press('Control+Space'); await page.keyboard.press('Shift+ArrowRight');
    await command('hideFields');
    const hidden = table(await saved(db => table(db).Datasheet.HiddenFields.includes('active')));
    assert.deepEqual(hidden.Datasheet.HiddenFields, ['ExternalID', 'title', 'active']);
    assert.deepEqual(hidden.Records, original.Records); checks++;
    await page.screenshot({ path: screenshots + '/multi-column-layout.png', fullPage: true });
    // Hiding every remaining field fails atomically. It must not resurrect a random column.
    await click(hidden, 'RowID'); await page.keyboard.down('Shift');
    try { await click(hidden, 'empty'); } finally { await page.keyboard.up('Shift'); }
    await command('hideFields');
    assert.match(await page.locator('body').ariaSnapshot(), /Keep at least one field visible/);
    assert.deepEqual(table(await saved(db => table(db).Datasheet.HiddenFields.length === 3)).Datasheet, hidden.Datasheet); checks++;
    // Whole-column selection is not a request to delete all records.
    await click(hidden, 'RowID'); await page.keyboard.press('Delete'); await page.waitForTimeout(200);
    assert.match(await page.locator('body').ariaSnapshot(), /Select record rows, not whole columns/);
    assert.equal(await page.getByRole('button', { name: 'Continue', exact: true }).count(), 0);
    assert.deepEqual(table(await database()).Records, original.Records); checks++;
    await page.keyboard.press('Control+z');
    await saved(db => table(db).Datasheet.HiddenFields.length === 1);
    await page.keyboard.press('Control+y');
    await saved(db => table(db).Datasheet.HiddenFields.length === 3); checks++;
    await page.reload({ waitUntil: 'domcontentloaded' }); await ready();
    const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
    if (await enable.count()) await enable.press('Enter');
    await button(name); await button('Table Fields');
    assert.deepEqual(table(await database()).Datasheet, hidden.Datasheet); checks++;
    return checks;
}
