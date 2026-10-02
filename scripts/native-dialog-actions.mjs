/**
 * Reach a native XAML dialog button using actual keyboard traversal.
 * A clipped accessibility proxy's DOM focus does not itself establish native
 * focus. No click/evaluate-to-invoke fallback is allowed: unreachable commands
 * must fail the browser gate, especially for destructive confirmations.
 */
export function activateDialogButton(page, target, name, options = {}) {
    // Space activates the native button rather than the dialog's Enter default.
    return activateNativeItem(page, target, name, options, ['Tab', 'Shift+Tab'], 'Space');
}

/** Activate an open native menu using arrow-key focus, not a DOM-proxy focus call. */
export function activateMenuItem(page, target, name, options = {}) {
    return activateNativeItem(page, target, name, options, ['ArrowDown', 'ArrowUp'], 'Enter');
}

async function activateNativeItem(page, target, name, options, directions, activation) {
    const steps = options.stepsPerDirection ?? 32;
    const settle = options.settleMilliseconds ?? 100;
    if (!Number.isInteger(steps) || steps < 1 || steps > 100 ||
        !Number.isFinite(settle) || settle < 0 || settle > 1000) {
        throw new RangeError('Invalid native dialog navigation limits.');
    }
    await target.waitFor({ state: 'attached', timeout: 5000 });
    if (!await target.isEnabled()) throw new Error(name + ' is disabled.');
    const trace = [];
    // Traverse both ways: menus can stop at an edge, and dialog footer controls
    // can precede Cancel. Never trust an initial accessibility-proxy focus alone.
    for (const key of directions) {
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
            // Only real keyboard focus admits activation. No DOM click or
            // application-command invocation is used as a fallback.
            await page.keyboard.press(activation);
            return;
        }
    }
    throw new Error(name + ' must be keyboard reachable. Focus trace: ' + JSON.stringify(trace));
}
