#!/usr/bin/env python3
"""Require the native desktop to open and shut itself down, within a bounded time."""
from pathlib import Path
import subprocess
import sys
import tempfile

if len(sys.argv) != 2:
    raise SystemExit('usage: gui-smoke.py /path/to/PenguTEX.Desktop[.exe]')
with tempfile.TemporaryDirectory(prefix='pengutex-gui-forêt-') as folder:
    subprocess.run([str(Path(sys.argv[1]).resolve()), '--settings', str(Path(folder) / 'settings.json'),
                    '--smoke'], check=True, timeout=30)
print('Native desktop opened and exited successfully.')
