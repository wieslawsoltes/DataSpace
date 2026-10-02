/** Wait for the real modal/result transition before issuing one native save. */
export async function saveReadyTable(page, readDatabase, name, count) {
    // Import is asynchronous. A fixed post-click delay can expire while HTTP or
    // native worker reads still own the modal and legitimately block Ctrl+S.
    await page.getByRole('button', { name: 'Import table', exact: true }).waitFor({ state: 'detached', timeout: 30000 });
    await page.getByRole('button', { name: 'Append records', exact: true }).waitFor({ state: 'detached', timeout: 30000 });
    await page.getByRole('button', { name: 'Close ' + name, exact: true }).waitFor({ state: 'attached', timeout: 10000 });
    const grid = page.locator('[xamlautomationid="Datasheet"]');
    await grid.waitFor({ state: 'attached', timeout: 10000 });
    // Never use DOM focus()/click() on the clipped semantic proxy. The modal
    // teardown normally restores native grid focus; keyboard traversal verifies
    // reachability when a different real control retains focus after undo/redo.
    let focused = await grid.evaluate(element => element.ownerDocument.activeElement === element);
    for (const direction of ['Shift+Tab', 'Tab']) {
        if (focused) break;
        for (let step = 0; step < 80; step++) {
            await page.keyboard.press(direction);
            await page.waitForTimeout(60);
            focused = await grid.evaluate(element => element.ownerDocument.activeElement === element);
            if (focused) break;
        }
    }
    if (!focused) throw new Error('The completed datasheet is not keyboard reachable: ' + name);
    await page.keyboard.press('Control+s');
    for (let attempt = 0; attempt < 100; attempt++) {
        const table = (await readDatabase()).Tables.find(table => table.Name === name);
        if (table?.Records.length === count) return table;
        await page.waitForTimeout(100);
    }
    throw new Error('The completed datasheet was not saved: ' + name);
}
