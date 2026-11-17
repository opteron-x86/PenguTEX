#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
python3 tools/build-native.py
export PENGUTEX_REQUIRE_NATIVE_BC7=1
version="$(python3 tools/version.py)"
payload="$PWD/dist/linux-x64"
rm -rf -- "$payload"
mkdir -p "$payload"
for project in PenguTEX.Cli PenguTEX.Desktop; do
    "$DOTNET" publish "src/$project" -c Release -r linux-x64 --self-contained true \
        -o "$payload" --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
done
cp LICENSE README.md CHANGELOG.md "$payload/"
printf '%s\n' "$version" > "$payload/VERSION"
cp -a docs "$payload/"
cp packaging/pengutex.svg "$payload/"
cp tools/install-desktop.sh "$payload/"
chmod +x "$payload/install-desktop.sh"
python3 tools/collect-licenses.py
python3 tools/cli-smoke.py "$payload/PenguTEX"
tar -czf "dist/PenguTEX-$version-linux-x64.tar.gz" -C dist linux-x64
