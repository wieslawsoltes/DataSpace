// MIT. Uno 6.7 Skia browser compatibility adapter; requalify when upgrading Uno.
// Semantic composite controls and the document-level native keyboard dispatcher
// both handle the same keydown in the pinned runtime. Route those keys once via
// the native dispatcher, keeping its key state, focus, selection and routed events.
// Pointer/assistive-technology clicks, text entry and IME remain untouched.
const installations = new WeakMap();

export function installKeyboardRouter(document, dispatchNative) {
    if (!document?.addEventListener || typeof dispatchNative !== 'function') {
        throw new TypeError('A document and native keyboard dispatcher are required.');
    }
    if (installations.has(document)) return installations.get(document);
    const keydown = event => {
        if (event.isComposing || event.keyCode === 229) return;
        if (event.target?.isContentEditable || event.target?.closest?.('input, textarea, [contenteditable="true"]')) return;
        const element = event.target?.closest?.('[role="combobox"], [role="option"], [role="menuitem"]');
        const root = document.getElementById('uno-semantics-root');
        if (!element?.id?.startsWith('uno-semantics-') || !root?.contains(element)) return;
        const role = element.getAttribute('role');
        const activate = event.key === 'Enter' || event.key === ' ';
        const duplicated = role === 'combobox'
            ? activate || event.key === 'Escape' || (event.key === 'ArrowDown' && event.altKey)
            : role === 'option' ? activate
            : role === 'menuitem' && (activate || event.key === 'Escape' || event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'ArrowRight');
        if (!duplicated) return;
        // Capture precedes the semantic target handlers and the document bubble
        // listener. Forward the original event once; keyup follows Uno normally.
        event.preventDefault();
        event.stopImmediatePropagation();
        dispatchNative(event);
    };
    document.addEventListener('keydown', keydown, true);
    const dispose = () => {
        document.removeEventListener('keydown', keydown, true);
        if (installations.get(document) === dispose) installations.delete(document);
    };
    installations.set(document, dispose);
    return dispose;
}

export function install() {
    const dispatcher = globalThis.Uno?.UI?.Runtime?.Skia?.BrowserKeyboardInputSource;
    if (typeof dispatcher?.onKeyboardEvent !== 'function') {
        throw new Error('The configured Uno browser keyboard dispatcher is unavailable. Requalify the input adapter when upgrading Uno.');
    }
    installKeyboardRouter(globalThis.document, event => dispatcher.onKeyboardEvent(event));
}
