/**
 * Reach a native XAML dialog button using actual keyboard traversal.
 * A clipped accessibility proxy's DOM focus does not itself establish native
 * focus. No click/evaluate-to-invoke fallback is allowed: unreachable commands
 * must fail the browser gate, especially for destructive confirmations.
 */
export async function activateDialogButton(page, target, name, options = {}) {
    const steps = options.stepsPerDirection ?? 32;
    const settle = options.settleMilliseconds ?? 100;
    if (!Number.isInteger(steps) || steps < 1 || steps > 100 ||
        !Number.isFinite(settle) || settle < 0 || settle > 1000) {
        throw new RangeError('Invalid native dialog navigation limits.');
    }
    await target.waitFor({ state: 'attached', timeout: 5000 });
    if (!await target.isEnabled()) throw new Error(name + ' is disabled.');
    const trace = [];
    // Reverse traversal also covers footer controls preceding the safe default
    // Cancel button. Neither traversal changes the default action to Continue.
    for (const key of ['Tab', 'Shift+Tab']) {
        for (let i = 0; i < steps; i++) {
            await page.keyboard.press(key);
            await page.waitForTimeout(settle);
            const focus = await target.evaluate(element => {
                const active = element.ownerDocument.activeElement;
                return {
                    focused: active === element,
                    active: active ? {
                        tag: active.tagName, id: active.id,
                        name: active.getAttribute('aria-label') ?? active.textContent?.trim().slice(0, 120)
                    } : null
                };
            });
            trace.push({ key, ...focus });
            if (!focus.focused) continue;
            // Space activates the focused button without invoking the dialog's
            // Enter default (Cancel for destructive-action confirmations).
            await page.keyboard.press('Space');
            return;
        }
    }
    throw new Error(name + ' must be keyboard reachable. Focus trace: ' + JSON.stringify(trace));
}
