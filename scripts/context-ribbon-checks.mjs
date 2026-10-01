import assert from 'node:assert/strict';

/** Exercise the ribbon commands, not similarly named standalone editor-toolbar buttons. */
export async function contextRibbonChecks(page, screenshots, database, saved) {
    let checks = 0;
    async function button(name, first = false) {
        const matches = page.getByRole('button', { name, exact: true });
        await (first ? matches.first() : matches.last()).press('Enter');
        await page.waitForTimeout(200);
    }
    async function state(pattern) {
        for (let attempt = 0; attempt < 100; attempt++) {
            if (pattern.test(await page.locator('body').ariaSnapshot())) return;
            await page.waitForTimeout(100);
        }
        throw new Error('Contextual ribbon did not produce state: ' + pattern);
    }
    // The ribbon precedes the editor in the accessibility tree. The first matching
    // command deliberately selects the ribbon when both regions expose a command.
    await button('Customers in the UK'); await button('Query Design');
    await button('SQL View', true);
    await page.getByRole('textbox', { name: 'SQL statement', exact: true }).waitFor({ state: 'attached' });
    await button('Design View', true);
    await page.getByRole('textbox', { name: 'Criteria column 1', exact: true }).waitFor({ state: 'attached' });
    await button('Run', true);
    await page.screenshot({ path: screenshots + '/query-context-ribbon.png', fullPage: true }); checks++;

    const formName = 'Customer Details';
    const form = (await database()).Forms.find(form => form.Name === formName);
    await button(formName); await button('Form'); await button('Design View', true); await button('Form Design');
    await button('Label', true);
    const after = await saved(db => db.Forms.find(form => form.Name === formName).Controls.length === form.Controls.length + 1);
    const added = after.Forms.find(form => form.Name === formName).Controls.at(-1);
    assert.equal(added.Kind, 'Label'); checks++;
    await button('Property Sheet', true); await button('Property Sheet', true);
    await page.screenshot({ path: screenshots + '/form-context-ribbon.png', fullPage: true });
    await button('Delete Control', true);
    await saved(db => db.Forms.find(form => form.Name === formName).Controls.length === form.Controls.length); checks++;
    await button('Form View', true);
    await page.getByRole('button', { name: 'Form', exact: true }).waitFor({ state: 'attached' }); checks++;

    // A real 403-record imported table exercises multiple preview pages.
    await button('SQLite_Import_Test');
    const beforeReports = (await database()).Reports.map(report => report.Name);
    await button('Create'); await button('Report');
    const reportDocument = await saved(db => db.Reports.length === beforeReports.length + 1);
    const report = reportDocument.Reports.find(report => !beforeReports.includes(report.Name));
    assert.equal(report.Source, 'SQLite_Import_Test');
    await button('Print Preview'); await state(/Page 1 of \d+ · 403 records/);
    await button('Next Page', true); await state(/Page 2 of \d+ · 403 records/);
    await button('Previous Page', true); await state(/Page 1 of \d+ · 403 records/); checks++;
    await button('Portrait', true); await saved(db => db.Reports.find(item => item.Name === report.Name).Landscape === false);
    await button('Landscape', true); await saved(db => db.Reports.find(item => item.Name === report.Name).Landscape === true); checks++;
    const zoom = page.getByRole('slider', { name: 'Report zoom', exact: true });
    const original = Number(await zoom.getAttribute('aria-valuenow'));
    assert.equal(original, 100);
    await button('Zoom In', true); assert.equal(Number(await zoom.getAttribute('aria-valuenow')), 110);
    await button('Zoom Out', true); assert.equal(Number(await zoom.getAttribute('aria-valuenow')), original); checks++;
    await page.screenshot({ path: screenshots + '/print-preview-ribbon.png', fullPage: true });
    return checks;
}
