# Windows and Linux packages

Download a package and `SHA256SUMS` from the same
[GitHub release](https://github.com/opteron-x86/PenguTEX/releases). Packages include
self-contained .NET desktop/CLI applications, so no system .NET SDK is required.
These are x64 alpha builds. See the [testing guide](ALPHA-TESTING.md) for source
setup, known limits and feedback. PenguTEX itself does not run through Wine or MO2.

## Windows

Download `PenguTEX-0.1.0-alpha.10-win-x64.zip`, use **Extract All**, then open
`win-x64/PenguTEX.Desktop.exe`. Extract the entire package; the executable needs
the libraries beside it. `PenguTEX.exe` is the CLI, for example in PowerShell:

```powershell
.\win-x64\PenguTEX.exe --version
Get-FileHash .\PenguTEX-0.1.0-alpha.10-win-x64.zip -Algorithm SHA256
```

Compare that hash with the ZIP's entry in `SHA256SUMS`. The alpha executables are
unsigned. No installer or administrator privileges are needed. Preferences use
`%LOCALAPPDATA%\PenguTEX\settings.json`; the default work cache is
`%LOCALAPPDATA%\PenguTEX\Cache`. A custom cache or `--settings` location still works.
To uninstall, remove the extracted application folder. Settings, cache and generated
mods are separate and are not deleted automatically.

Native Windows CI uses Windows Server 2025 x64. It runs the engine and rendered UI
tests, then extracts the ZIP into a path containing spaces and Unicode and checks
CLI generation/verification and native GUI startup. Windows desktop editions,
drivers and in-game modlists still need alpha feedback; CI is not gameplay testing.

## Linux

Extract `PenguTEX-0.1.0-alpha.10-linux-x64.tar.gz`, then run
`linux-x64/PenguTEX.Desktop` or `linux-x64/PenguTEX --help`. Move it to its final
location before optionally running `bash linux-x64/install-desktop.sh`. That
explicitly installs a per-user menu entry. To uninstall, remove the portable
directory and `pengutex.desktop` under `$XDG_DATA_HOME/applications` (normally
`~/.local/share/applications`). Preferences and cache are separate XDG folders
and are not removed automatically.

For AppImage, mark it executable and launch it. `--cli` selects the CLI. If mounting
AppImages through FUSE is unavailable, use:

```sh
./PenguTEX-0.1.0-alpha.10-x86_64.AppImage --appimage-extract-and-run
./PenguTEX-0.1.0-alpha.10-x86_64.AppImage --appimage-extract-and-run --cli --version
```

Use `sha256sum` on downloaded files and compare their entries in `SHA256SUMS`.
If all release files are present, `sha256sum -c SHA256SUMS` checks them together.
CI checks extracted portable and AppImage CLI/GUI startup on Ubuntu 24.04, Debian 13
and Fedora 43. This is Linux x64/glibc support, not a promise for every distribution,
ARM, musl, GPU driver or desktop backend. Native X11 startup is tested through Xvfb;
Wayland sessions can use XWayland. Runtime libraries include fontconfig, X11/ICE/SM,
ICU, OpenSSL and standard graphics/runtime libraries.

## Build and release

Build with .NET 10 and Python 3. Linux packaging also uses tar, curl and AppStream:

```sh
bash tools/test.sh
bash tools/build.sh
bash tools/package-appimage.sh
bash tools/package-release.sh  # requires committed source
```

On Windows, from PowerShell:

```powershell
dotnet test PenguTEX.slnx -c Release
python tools/build-windows.py
```

`PENGUTEX_DOTNET` can select a private SDK; `NUGET_PACKAGES` can select a package
cache. `python tools/build-windows.py --cross-compile` can prepare a Windows package
on Linux, but does not execute or validate Windows binaries. Releases require the
native Windows job as well as Linux package checks.

Generated build/package files live in ignored `.tools`, `.artifacts`, `bin`, `obj`
and `dist` directories. The AppImage packager verifies its pinned tool checksum
and extracts that tool without mounting it. Tagged releases must match
`Directory.Build.props`; publication waits for all Windows and Linux checks.
The release job combines each platform's checksums into a single `SHA256SUMS`.

Release artifacts include the exact committed source, GPL license, project guides,
dependency inventory and bundled license texts. No game/mod assets or VRAMr files
are included. Distribution does not grant rights to redistribute generated game/mod
textures.
