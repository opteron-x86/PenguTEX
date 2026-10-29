# PenguTEX

A Linux-first texture optimizer for Skyrim Special Edition modlists. PenguTEX reads
Fluorine and Mod Organizer 2 profiles directly, explains proposed changes, and writes
a separate texture override mod. It does not need Wine or a running game.

The native desktop and CLI analyze profiles/folders, reduce existing DDS mip chains,
reuse verified cached work and publish removable output mods. The desktop adds
editable policies, a searchable review table and before/after channel inspection.
Explicit per-path rules can also [rebuild mips on the CPU](docs/CPU-PROCESSING.md)
with declared color/normal/data semantics. The [roadmap](docs/ROADMAP.md) distinguishes
implementation from planned work and in-game validation.

- Resolve winning textures, including loaded BSAs and loose files.
- Prefer existing lower mip levels to avoid an additional compression pass.
- Preserve formats, color space and shader channels by default.
- Review proposed changes, protection rules and estimated payload reduction.
- Resume verified work and publish an independently removable output mod.
- Use the same engine, presets and validation from the desktop and CLI.

Run the desktop with .NET 10: `dotnet run --project src/PenguTEX.Desktop -c Release`.
Build the CLI: `dotnet build src/PenguTEX.Cli -c Release`. Run regression tests
with `bash tools/test.sh`. See [CLI commands and source requirements](docs/CLI.md),
the [desktop guide](docs/DESKTOP.md), [design](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md),
[validation strategy](docs/VALIDATION.md) and [contribution guide](CONTRIBUTING.md).

GPL-3.0-only. See [LICENSE](LICENSE) and [provenance](docs/PROVENANCE.md).
Generated textures retain the rights of the installed game and mod assets.
