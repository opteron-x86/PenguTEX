# Linux distributions

The Linux x64 portable archive contains self-contained .NET desktop/CLI applications;
no system .NET SDK or Wine is required. Extract it, then run `linux-x64/PenguTEX.Desktop`
or `linux-x64/PenguTEX --help`. Move it to its final location before optionally running
`bash linux-x64/install-desktop.sh`. That explicitly installs a per-user menu entry.
To uninstall, remove the portable directory and `pengutex.desktop` under
`$XDG_DATA_HOME/applications` (normally `~/.local/share/applications`). Preferences
and cache are separate XDG folders and are not removed automatically.

For AppImage, mark it executable and launch it. `--cli` selects the CLI. If mounting
AppImages through FUSE is unavailable, use:

```sh
./PenguTEX-0.1.0-alpha.3-x86_64.AppImage --appimage-extract-and-run
./PenguTEX-0.1.0-alpha.3-x86_64.AppImage --appimage-extract-and-run --cli --version
```

Compare downloads with `sha256sum -c SHA256SUMS`. Release artifacts include the exact
committed source, GPL license, project guides, dependency inventory and bundled
license texts. No game/mod assets or VRAMr files are included. Distribution does not
grant redistribution rights for generated game/mod textures.

CI checks extracted portable and AppImage CLI/GUI startup on Ubuntu 24.04, Debian 13
and Fedora 43. This is Linux x64/glibc support, not a promise for every distribution,
ARM, musl, GPU driver or desktop backend. Native X11 startup is tested through Xvfb;
Wayland sessions can use their XWayland support. The current runtime needs standard
fontconfig, X11/ICE/SM, ICU, OpenSSL and graphics/runtime libraries.

Build with .NET 10, Python 3, tar, curl and AppStream tools:

```sh
bash tools/test.sh
bash tools/build.sh
bash tools/package-appimage.sh
bash tools/package-release.sh  # requires committed source
```

`PENGUTEX_DOTNET` can select a private SDK; `NUGET_PACKAGES` can select a package cache.
All generated build/package files live in ignored `.tools`, `.artifacts`, `bin`,
`obj` and `dist` directories. The AppImage packager verifies its pinned tool checksum
and extracts that tool without mounting it. Tagged releases must match the version
in `Directory.Build.props`; release publication waits for all compatibility jobs.
