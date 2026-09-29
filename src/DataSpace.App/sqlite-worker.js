// MIT. A dedicated, disposable SQLite WASM worker. Only catalog/page/export messages are accepted.
importScripts('vendor/sqlite/sql-wasm.js');
const engine = initSqlJs({ locateFile: name => new URL('vendor/sqlite/' + name, self.location.href).href });
const MAX_BYTES = 16 * 1024 * 1024;
let database = null;
let catalog = [];
const quote = name => '"' + name.replaceAll('"', '""') + '"';
function query(sql, params = []) {
    const statement = database.prepare(sql);
    try { statement.bind(params); const rows = []; while (statement.step()) rows.push(statement.get()); return rows; }
    finally { statement.free(); }
}
function describe() {
    const names = query("SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 129").map(row => row[0]);
    if (!names.length || names.length > 128) throw new Error('SQLite source requires 1–128 user tables.');
    return names.map(name => {
        const fields = query('SELECT name,type,pk FROM pragma_table_info(?) ORDER BY cid', [name]);
        if (!fields.length || fields.length > 128) throw new Error('SQLite tables require 1–128 columns.');
        const keys = fields.filter(f => f[2] > 0).sort((a, b) => a[2] - b[2]).map(f => f[0]);
        if (!keys.length) {
            if (fields.some(f => f[0].toLowerCase() === '_rowid_')) throw new Error('A table shadows _rowid_ and has no primary key.');
            keys.push('_rowid_');
        }
        return { id: name, name, columns: fields.map(f => ({ name: f[0], kind: 'text', nativeType: f[1] })), orderColumns: keys };
    });
}
function readPage(request) {
    if (!Number.isInteger(request.offset) || request.offset < 0 || request.offset > 1000000 || !Number.isInteger(request.limit) || request.limit < 1 || request.limit > 1000) throw new Error('Invalid page range.');
    const table = catalog.find(t => t.id === request.table);
    if (!table) throw new Error('SQLite table not found.');
    // Keep all 64-bit integers and numeric lexemes out of JS Number. Null and BLOB storage classes remain explicit.
    const columns = table.columns.map(c => `CASE WHEN typeof(${quote(c.name)})='blob' THEN 'hex:'||hex(${quote(c.name)}) ELSE CAST(${quote(c.name)} AS TEXT) END`);
    const statement = database.prepare(`SELECT ${columns.join(',')} FROM ${quote(table.name)} ORDER BY ${table.orderColumns.map(quote).join(',')} LIMIT ? OFFSET ?`);
    const rows = []; let characters = 0; let hasMore = false;
    try {
        statement.bind([request.limit + 1, request.offset]);
        while (statement.step()) {
            if (rows.length === request.limit) { hasMore = true; break; }
            const values = statement.get();
            for (const value of values) {
                if (value !== null && (typeof value !== 'string' || value.length > 1000000)) throw new Error('SQLite cell exceeds the size limit.');
                characters += value?.length ?? 0;
                if (characters > 2 * 1024 * 1024) throw new Error('SQLite page is too large. Request fewer rows.');
            }
            rows.push(values);
        }
    } finally { statement.free(); }
    return { columns: table.columns, rows, hasMore };
}
function exportTable(SQL, value) {
    const { name, columns, rows } = value;
    if (typeof name !== 'string' || !name || name.length > 64 || !Array.isArray(columns) || columns.length < 1 || columns.length > 128 || !Array.isArray(rows) || rows.length > 100000) throw new Error('Invalid SQLite export.');
    if (columns.some(c => typeof c !== 'string' || c.length < 1 || c.length > 512) || new Set(columns.map(c => c.toLowerCase())).size !== columns.length) throw new Error('Invalid export columns.');
    const output = new SQL.Database();
    try {
        output.run(`CREATE TABLE ${quote(name)} (${columns.map(c => quote(c) + ' TEXT').join(',')})`);
        output.run('BEGIN');
        const insert = output.prepare(`INSERT INTO ${quote(name)} VALUES (${columns.map(() => '?').join(',')})`);
        try {
            let characters = 0;
            for (const row of rows) {
                if (!Array.isArray(row) || row.length !== columns.length) throw new Error('Invalid export row.');
                for (const cell of row) {
                    if (cell !== null && typeof cell !== 'string') throw new Error('Export values must be strings or null.');
                    characters += cell?.length ?? 0;
                    if ((cell?.length ?? 0) > 1000000 || characters > MAX_BYTES) throw new Error('SQLite export exceeds the size limit.');
                }
                insert.run(row);
            }
        } finally { insert.free(); }
        output.run('COMMIT'); const bytes = output.export();
        if (bytes.byteLength > MAX_BYTES) throw new Error('SQLite export exceeds 16 MiB.');
        return bytes;
    } finally { output.close(); }
}
self.onmessage = async ({ data }) => {
    const { id, operation, payload } = data;
    try {
        const SQL = await engine;
        let result;
        if (operation === 'open') {
            if (!(payload instanceof ArrayBuffer) || payload.byteLength > MAX_BYTES || payload.byteLength < 100) throw new Error('Select a SQLite file no larger than 16 MiB.');
            database?.close(); database = null; catalog = [];
            database = new SQL.Database(new Uint8Array(payload));
            database.run('PRAGMA query_only=ON; PRAGMA trusted_schema=OFF;'); catalog = describe(); result = catalog;
        } else if (operation === 'tables') { if (!database) throw new Error('SQLite file is closed.'); result = catalog; }
        else if (operation === 'page') { if (!database) throw new Error('SQLite file is closed.'); result = readPage(payload); }
        else if (operation === 'export') {
            const bytes = exportTable(SQL, payload); self.postMessage({ id, result: bytes.buffer }, [bytes.buffer]); return;
        } else throw new Error('Unsupported SQLite operation.');
        self.postMessage({ id, result });
    } catch (error) { self.postMessage({ id, error: String(error?.message ?? error).slice(0, 500) }); }
};
