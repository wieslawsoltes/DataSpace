import { formTabOrderChecks } from './form-tab-order-checks.mjs';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

/** Exercise the ribbon commands, not similarly named standalone editor-toolbar buttons. */
export async function contextRibbonChecks(page, screenshots, database, saved) {
    let checks = 0;
    async function button(name) {
        await page.getByRole('button', { name, exact: true }).last().press('Enter');
        await page.waitForTimeout(200);
    }
    async function command(id) {
        // Uno attaches this stable AutomationId to the native semantic peer.
        // Dynamic peer order need not match visual order; never select by first/last.
        await page.locator(`[xamlautomationid="Command_${id}"]`).press('Enter');
        await page.waitForTimeout(200);
    }
    async function state(pattern) {
        for (let attempt = 0; attempt < 100; attempt++) {
            if (pattern.test(await page.locator('body').ariaSnapshot())) return;
            await page.waitForTimeout(100);
        }
        throw new Error('Contextual ribbon did not produce state: ' + pattern);
    }
    await button('Customers in the UK'); await button('Query Design');
    await command('querySqlView');
    await page.getByRole('textbox', { name: 'SQL statement', exact: true }).waitFor({ state: 'attached' });
    await command('queryDesignView');
    await page.getByRole('textbox', { name: 'Criteria column 1', exact: true }).waitFor({ state: 'attached' });
    const expression = page.getByRole('textbox', { name: 'Field expression column 2', exact: true });
    assert.match((await expression.inputValue()).replace(/\r\n?/g, '\n'), /\n\)$/);
    await command('queryRun');
    const download = page.waitForEvent('download'); await command('exportCsv');
    const csv = (await readFile(await (await download).path(), 'utf8')).trim().split(/\r?\n/);
    assert.deepEqual(csv.slice(1), ['1,3', '2,3']);
    await page.screenshot({ path: screenshots + '/query-context-ribbon.png', fullPage: true }); checks++;

    const formName = 'Customer Details';
    const form = (await database()).Forms.find(form => form.Name === formName);
    await button(formName); await button('Form'); await command('designView'); await button('Form Design');
    await command('formLabel');
    const after = await saved(db => db.Forms.find(form => form.Name === formName).Controls.length === form.Controls.length + 1);
    const added = after.Forms.find(form => form.Name === formName).Controls.at(-1);
    assert.equal(added.Kind, 'Label'); checks++;
    await command('propertySheet'); await command('propertySheet');
    await page.screenshot({ path: screenshots + '/form-context-ribbon.png', fullPage: true });
    await command('formDelete');
    await saved(db => db.Forms.find(form => form.Name === formName).Controls.length === form.Controls.length); checks++;
    checks += await formTabOrderChecks(page, screenshots, database, saved, command);
    await command('datasheetView');
    await page.getByRole('button', { name: 'Form', exact: true }).waitFor({ state: 'attached' }); checks++;

    // A real 403-record imported table exercises multiple preview pages.
    await button('SQLite_Import_Test');
    const beforeReports = (await database()).Reports.map(report => report.Name);
    await button('Create'); await button('Report');
    const reportDocument = await saved(db => db.Reports.length === beforeReports.length + 1);
    const report = reportDocument.Reports.find(report => !beforeReports.includes(report.Name));
    assert.equal(report.Source, 'SQLite_Import_Test');
    await button('Print Preview'); await state(/Page 1 of \d+ · 403 records/);
    await command('reportNext'); await state(/Page 2 of \d+ · 403 records/);
    await command('reportPrevious'); await state(/Page 1 of \d+ · 403 records/); checks++;
    await command('reportPortrait'); await saved(db => db.Reports.find(item => item.Name === report.Name).Landscape === false);
    await command('reportLandscape'); await saved(db => db.Reports.find(item => item.Name === report.Name).Landscape === true); checks++;
    const zoom = page.getByRole('slider', { name: 'Report zoom', exact: true });
    const original = Number(await zoom.getAttribute('aria-valuenow'));
    assert.equal(original, 100);
    await command('reportZoomIn'); assert.equal(Number(await zoom.getAttribute('aria-valuenow')), 110);
    await command('reportZoomOut'); assert.equal(Number(await zoom.getAttribute('aria-valuenow')), original); checks++;
    await page.screenshot({ path: screenshots + '/print-preview-ribbon.png', fullPage: true });
    return checks;
}
