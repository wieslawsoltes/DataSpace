#!/usr/bin/env python3
"""Stage the complete Uno publish root without guessing the SDK's output subdirectory."""
from pathlib import Path
import shutil
import sys

output = Path(sys.argv[1] if len(sys.argv) > 1 else 'artifacts/site')
roots = [Path('artifacts/publish'), Path('src/DataSpace.App/bin/Release/net10.0-browserwasm')]
indexes = [file for root in roots if root.exists() for file in root.rglob('index.html')]
if not indexes:
    raise SystemExit('Uno publish did not produce index.html.')
source = indexes[0].parent
if output.exists():
    shutil.rmtree(output)
shutil.copytree(source, output)
shutil.copy2('src/DataSpace.App/browser-storage.js', output / 'browser-storage.js')
for name in ('browser-sqlite.js', 'sqlite-worker.js', 'browser-input.js'):
    shutil.copy2(Path('src/DataSpace.App') / name, output / name)
package = Path('scripts/node_modules/sql.js')
if not (package / 'dist/sql-wasm.wasm').is_file():
    raise SystemExit('Run npm install in scripts before staging SQLite assets.')
vendor = output / 'vendor/sqlite'
vendor.mkdir(parents=True, exist_ok=True)
for name in ('sql-wasm.js', 'sql-wasm.wasm'):
    shutil.copy2(package / 'dist' / name, vendor / name)
shutil.copy2(package / 'LICENSE', vendor / 'LICENSE')
(output / '.nojekyll').touch()
if not any(output.rglob('*.wasm')):
    raise SystemExit('Refusing to publish a site with no .wasm runtime.')
print(f'Staged {sum(1 for p in output.rglob("*") if p.is_file())} files from {source} to {output}')
