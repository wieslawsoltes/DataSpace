// MIT. Standalone IndexedDB optimistic store; no framework dependency and no network requests.
export class IndexedWorkspaceStore {
    constructor(databaseName = 'DataSpace.v1', key = 'workspace') {
        this.databaseName = databaseName;
        this.key = key;
        this.connection = null;
    }
    async open() {
        if (!this.connection) {
            this.connection = new Promise((resolve, reject) => {
                const request = indexedDB.open(this.databaseName, 1);
                request.onupgradeneeded = () => request.result.createObjectStore('documents');
                request.onerror = () => reject(request.error ?? new Error('Browser storage could not be opened.'));
                request.onblocked = () => reject(new Error('A different window is blocking the database upgrade. Close it and reload.'));
                request.onsuccess = () => {
                    const database = request.result;
                    database.onversionchange = () => { database.close(); this.connection = null; };
                    resolve(database);
                };
            }).catch(error => { this.connection = null; throw error; });
        }
        return this.connection;
    }
    validate(value) {
        if (value === undefined) return null;
        if (!value || typeof value.version !== 'string' || !value.version || value.version.includes('\n') || typeof value.json !== 'string') {
            throw new Error('Invalid stored database envelope. The original data has not been overwritten.');
        }
        return value;
    }
    async load() {
        const database = await this.open();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction('documents', 'readonly');
            const request = transaction.objectStore('documents').get(this.key);
            let result = null;
            let failure = null;
            request.onsuccess = () => {
                try { result = this.validate(request.result); }
                catch (error) { failure = error; transaction.abort(); }
            };
            transaction.oncomplete = () => resolve(result);
            transaction.onabort = transaction.onerror = () => reject(failure ?? transaction.error ?? new Error('Browser storage read failed.'));
        });
    }
    async save(json, expectedVersion) {
        if (typeof json !== 'string' || json.length > 64 * 1024 * 1024) throw new Error('Database size exceeds the 64 MiB character limit.');
        if (expectedVersion !== null && typeof expectedVersion !== 'string') throw new TypeError('Expected version must be a string or null.');
        const database = await this.open();
        return new Promise((resolve, reject) => {
            // The comparison and replacement share one read-write transaction, including across browser tabs.
            const transaction = database.transaction('documents', 'readwrite');
            const objects = transaction.objectStore('documents');
            const request = objects.get(this.key);
            const version = crypto.randomUUID();
            let failure = null;
            request.onsuccess = () => {
                try {
                    const previous = this.validate(request.result);
                    if ((previous?.version ?? null) !== expectedVersion) throw new Error('The stored database changed in another window. Export your changes before reloading.');
                    if (previous) objects.put(previous, this.key + ':previous');
                    objects.put({ version, json }, this.key);
                } catch (error) { failure = error; transaction.abort(); }
            };
            transaction.oncomplete = () => resolve(version);
            transaction.onabort = transaction.onerror = () => reject(failure ?? transaction.error ?? new Error('Browser storage save failed. Export a file to preserve your changes.'));
        });
    }
    async close() { if (this.connection) (await this.connection).close(); this.connection = null; }
}

const workspace = new IndexedWorkspaceStore();
let dirty = false;
export async function readWorkspace() {
    const current = await workspace.load();
    return current ? current.version + '\n' + current.json : '';
}
export async function writeWorkspace(json, expectedVersion) {
    return workspace.save(json, expectedVersion || null);
}
export function setDirty(value) { dirty = Boolean(value); }
export function ready(name, storageLoaded) {
    document.title = name + ' — DataSpace';
    document.documentElement.dataset.dataspaceReady = 'true';
    document.documentElement.dataset.dataspaceStorageLoaded = String(storageLoaded);
    window.dispatchEvent(new Event('dataspace-ready'));
}
export function reportFailure(message) {
    document.documentElement.dataset.dataspaceError = String(message);
    console.error('DataSpace startup:', message);
}
if (typeof window !== 'undefined') {
    window.addEventListener('beforeunload', event => {
        if (dirty) { event.preventDefault(); event.returnValue = ''; }
    });
    // Staged native text edits also deserve an unload warning before they have been committed to the document.
    document.addEventListener('input', event => {
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) dirty = true;
    }, true);
}
export function pickTextFile(kind) {
    return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file'; input.accept = kind === 'csv' ? '.csv,text/csv' : '.dspace,.json,application/json';
        input.style.display = 'none'; document.body.appendChild(input);
        let done = false;
        function complete(value, error) {
            if (done) return; done = true; input.remove();
            if (error) reject(error); else resolve(value);
        }
        input.addEventListener('cancel', () => complete(null), { once: true });
        input.addEventListener('change', async () => {
            try {
                const file = input.files?.[0];
                if (!file) { complete(null); return; }
                if (file.size > 64 * 1024 * 1024) throw new Error('Import is limited to 64 MiB.');
                complete(await file.text());
            } catch (error) { complete(null, error); }
        }, { once: true });
        input.click();
    });
}
export function downloadFile(name, mimeType, base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; document.body.appendChild(anchor);
    anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 30000);
}
