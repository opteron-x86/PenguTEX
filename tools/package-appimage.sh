#!/usr/bin/env bash
# Based on PenguLOD's checksum-pinned packaging flow; extraction needs no FUSE.
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
version="$(python3 tools/version.py)"
test -x dist/linux-x64/PenguTEX.Desktop
mkdir -p .tools .artifacts/appimage-tool dist
tool="$PWD/.tools/appimagetool.AppImage"
if [[ ! -f "$tool" ]]; then
    curl -fL --retry 3 https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage -o "$tool"
fi
python3 - "$tool" <<'PY'
import hashlib,pathlib,sys
p=pathlib.Path(sys.argv[1])
if hashlib.sha256(p.read_bytes()).hexdigest() != 'ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0':
    raise SystemExit('AppImage tool checksum mismatch')
p.chmod(0o755)
PY
(cd .artifacts/appimage-tool && "$tool" --appimage-extract > extract.log)
offset="$("$tool" --appimage-offset)"
python3 - "$tool" "$offset" <<'PY'
import pathlib,sys
pathlib.Path('.artifacts/appimage-tool/runtime').write_bytes(pathlib.Path(sys.argv[1]).read_bytes()[:int(sys.argv[2])])
PY
appdir="$PWD/.artifacts/PenguTEX.AppDir"
rm -rf -- "$appdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/lib/PenguTEX" "$appdir/usr/share/metainfo" \
    "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/scalable/apps"
cp -a dist/linux-x64/. "$appdir/usr/lib/PenguTEX/"
cp packaging/AppRun "$appdir/AppRun"
cp packaging/io.github.opteron_x86.PenguTEX.desktop "$appdir/"
cp packaging/io.github.opteron_x86.PenguTEX.desktop "$appdir/usr/share/applications/"
cp packaging/io.github.opteron_x86.PenguTEX.metainfo.xml "$appdir/usr/share/metainfo/"
cp packaging/pengutex.svg "$appdir/"
cp packaging/pengutex.svg "$appdir/usr/share/icons/hicolor/scalable/apps/"
chmod +x "$appdir/AppRun"
ln -s ../../AppRun "$appdir/usr/bin/PenguTEX.Desktop"
appstreamcli validate-tree --no-net "$appdir"
ARCH=x86_64 .artifacts/appimage-tool/squashfs-root/AppRun --no-appstream --runtime-file .artifacts/appimage-tool/runtime \
    --mksquashfs-opt -processors --mksquashfs-opt 2 "$appdir" "$PWD/dist/PenguTEX-$version-x86_64.AppImage"
