#!/usr/bin/env python3
"""Publish the Windows x64 desktop/CLI, licenses and portable ZIP using .NET 10."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--cross-compile', action='store_true',
                    help='Package on a non-Windows host without executing Windows binaries; not release validation.')
args = parser.parse_args()
if args.cross_compile == (os.name == 'nt'):
    parser.error('Use --cross-compile only on a non-Windows host. Releases require native Windows validation.')

root = Path(__file__).resolve().parents[1]
os.chdir(root)
os.environ.setdefault('DOTNET_CLI_HOME', str(root / '.tools' / 'cli-home'))
os.environ.setdefault('DOTNET_CLI_TELEMETRY_OPTOUT', '1')
os.environ.setdefault('PYTHONUTF8', '1')
dotnet = os.environ.get('PENGUTEX_DOTNET', 'dotnet')
version = subprocess.check_output([sys.executable, 'tools/version.py'], text=True).strip()
payload = root / 'dist' / 'win-x64'
if payload.exists():
    shutil.rmtree(payload)
payload.mkdir(parents=True)
for project in ('PenguTEX.Cli', 'PenguTEX.Desktop'):
    subprocess.run([dotnet, 'publish', f'src/{project}', '-c', 'Release', '-r', 'win-x64',
                    '--self-contained', 'true', '-o', str(payload), '--disable-build-servers',
                    '-m:1', '-nr:false', '-p:UseSharedCompilation=false'], check=True)
for name in ('LICENSE', 'README.md'):
    shutil.copyfile(root / name, payload / name)
shutil.copytree(root / 'docs', payload / 'docs')
shutil.copyfile(root / 'packaging/pengutex.svg', payload / 'pengutex.svg')
subprocess.run([sys.executable, 'tools/collect-licenses.py', '--rid', 'win-x64'], check=True)
if not args.cross_compile:
    subprocess.run([sys.executable, 'tools/cli-smoke.py', str(payload / 'PenguTEX.exe')], check=True)
else:
    print('Cross-compiled package only. Windows execution has NOT been tested.', flush=True)

archive = root / 'dist' / f'PenguTEX-{version}-win-x64.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as package:
    for file in sorted(payload.rglob('*')):
        if file.is_file():
            package.write(file, file.relative_to(payload.parent))
with archive.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
(archive.parent / 'SHA256SUMS.windows').write_text(f'{digest}  {archive.name}\n', encoding='utf-8')
print(f'Packaged {archive.name}', flush=True)
