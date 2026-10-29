#!/usr/bin/env bash
# Run explicitly after placing the portable folder in its final location.
set -euo pipefail
app_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 - "$app_dir" <<'PY'
import os, pathlib, sys
app = pathlib.Path(sys.argv[1])
target = pathlib.Path(os.environ.get('XDG_DATA_HOME', str(pathlib.Path.home() / '.local/share'))) / 'applications'
target.mkdir(parents=True, exist_ok=True)
def quoted(value):
    return '"' + str(value).replace('\\', '\\\\\\\\').replace('"', '\\\\"').replace('`', '\\\\`').replace('$', '\\\\$').replace('%', '%%') + '"'
entry = target / 'pengutex.desktop'
entry.write_text('[Desktop Entry]\nType=Application\nName=PenguTEX\nComment=Optimize Skyrim textures on Linux\n'
                 + 'Exec=' + quoted(app / 'PenguTEX.Desktop') + '\n'
                 + 'Icon=' + str(app / 'pengutex.svg') + '\nTerminal=false\nCategories=Utility;\nStartupWMClass=PenguTEX.Desktop\n')
print(f'Installed application-menu entry: {entry}')
PY
