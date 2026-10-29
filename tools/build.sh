#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
version="$(python3 tools/version.py)"
payload="$PWD/dist/linux-x64"
rm -rf -- "$payload"
mkdir -p "$payload"
for project in PenguTEX.Cli PenguTEX.Desktop; do
    "$DOTNET" publish "src/$project" -c Release -r linux-x64 --self-contained true \
        -o "$payload" --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
done
cp LICENSE README.md "$payload/"
cp -a docs "$payload/"
cp packaging/pengutex.svg "$payload/"
cp tools/install-desktop.sh "$payload/"
chmod +x "$payload/install-desktop.sh"
python3 tools/collect-licenses.py
python3 tools/cli-smoke.py "$payload/PenguTEX"
tar -czf "dist/PenguTEX-$version-linux-x64.tar.gz" -C dist linux-x64
