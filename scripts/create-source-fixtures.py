#!/usr/bin/env python3
"""Create disposable, real SQLite and JSON browser fixtures; never touches a user database."""
from pathlib import Path
import json
import sqlite3
root = Path('artifacts/external')
root.mkdir(parents=True, exist_ok=True)
path = root / 'items.sqlite'
if path.exists(): path.unlink()
with sqlite3.connect(path) as db:
    db.execute('CREATE TABLE items(id INTEGER PRIMARY KEY,title TEXT,amount TEXT,optional TEXT,bytes BLOB)')
    db.executemany('INSERT INTO items VALUES(?,?,?,?,?)', [(i, f'External row {i} Żółć 😀', '12345678901234567890.123456789', None if i % 2 else '', bytes([0,1,255])) for i in range(1,404)])
    db.execute("CREATE TABLE exact_values(id INTEGER PRIMARY KEY, title TEXT)")
    db.execute('INSERT INTO exact_values VALUES(?,?)', (9223372036854775807, 'largest signed 64-bit integer'))
(root / 'items.json').write_text(json.dumps({'data': {'items': [{'id': 1, 'title': 'JSON Żółć 😀', 'active': True, 'empty': '', 'missing': None}, {'id': 2, 'title': 'two', 'active': False}, {'id': 3, 'title': 'three', 'active': True}]}}, ensure_ascii=False))
print('Created disposable SQLite (403 paged rows plus Int64 fixture) and JSON sources.')
