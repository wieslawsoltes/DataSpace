import assert from 'node:assert/strict';
import test from 'node:test';
import { activateDialogButton, activateMenuItem } from './native-dialog-actions.mjs';

// These unit tests validate only helper control flow, not Uno/browser behavior.
function fixture(transition, { enabled = true, initiallyFocused = false, delayed = false, activation = 'Space' } = {}) {
    const commands = [];
    const waits = [];
    const document = { activeElement: null };
    const element = (name) => ({
        ownerDocument: document, tagName: 'BUTTON', id: name.toLowerCase(),
        textContent: name, getAttribute: key => key === 'aria-label' ? name : null
    });
    const targetElement = element('Continue');
    const cancelElement = element('Cancel');
    document.activeElement = initiallyFocused ? targetElement : cancelElement;
    let pending;
    const page = {
        keyboard: {
            async press(key) {
                commands.push(key);
                if (key === activation) {
                    assert.equal(document.activeElement, targetElement, 'Activation requires target focus.');
                    return;
                }
                const next = transition(key, commands, { target: targetElement, cancel: cancelElement });
                if (delayed) pending = next;
                else document.activeElement = next;
            }
        },
        async waitForTimeout(milliseconds) {
            waits.push(milliseconds);
            if (pending !== undefined) { document.activeElement = pending; pending = undefined; }
        }
    };
    const target = {
        async waitFor(options) { assert.equal(options.state, 'attached'); },
        async isEnabled() { return enabled; },
        async evaluate(fn) { return fn(targetElement); }
    };
    return { page, target, commands, waits };
}

test('uses real forward traversal and Space on the focused target', async () => {
    const f = fixture((_key, _commands, e) => e.target);
    await activateDialogButton(f.page, f.target, 'Continue');
    assert.deepEqual(f.commands, ['Tab', 'Space']);
    assert.deepEqual(f.waits, [100]);
});

test('reverse traversal reaches a footer button before safe default Cancel', async () => {
    const f = fixture((key, _commands, e) => key === 'Shift+Tab' ? e.target : e.cancel);
    await activateDialogButton(f.page, f.target, 'Continue', { stepsPerDirection: 2 });
    assert.deepEqual(f.commands, ['Tab', 'Tab', 'Shift+Tab', 'Space']);
});

test('never invokes an unreachable destructive command and reports focus trace', async () => {
    const f = fixture((_key, _commands, e) => e.cancel);
    await assert.rejects(activateDialogButton(f.page, f.target, 'Continue', { stepsPerDirection: 2 }),
        /Continue must be keyboard reachable.*Cancel/);
    assert.deepEqual(f.commands, ['Tab', 'Tab', 'Shift+Tab', 'Shift+Tab']);
});

test('does not activate a disabled command', async () => {
    const f = fixture((_key, _commands, e) => e.target, { enabled: false });
    await assert.rejects(activateDialogButton(f.page, f.target, 'Continue'), /disabled/);
    assert.deepEqual(f.commands, []);
});

test('waits for focus to settle after native key dispatch', async () => {
    const f = fixture((_key, _commands, e) => e.target, { delayed: true });
    await activateDialogButton(f.page, f.target, 'Continue');
    assert.deepEqual(f.commands, ['Tab', 'Space']);
});

test('does not trust initial DOM focus without a keyboard transition', async () => {
    const f = fixture((_key, commands, e) => commands.length === 1 ? e.cancel : e.target,
        { initiallyFocused: true });
    await activateDialogButton(f.page, f.target, 'Continue');
    assert.deepEqual(f.commands, ['Tab', 'Tab', 'Space']);
});

test('rejects invalid limits before sending keyboard input', async () => {
    const f = fixture((_key, _commands, e) => e.target);
    await assert.rejects(activateDialogButton(f.page, f.target, 'Continue', { stepsPerDirection: 0 }), RangeError);
    await assert.rejects(activateDialogButton(f.page, f.target, 'Continue', { settleMilliseconds: -1 }), RangeError);
    assert.deepEqual(f.commands, []);
});


test('menu activation uses arrow traversal and Enter on the intended item', async () => {
    const f = fixture((_key, _commands, e) => e.target, { activation: 'Enter' });
    await activateMenuItem(f.page, f.target, 'Queries');
    assert.deepEqual(f.commands, ['ArrowDown', 'Enter']);
});

test('reverse menu traversal reaches an item above a non-wrapping edge', async () => {
    const f = fixture((key, _commands, e) => key === 'ArrowUp' ? e.target : e.cancel, { activation: 'Enter' });
    await activateMenuItem(f.page, f.target, 'All Access Objects', { stepsPerDirection: 2 });
    assert.deepEqual(f.commands, ['ArrowDown', 'ArrowDown', 'ArrowUp', 'Enter']);
});

test('unreachable menu items never activate a different command', async () => {
    const f = fixture((_key, _commands, e) => e.cancel, { activation: 'Enter' });
    await assert.rejects(activateMenuItem(f.page, f.target, 'Freeze Fields', { stepsPerDirection: 2 }), /Freeze Fields must be keyboard reachable.*Cancel/);
    assert.deepEqual(f.commands, ['ArrowDown', 'ArrowDown', 'ArrowUp', 'ArrowUp']);
});

