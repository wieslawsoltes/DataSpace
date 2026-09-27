#!/usr/bin/env python3
"""Compile the sample against exact generated packages, with no library project references."""
from pathlib import Path
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
target = root / 'artifacts' / 'package-consumer'
packages = root / 'artifacts' / 'packages'
archives = list(packages.glob('DataSpace.Controls.*.nupkg'))
if len(archives) != 1:
    raise SystemExit('Expected exactly one generated Controls package.')
with zipfile.ZipFile(archives[0]) as archive:
    manifest = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith('.nuspec'))))
    version = manifest.find('.//{*}metadata/{*}version').text
if not version:
    raise SystemExit('Generated package has no version.')
if target.exists():
    shutil.rmtree(target)
shutil.copytree(root / 'src' / 'DataSpace.App', target, ignore=shutil.ignore_patterns('bin', 'obj'))
project = target / 'DataSpace.App.csproj'
tree = ET.parse(project)
count = 0
for group in tree.getroot().findall('ItemGroup'):
    for reference in list(group.findall('ProjectReference')):
        group.remove(reference)
        ET.SubElement(group, 'PackageReference', {'Include': 'DataSpace.Controls', 'Version': '[' + version + ']'})
        count += 1
if count != 1 or tree.getroot().findall('.//ProjectReference'):
    raise SystemExit('Expected exactly one app-to-controls reference; refusing an ambiguous consumer test.')
tree.write(project, encoding='utf-8', xml_declaration=False)
# Uno's Debug dependency graph includes DevServer; restore the same configuration
# that is compiled below instead of reusing Debug assets in an optimized build.
subprocess.run(['dotnet', 'restore', str(project), '-p:Configuration=Release', '--source', str(packages), '--source', 'https://api.nuget.org/v3/index.json'], cwd=root, check=True)
for framework in ('net10.0-desktop', 'net10.0-browserwasm'):
    subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '-f', framework, '--no-restore'], cwd=root, check=True)
libraries = json.loads((target / 'obj' / 'project.assets.json').read_text())['libraries']
required = {'DataSpace.Core', 'DataSpace.Query', 'DataSpace.Storage', 'DataSpace.Rendering', 'DataSpace.Controls'}
actual = {name.split('/')[0] for name, value in libraries.items() if value.get('type') == 'package' and name.endswith('/' + version)}
if not required.issubset(actual):
    raise SystemExit('Consumer did not resolve all five DataSpace libraries at the generated package version.')
print(f'PASS: both Uno heads compiled using all five DataSpace {version} NuGet packages; no library ProjectReference remained.')
