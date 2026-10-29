# Contributing

Use a focused branch from `master` and a PR linked to a milestone issue. Each issue
has observable acceptance criteria. Keep completed implementation separate from
in-game and physical-device validation. Merge after applicable CI checks pass.

The stack is .NET 10 and Avalonia. Desktop and CLI call the same services. Use
synthetic DDS/archive/profile fixtures in tests. Do not commit proprietary assets,
personal profiles, absolute local paths, credentials or investigation/session logs.
Never overwrite original textures or auto-enable test outputs in a real modlist.

Changes to precedence, DDS processing, cache reuse and publication need regression
coverage, including failure/interruption. Rendered UI and actual native GUI startup
are distinct checks. Describe actual validation and limits in PRs. Release tags
require tested packages, source, dependency notices and checksums.

Layout: `src/PenguTEX.Core` for contracts, DDS, policy and jobs;
`src/PenguTEX.Assets` for profiles and archives; `src/PenguTEX.Cli` and
`src/PenguTEX.Desktop` for frontends; `tests` for fixtures; `tools` for build/test/package.

Track scope in public issues and design documentation. Do not add assistant/session
instruction files to release sources.
