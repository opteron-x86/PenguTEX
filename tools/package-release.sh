#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
version="$(python3 tools/version.py)"
if [[ -n "$(git status --porcelain)" ]]; then
    echo 'Commit all source changes before packaging a release.' >&2; exit 1
fi
git archive --format=tar.gz --prefix="PenguTEX-$version/" --output="dist/PenguTEX-$version-source.tar.gz" HEAD
cp RELEASE-NOTES.md dist/RELEASE-NOTES.md
cd dist
sha256sum "PenguTEX-$version-linux-x64.tar.gz" "PenguTEX-$version-source.tar.gz" "PenguTEX-$version-x86_64.AppImage" > SHA256SUMS
