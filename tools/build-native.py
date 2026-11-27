#!/usr/bin/env python3
"""Build the pinned BC7 encoder for the current x64 host (CMake + C++ required)."""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import subprocess
import tarfile
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ispc', help='Use an existing ISPC 1.32.0 compiler instead of downloading it')
args = parser.parse_args()
machine, system = platform.machine().lower(), platform.system()
macos = system == 'Darwin' and machine == 'arm64'
if not macos and (machine not in ('x86_64', 'amd64') or system not in ('Linux', 'Windows')):
    parser.error('Native BC7 packages currently support Linux/Windows x64 and macOS arm64')
windows = os.name == 'nt'
rid = 'win-x64' if windows else 'osx-arm64' if macos else 'linux-x64'
work = root / '.tools' / 'bc7'
work.mkdir(parents=True, exist_ok=True)
selected = args.ispc or os.environ.get('PENGUTEX_ISPC')
if selected:
    compiler = Path(selected).resolve()
else:
    # (archive, sha256, extracted folder) from the ispc v1.32.0 GitHub release.
    archive, digest, folder = {
        'win-x64': ('ispc-v1.32.0-windows.zip', '148d5cd96abe7501c730d5fbd00f1f0c893b96bfeada38d1811c9b6251e86a5c', 'ispc-v1.32.0-windows'),
        'linux-x64': ('ispc-v1.32.0-linux.tar.gz', 'faf266bda2b702d68976ffd26bbd36c1ddba43424cc3c44f25b092cd1aa1f64a', 'ispc-v1.32.0-linux'),
        'osx-arm64': ('ispc-v1.32.0-macOS.arm64.tar.gz', 'efc5180095554628c01022507329ba7a2141c7ba97cf1bccc2df85dc47874e02', 'ispc-v1.32.0-macOS.arm64'),
    }[rid]
    compiler = work / folder / 'bin' / ('ispc.exe' if windows else 'ispc')
    if not compiler.exists():
        package = work / archive
        if not package.exists():
            urllib.request.urlretrieve('https://github.com/ispc/ispc/releases/download/v1.32.0/' + archive, package)
        if hashlib.sha256(package.read_bytes()).hexdigest() != digest:
            raise SystemExit('ISPC archive checksum mismatch: ' + str(package))
        if windows:
            with zipfile.ZipFile(package) as zipped:
                zipped.extractall(work)
        else:
            with tarfile.open(package) as tar:
                tar.extractall(work, filter='data')
build = work / ('build-' + rid)
output = root / 'native' / 'bin' / rid
subprocess.run(['cmake', '-S', str(root / 'native/bc7'), '-B', str(build),
                '-DCMAKE_BUILD_TYPE=Release', '-DISPC_EXECUTABLE=' + str(compiler),
                '-DCMAKE_INSTALL_PREFIX=' + str(output),
                # Match the bundled .NET runtime's floor (libcoreclr minos 12.0), not the build host's OS.
                *(['-DCMAKE_OSX_DEPLOYMENT_TARGET=12.0'] if macos else []),
                '-DPENGUTEX_WARNINGS_AS_ERRORS=' + ('ON' if os.environ.get('CI') else 'OFF')], check=True)
subprocess.run(['cmake', '--build', str(build), '--config', 'Release', '--parallel', '2'], check=True)
subprocess.run(['ctest', '--test-dir', str(build), '-C', 'Release', '--output-on-failure'], check=True)
subprocess.run(['cmake', '--install', str(build), '--config', 'Release'], check=True)
print('Native BC7 ready:', output)
