// MIT. Browser file adapter and worker RPC, reusable independently of Uno.
export class SqliteWorkerClient {
    constructor() {
        this.worker = new Worker(new URL('sqlite-worker.js', import.meta.url));
        this.pending = new Map(); this.next = 0; this.closed = false;
        this.worker.onmessage = ({ data }) => {
            const entry = this.pending.get(data.id); if (!entry) return;
            this.pending.delete(data.id); clearTimeout(entry.timer);
            if (data.error) entry.reject(new Error(data.error)); else entry.resolve(data.result);
        };
        this.worker.onerror = () => this.close('SQLite worker failed. Check the local WASM assets and reopen the file.');
    }
    call(operation, payload, transfer = []) {
        if (this.closed) return Promise.reject(new Error('SQLite source is closed.'));
        const id = ++this.next;
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => this.close('SQLite operation timed out. Reopen the file.'), 30000);
            this.pending.set(id, { resolve, reject, timer });
            try { this.worker.postMessage({ id, operation, payload }, transfer); }
            catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
        });
    }
    async open(bytes) { return this.call('open', bytes, [bytes]); }
    close(message = 'SQLite operation cancelled; reopen the file to continue.') {
        if (this.closed) return; this.closed = true; this.worker.terminate();
        for (const entry of this.pending.values()) { clearTimeout(entry.timer); entry.reject(new Error(message)); }
        this.pending.clear();
    }
}
const connections = new Map();
function pickFile() {
    return new Promise((resolve, reject) => {
        const input = document.createElement('input'); input.type = 'file'; input.accept = '.sqlite,.sqlite3,.db'; input.style.display = 'none'; document.body.appendChild(input);
        input.addEventListener('cancel', () => { input.remove(); resolve(null); }, { once: true });
        input.addEventListener('change', () => { const file = input.files?.[0]; input.remove(); if (file && file.size > 16 * 1024 * 1024) reject(new Error('SQLite files are limited to 16 MiB.')); else resolve(file ?? null); }, { once: true });
        input.click();
    });
}
export async function pickSqlite() {
    const file = await pickFile(); if (!file) return null;
    const client = new SqliteWorkerClient();
    try { await client.open(await file.arrayBuffer()); const id = crypto.randomUUID(); connections.set(id, client); return JSON.stringify({ id, name: file.name }); }
    catch (error) { client.close(); throw error; }
}
export async function sqliteTables(id) { const client = connections.get(id); if (!client) throw new Error('SQLite source is closed.'); return JSON.stringify(await client.call('tables')); }
export async function sqlitePage(id, request) { const client = connections.get(id); if (!client) throw new Error('SQLite source is closed.'); return JSON.stringify(await client.call('page', JSON.parse(request))); }
export function closeSqlite(id) { connections.get(id)?.close(); connections.delete(id); }
export async function exportSqlite(json) {
    const client = new SqliteWorkerClient();
    try {
        const bytes = new Uint8Array(await client.call('export', JSON.parse(json)));
        const pieces = []; for (let offset = 0; offset < bytes.length; offset += 32768) pieces.push(String.fromCharCode(...bytes.subarray(offset, offset + 32768)));
        return btoa(pieces.join(''));
    } finally { client.close(); }
}
