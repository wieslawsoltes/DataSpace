import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { activateDialogButton } from './native-dialog-actions.mjs';

/** Uses native keyboard traversal and the actual bound record inputs. */
export async function formTabOrderChecks(page, screenshots, database, saved, command) {
    let checks = 0;
    const name = 'Customer Details';
    const form = (await database()).Forms.find(f => f.Name === name);
    const inputs = form.Controls.filter(c => c.Kind === 'TextBox' || c.Kind === 'CheckBox');
    assert.ok(inputs.length >= 3);
    const button = label => activateDialogButton(page, page.getByRole('button', { name: label, exact: true }).last(), label);
    async function focus(target) {
        for (const key of ['Tab', 'Shift+Tab']) for (let index = 0; index < 50; index++) {
            if (await target.evaluate(element => element.ownerDocument.activeElement === element)) return;
            await page.keyboard.press(key); await page.waitForTimeout(80);
        }
        throw new Error('Form control is not reachable using native keyboard focus.');
    }
    await command('formTabOrder'); await button('Auto Order'); await button('Cancel');
    assert.deepEqual((await database()).Forms.find(f => f.Name === name).Controls, form.Controls); checks++;
    await command('formTabOrder'); await button('Move Down');
    const stop = page.getByRole('checkbox', { name: 'Tab Stop', exact: true }).last();
    await focus(stop); await page.keyboard.press('Space');
    await page.screenshot({ path: screenshots + '/form-tab-order.png', fullPage: true });
    await button('Apply Tab Order');
    const updated = (await saved(db => db.Forms.find(f => f.Name === name).Controls.find(c => c.Id === inputs[0].Id).TabIndex === 1)).Forms.find(f => f.Name === name);
    assert.equal(updated.Controls.find(c => c.Id === inputs[0].Id).TabStop, false);
    assert.equal(updated.Controls.find(c => c.Id === inputs[1].Id).TabIndex, 0);
    assert.deepEqual(updated.Controls.map(c => c.Id), form.Controls.map(c => c.Id)); checks++;
    await page.keyboard.press('Control+z');
    await saved(db => db.Forms.find(f => f.Name === name).Controls.find(c => c.Id === inputs[0].Id).TabIndex === -1); checks++;
    await page.keyboard.press('Control+y');
    await saved(db => db.Forms.find(f => f.Name === name).Controls.find(c => c.Id === inputs[0].Id).TabStop === false); checks++;
    await command('datasheetView');
    const second = page.getByRole(inputs[1].Kind === 'CheckBox' ? 'checkbox' : 'textbox', { name: inputs[1].Caption, exact: true }).last();
    const third = page.getByRole(inputs[2].Kind === 'CheckBox' ? 'checkbox' : 'textbox', { name: inputs[2].Caption, exact: true }).last();
    await second.waitFor({ state: 'attached' }); await focus(second);
    await page.keyboard.press('Tab'); await page.waitForTimeout(150);
    assert.equal(await third.evaluate(element => element.ownerDocument.activeElement === element), true);
    await page.keyboard.press('Shift+Tab'); await page.waitForTimeout(150);
    assert.equal(await second.evaluate(element => element.ownerDocument.activeElement === element), true); checks++;
    await command('designView');
    await page.getByRole('button', { name: 'Form Design', exact: true }).press('Enter');
    await command('formTabOrder'); await button('Auto Order'); await button('Apply Tab Order');
    const ordered = [...inputs].sort((a, b) => a.Y - b.Y || a.X - b.X);
    await saved(db => db.Forms.find(f => f.Name === name).Controls.find(c => c.Id === ordered[0].Id).TabIndex === 0); checks++;
    await writeFile(screenshots + '/form-tab-order-checks.json', JSON.stringify({ checks, nativeTabAndShiftTab: true, keepsDrawOrder: true }, null, 2));
    return checks;
}
