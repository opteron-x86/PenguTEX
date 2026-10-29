#!/usr/bin/env python3
"""Reproducible synthetic end-to-end timings and cache-recovery checks, not game FPS."""
import argparse
import hashlib
import json
import pathlib
import platform
import struct
import subprocess
import tempfile
import time

p = argparse.ArgumentParser()
p.add_argument('--binary', required=True)
p.add_argument('--files', type=int, default=32)
p.add_argument('--size', type=int, default=1024)
p.add_argument('--workers', type=int, default=2)
p.add_argument('--report', required=True)
a = p.parse_args()
if not 2 <= a.files <= 256 or a.size not in (64, 128, 256, 512, 1024, 2048):
    p.error('Use 2–256 files and a power-of-two size from 64 to 2048.')
binary = str(pathlib.Path(a.binary).resolve())
report = {'schema': 1, 'kind': 'synthetic-mip-copy', 'platform': platform.platform(),
          'files': a.files, 'size': a.size, 'workers': a.workers, 'runs': []}
report['version'] = subprocess.check_output([binary, '--version'], text=True).strip()
with tempfile.TemporaryDirectory(prefix='pengutex-benchmark-') as temp:
    root = pathlib.Path(temp); source = root / 'source/textures/armor'; source.mkdir(parents=True)
    mips = a.size.bit_length()
    payload_bytes = sum(max(1, (max(1, a.size >> m) + 3) // 4) ** 2 * 16 for m in range(mips))
    header = bytearray(148); header[:4] = b'DDS '; header[84:88] = b'DX10'
    for at, value in {4:124, 8:0xA1007, 12:a.size, 16:a.size, 20:a.size*a.size, 28:mips,
                      76:32, 80:4, 108:0x401008, 128:98, 132:3, 140:1}.items():
        struct.pack_into('<I', header, at, value)
    originals = {}
    for i in range(a.files):
        file = source / f'fixture-{i:03d}.dds'
        payload = (bytes([64, i]) + bytes(14)) * (payload_bytes // 16)
        file.write_bytes(header + payload); originals[file.name] = hashlib.sha256(file.read_bytes()).hexdigest()
    command = [binary, 'build', '--folder', str(root/'source'), '--max', str(a.size//2),
               '--output', str(root/'output'), '--cache', str(root/'cache'), '--workers', str(a.workers)]
    def build(label):
        start = time.perf_counter(); result = subprocess.run(command, text=True, capture_output=True, check=True)
        elapsed = time.perf_counter()-start; data = json.loads(result.stdout)
        report['runs'].append({'name':label, 'wallSeconds':elapsed, 'engineBuildSeconds':data['Seconds'],
                              'files':data['Files'], 'reused':data['Reused'], 'bytes':data['Bytes']})
        subprocess.run([binary,'verify',str(root/'output')], check=True, capture_output=True)
        return data
    assert build('cold')['Reused'] == 0
    assert build('warm')['Reused'] == a.files
    cache = next((root/'cache').glob('*.dds')); content = bytearray(cache.read_bytes()); content[-1] ^= 1; cache.write_bytes(content)
    assert build('corrupt-cache-recovery')['Reused'] == a.files-1
    generated = root/'output/textures/armor/fixture-000.dds'; generated.write_bytes(b'broken')
    assert subprocess.run([binary,'verify',str(root/'output')], capture_output=True).returncode != 0
    assert build('output-recovery')['Reused'] == a.files
    for name, digest in originals.items():
        assert hashlib.sha256((source/name).read_bytes()).hexdigest() == digest
    (source/'fixture-000.dds').unlink()
    assert build('removed-source')['Files'] == a.files-1
    assert not generated.exists()
    report['sourcePayloadBytes'] = payload_bytes*a.files
    report['outputPayloadBytes'] = report['runs'][0]['bytes']-148*a.files
    report['validation'] = 'All runs verified; source hashes preserved; cache/output damage and stale output recovered.'
path = pathlib.Path(a.report); path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(report, indent=2)+'\n'); print(json.dumps(report, indent=2))