test('menu activation waits for actual native focus, ignoring initial DOM proxy focus', async () => {
    const f = fixture((_key, commands, e) => commands.length === 1 ? e.cancel : e.target,
        { activation: 'Enter', initiallyFocused: true, delayed: true });
    await activateMenuItem(f.page, f.target, 'Freeze Fields');
    assert.deepEqual(f.commands, ['ArrowDown', 'ArrowDown', 'Enter']);
});

test('disabled menu item cannot be activated', async () => {
    const f = fixture((_key, _commands, e) => e.target, { activation: 'Enter', enabled: false });
    await assert.rejects(activateMenuItem(f.page, f.target, 'Freeze Fields'), /disabled/);
    assert.deepEqual(f.commands, []);
});


// Platform-adapter unit tests simulate event propagation. Actual Uno behavior is
// separately gated by browser-smoke.mjs against the published WebAssembly app.
import { installKeyboardRouter } from '../src/DataSpace.App/browser-input.js';

function routingFixture({ role = 'combobox', outside = false, input = false } = {}) {
    const listeners = new Map();
    const root = { contains: element => element === target && !outside };
    const target = {
        id: 'uno-semantics-42', isContentEditable: false,
        getAttribute: name => name === 'role' ? role : null,
        closest: query => query.includes('input') ? (input ? target : null) : target
    };
    const document = {
        getElementById: name => name === 'uno-semantics-root' ? root : null,
        addEventListener: (type, callback, capture) => { assert.equal(capture, true); listeners.set(type, callback); },
        removeEventListener: (type, callback, capture) => { assert.equal(capture, true); if (listeners.get(type) === callback) listeners.delete(type); }
    };
    const native = [], semantic = [];
    const dispatch = event => native.push(event);
    function send(key, options = {}) {
        const event = {
            type: 'keydown', key, code: key, target, altKey: false, ...options,
            prevented: false, stopped: false,
            preventDefault() { this.prevented = true; },
            stopImmediatePropagation() { this.stopped = true; }
        };
        listeners.get('keydown')?.(event);
        if (!event.stopped) { semantic.push(event); dispatch(event); }
        return event;
    }
    return { document, target, native, semantic, dispatch, send, listeners };
}

for (const [role, key, altKey] of [
    ['combobox', 'Enter', false], ['combobox', ' ', false], ['combobox', 'Escape', false], ['combobox', 'ArrowDown', true],
    ['option', 'Enter', false], ['option', ' ', false],
    ['menuitem', 'Enter', false], ['menuitem', ' ', false], ['menuitem', 'Escape', false],
    ['menuitem', 'ArrowDown', false], ['menuitem', 'ArrowUp', false], ['menuitem', 'ArrowRight', false]
]) test(`routes ${role} ${JSON.stringify(key)} once through native input`, () => {
    const f = routingFixture({ role });
    const dispose = installKeyboardRouter(f.document, f.dispatch);
    const event = f.send(key, { altKey });
    assert.deepEqual(f.native, [event]); assert.deepEqual(f.semantic, []);
    assert.equal(event.prevented, true); assert.equal(event.stopped, true);
    assert.equal(f.listeners.has('keyup'), false, 'Native keyup and physical key state remain unmodified.');
    dispose();
});

for (const [key, fixtureOptions, eventOptions] of [
    ['Tab', {}, {}], ['a', {}, {}], ['ArrowDown', {}, {}],
    ['Enter', { outside: true }, {}], [' ', { input: true }, {}],
    ['Enter', {}, { isComposing: true }], ['Enter', {}, { keyCode: 229 }],
    ['Enter', { role: 'textbox' }, {}]
]) test(`leaves unrelated/text/IME event unchanged: ${JSON.stringify([key, fixtureOptions, eventOptions])}`, () => {
    const f = routingFixture(fixtureOptions); installKeyboardRouter(f.document, f.dispatch);
    const event = f.send(key, eventOptions);
    assert.equal(event.stopped, false); assert.equal(event.prevented, false);
    assert.deepEqual(f.semantic, [event]); assert.deepEqual(f.native, [event]);
});

test('keyboard router installs once and can be disposed and reinstalled', () => {
    const f = routingFixture(); const dispose = installKeyboardRouter(f.document, f.dispatch);
    assert.equal(installKeyboardRouter(f.document, f.dispatch), dispose);
    dispose(); dispose(); assert.equal(f.listeners.size, 0);
    const fresh = installKeyboardRouter(f.document, f.dispatch); assert.notEqual(fresh, dispose);
    const event = f.send('Enter'); assert.deepEqual(f.native, [event]); fresh();
});

test('keyboard router rejects incomplete host contracts', () => {
    assert.throws(() => installKeyboardRouter(null, () => {}), TypeError);
    assert.throws(() => installKeyboardRouter(routingFixture().document, null), TypeError);
});
