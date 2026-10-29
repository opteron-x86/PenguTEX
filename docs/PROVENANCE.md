# Provenance

PenguTEX is GPL-3.0-only. Selected path/profile components are adapted from
[PenguLOD](https://github.com/opteron-x86/PenguLOD), based on
[AnvilLOD](https://github.com/CageTV/AnvilLOD). Record exact revisions, files and
changes below. No neighboring checkout is required at runtime.

Dependencies include Avalonia, Mutagen and BCnEncoder.NET. Package versions are pinned;
distributions include dependency notices. No VRAMr code, binaries, exclusion
databases, or proprietary game/mod assets are redistributed.

References: [DDS layout](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-file-layout-for-textures),
[BC7](https://learn.microsoft.com/en-us/windows/win32/direct3d11/bc7-format),
[BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET),
[Community Shaders](https://github.com/community-shaders/skyrim-community-shaders).

## Imported source

Profile/path/archive selection files under `src/PenguTEX.Assets/Paths`,
`Profiles`, and `SkyrimArchives.cs` are adapted from PenguLOD commit
`1eb7882ee44387bda80de237783fedce650e131d` (GPL-3.0-only), based on CageTV’s AnvilLOD.
Namespaces and subsequent robustness/diagnostic changes are local to PenguTEX.

Linux AppImage/desktop-install packaging conventions, dependency-license collection
and copied upstream license texts also derive from that PenguLOD revision. The
PenguTEX scripts simplify its workflow to the texture application. The pinned
AppImage tool/runtime is 1.9.1, SHA-256
`ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0`.
The vector application icon and application metadata are original PenguTEX assets.

Where NuGet packages omit license text, `packaging/licenses/UPSTREAM-SOURCES.json`
records the original repository, revision, file and blob used to retain that text.
Package-declared revisions are used where available; packages without revision
metadata use a recorded upstream revision. The distribution inventory also records
the package repository/revision rather than silently leaving missing metadata blank.
