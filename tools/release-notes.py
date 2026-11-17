#!/usr/bin/env python3
"""Write the GitHub release notes for this version from CHANGELOG.md.

A tagged release needs its own CHANGELOG.md section (tools/version.py --check-tag checks it).
Untagged development builds use the [Unreleased] section instead.
"""
import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[1]
version = subprocess.check_output([sys.executable, str(root / 'tools/version.py')], text=True).strip()
changelog = (root / 'CHANGELOG.md').read_text(encoding='utf-8')


def section(heading):
    if changelog.count(heading) != 1:
        return None
    return changelog.split(heading, 1)[1].split('\n', 1)[1].split('\n## ', 1)[0].strip()


notes = section(f'## [{version}]')
title = f'# PenguTEX {version}'
if notes is None:
    notes, title = section('## [Unreleased]') or '- No changes recorded.', f'# PenguTEX {version} (development build)'
base = f'https://github.com/opteron-x86/PenguTEX/blob/v{version}/docs'
print(f'''{title}

{notes}

Downloads: `win-x64.zip` for Windows (extract it and run `win-x64/PenguTEX.Desktop.exe`),
`linux-x64.tar.gz` or `x86_64.AppImage` for Linux. Alpha builds are unsigned.
See [installation]({base}/DISTRIBUTION.md) and the [alpha testing guide]({base}/ALPHA-TESTING.md).
Downloads include source, dependency licenses and `SHA256SUMS`.''')
