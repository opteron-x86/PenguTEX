#!/usr/bin/env bash
# Build a self-contained macOS arm64 package: dist/osx-arm64 (CLI + desktop), dist/PenguTEX.app, and a zip.
# The app is ad-hoc signed only (no Developer ID, not notarized). A copy built on this Mac opens normally. A
# downloaded copy is quarantined: on macOS 15 (Sequoia) and later, open it once, then allow it in System Settings >
# Privacy & Security > "Open Anyway" (right-click > Open no longer bypasses Gatekeeper there); on older macOS,
# right-click > Open. Removing quarantine also works: `xattr -dr com.apple.quarantine PenguTEX.app`.
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/env.sh"
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || { echo "build-macos.sh needs macOS on Apple Silicon" >&2; exit 1; }
python3 tools/build-native.py
export PENGUTEX_REQUIRE_NATIVE_BC7=1
version="$(python3 tools/version.py)"
rid=osx-arm64
payload="$PWD/dist/$rid"
app="$PWD/dist/PenguTEX.app"
rm -rf -- "$payload" "$app"
mkdir -p "$payload"
for project in PenguTEX.Cli PenguTEX.Desktop; do
    "$DOTNET" publish "src/$project" -c Release -r "$rid" --self-contained true \
        -o "$payload" --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
done
[[ -f "$payload/libpengutex_bc7.dylib" ]] || { echo "native BC7 encoder missing from $payload" >&2; exit 1; }
# Every Mach-O in the payload must load on the advertised LSMinimumSystemVersion.
min_os=12.0
while IFS= read -r -d '' binary; do
    file -b "$binary" | grep -q '^Mach-O' || continue
    minos="$(vtool -show-build "$binary" 2>/dev/null | awk '/minos/ {print $2; exit}')"
    [[ -z "$minos" ]] && continue
    if [[ "$(printf '%s\n%s\n' "$minos" "$min_os" | sort -V | tail -1)" != "$min_os" ]]; then
        echo "$(basename "$binary") needs macOS $minos, above the bundle floor $min_os" >&2; exit 1
    fi
done < <(find "$payload" -type f -perm -u+x -print0)
cp LICENSE README.md CHANGELOG.md "$payload/"
printf '%s\n' "$version" > "$payload/VERSION"
cp -a docs "$payload/"
cp -a packaging/licenses "$payload/licenses"
python3 tools/cli-smoke.py "$payload/PenguTEX"

# .app bundle. The apphost needs the runtime, managed assemblies, native libraries and its .json files beside it in
# Contents/MacOS; static documents go to Contents/Resources so only code sits in the code folder.
macos_dir="$app/Contents/MacOS" resources="$app/Contents/Resources"
mkdir -p "$macos_dir" "$resources"
cp -a "$payload/." "$macos_dir/"
for item in docs licenses LICENSE README.md CHANGELOG.md VERSION; do mv "$macos_dir/$item" "$resources/"; done
# Icon: sips renders the SVG on stock macOS (rsvg-convert is not needed). Every size renders from the vector source.
iconset="$PWD/dist/pengutex.iconset"
rm -rf -- "$iconset"; mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -s format png -z "$size" "$size" packaging/pengutex.svg --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -s format png -z $((size * 2)) $((size * 2)) packaging/pengutex.svg --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$resources/pengutex.icns"
rm -rf -- "$iconset"
icon_key="<key>CFBundleIconFile</key><string>pengutex</string>"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>PenguTEX</string>
<key>CFBundleDisplayName</key><string>PenguTEX</string>
<key>CFBundleIdentifier</key><string>io.github.opteron_x86.PenguTEX</string>
<key>CFBundleExecutable</key><string>PenguTEX.Desktop</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>${version%%-*}</string>
<key>CFBundleVersion</key><string>${version}</string>
<key>LSMinimumSystemVersion</key><string>${min_os}</string>
<key>LSArchitecturePriority</key><array><string>arm64</string></array>
<key>NSHighResolutionCapable</key><true/>
${icon_key}
</dict></plist>
PLIST
# Sign nested code inside-out, then the bundle, which seals Resources. Everything in Contents/MacOS counts as code
# (strict verification rejects an unsigned managed .dll or .json there), so each file is signed explicitly: a Mach-O
# gets an embedded signature, any other file an extended-attribute one. --deep is avoided.
while IFS= read -r -d '' binary; do
    [[ "$binary" == "$macos_dir/PenguTEX.Desktop" ]] && continue
    codesign --force --sign - --timestamp=none "$binary" 2>/dev/null
done < <(find "$macos_dir" -type f -print0)
codesign --force --sign - --timestamp=none "$app"
codesign --verify --deep --strict "$app"
zip="$PWD/dist/PenguTEX-$version-$rid.zip"
rm -f -- "$zip"
(cd dist && ditto -c -k --keepParent PenguTEX.app "$zip")
echo "macOS package ready: $app"
echo "                     $zip"
