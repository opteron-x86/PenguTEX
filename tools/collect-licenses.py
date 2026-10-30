#!/usr/bin/env python3
"""Inventory exact restored packages and collect their bundled licenses/notices."""
import argparse
import json
import pathlib
import shutil
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rid', choices=('linux-x64', 'win-x64'), default='linux-x64')
args = parser.parse_args()
out = root / 'dist' / args.rid
licenses = out / 'licenses'
licenses.mkdir(exist_ok=True)
for file in (root / 'packaging/licenses').iterdir():
    shutil.copyfile(file, licenses / file.name)
assets = json.loads((root / 'src/PenguTEX.Desktop/obj/project.assets.json').read_text(encoding='utf-8'))
folders = [pathlib.Path(p) for p in assets['packageFolders']]
lines = ['Third-party package inventory', '=============================', '',
         'PenguTEX is GPL-3.0-only. Dependencies retain their own licenses.',
         'See licenses/ for texts and .deps.json for runtime selections.', '']
for name, item in sorted(assets['libraries'].items()):
    if item['type'] != 'package':
        continue
    folder = next((p / item['path'] for p in folders if (p / item['path']).is_dir()), None)
    if folder is None:
        raise SystemExit('Missing restored package: ' + name)
    spec = ET.parse(next(folder.glob('*.nuspec')))
    def value(tag):
        return next((e.text or '' for e in spec.iter() if e.tag.split('}')[-1] == tag), '')
    repository = next((e.attrib for e in spec.iter() if e.tag.split('}')[-1] == 'repository'), {})
    lines += [name, 'License: ' + (value('license') or value('licenseUrl') or 'See upstream license text in licenses/'),
              'Copyright: ' + (value('copyright') or 'See upstream license text'),
              'Project: ' + (value('projectUrl') or repository.get('url', '') or 'See licenses/UPSTREAM-SOURCES.json'),
              'Source revision: ' + repository.get('commit', 'See licenses/UPSTREAM-SOURCES.json')]
    for file in folder.rglob('*'):
        if file.is_file() and file.name.lower().startswith(('license', 'licence', 'thirdpartynotice', 'ofl.')):
            dest = licenses / name.replace('/', '-') / file.relative_to(folder)
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file, dest)
    lines.append('')
(out / 'THIRD-PARTY-PACKAGES.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
# Self-contained runtime packs are not always listed in the managed package graph.
runtime = json.loads((out / 'PenguTEX.Desktop.runtimeconfig.json').read_text(encoding='utf-8'))['runtimeOptions']['includedFrameworks']
for framework in runtime:
    if framework['name'] != 'Microsoft.NETCore.App':
        continue
    package = 'microsoft.netcore.app.runtime.' + args.rid + '/' + framework['version']
    folder = next((p / package for p in folders if (p / package).is_dir()), None)
    if folder is None:
        raise SystemExit('Missing runtime pack: ' + package)
    for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
        shutil.copyfile(folder / name, licenses / ('dotnet-' + framework['version'] + '-' + name))
