#!/usr/bin/env python3
"""Build the research-only BC7E/bc7f shim on Linux. Basis source is not shipped."""
import argparse
import os
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--basis-source', required=True, type=Path,
                    help='Basis Universal checkout at 073eb6a0a34a23d77e75e54f41dcd9653a52da83')
args = parser.parse_args()
source = args.basis_source.resolve()
if sys.platform != 'linux':
    parser.error('The comparison shim currently builds on Linux; production BC7E supports Windows too')
if (source / '.git').exists():
    revision = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
    if revision != '073eb6a0a34a23d77e75e54f41dcd9653a52da83':
        parser.error('Use the pinned Basis Universal revision')
subprocess.run([sys.executable, str(root / 'tools/build-native.py')], check=True)
build = root / '.tools/bc7/build-linux-x64'
output = root / '.artifacts/bc7/libcompare.so'
output.parent.mkdir(parents=True, exist_ok=True)
subprocess.run([os.environ.get('CXX', 'g++'), '-std=c++17', '-O3', '-fPIC', '-fno-strict-aliasing',
                '-DBASISD_SUPPORT_KTX2_ZSTD=0', '-shared', '-pthread', '-I', str(build),
                '-I', str(source / 'transcoder'), str(root / 'tools/Bc7Benchmark/compare.cpp'),
                str(source / 'transcoder/basisu_transcoder.cpp'),
                *(str(build / name) for name in ('bc7e.o', 'bc7e_sse2.o', 'bc7e_avx2.o')),
                '-o', str(output)], check=True)
print(output)
