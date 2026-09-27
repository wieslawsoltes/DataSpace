#!/usr/bin/env python3
"""Build a package-only copy of the Uno sample; never resolves its controls through project references."""
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
target = root / 'artifacts' / 'package-consumer'
packages = root / 'artifacts' / 'packages'
if target.exists():
    shutil.rmtree(target)
shutil.copytree(root / 'src' / 'DataSpace.App', target, ignore=shutil.ignore_patterns('bin', 'obj'))
project = target / 'DataSpace.App.csproj'
tree = ET.parse(project)
count = 0
for group in tree.getroot().findall('ItemGroup'):
    for reference in list(group.findall('ProjectReference')):
        group.remove(reference)
        ET.SubElement(group, 'PackageReference', {'Include': 'DataSpace.Controls', 'Version': '$(Version)'})
        count += 1
if count != 1 or tree.getroot().findall('.//ProjectReference'):
    raise SystemExit('Expected exactly one app-to-controls reference; refusing an ambiguous consumer test.')
tree.write(project, encoding='utf-8', xml_declaration=False)
subprocess.run(['dotnet', 'restore', str(project), '--source', str(packages), '--source', 'https://api.nuget.org/v3/index.json'], cwd=root, check=True)
for framework in ('net10.0-desktop', 'net10.0-browserwasm'):
    subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '-f', framework, '--no-restore'], cwd=root, check=True)
assets = target / 'obj' / 'project.assets.json'
import json
libraries = json.loads(assets.read_text())['libraries']
required = {'DataSpace.Core', 'DataSpace.Query', 'DataSpace.Storage', 'DataSpace.Rendering', 'DataSpace.Controls'}
actual = {name.split('/')[0] for name, value in libraries.items() if value.get('type') == 'package'}
if not required.issubset(actual):
    raise SystemExit('Consumer did not resolve all five DataSpace libraries as packages.')
print('PASS: both Uno heads compiled using all five DataSpace NuGet packages; no library ProjectReference remained.')
