# PenguTEX

A Linux-first texture optimizer for Skyrim Special Edition modlists, with native
Windows and Linux applications. PenguTEX reads original textures, explains proposed
changes, and writes a separate texture override mod. PenguTEX itself runs without
Wine or a running game.

Download the Windows x64 ZIP, Linux x64 portable archive or AppImage from
[Releases](https://github.com/opteron-x86/PenguTEX/releases). These are alpha builds;
see the [installation guide](docs/DISTRIBUTION.md), [alpha testing guide](docs/ALPHA-TESTING.md)
and [PenguLOD handoff](docs/PENGULOD-HANDOFF.md).

Profile sources support MO2 on Windows, MO2 through Wine/Proton on Linux, and Fluorine
on Linux. Choose the instance folder containing `ModOrganizer.ini`; PenguTEX reads
its files directly. Fluorine is a Linux manager, not a Windows requirement. Loose
texture folders work independently of a mod manager on either platform.

The native desktop and CLI analyze profiles/folders, reduce existing DDS mip chains,
reuse verified cached work and publish removable output mods. The desktop adds
editable policies, a searchable review table and before/after channel inspection.
Explicit per-path rules can also [rebuild mips on the CPU](docs/CPU-PROCESSING.md)
with declared color/normal/data semantics. The [roadmap](docs/ROADMAP.md) distinguishes
implementation from planned work and in-game validation.

When replacing a previous optimizer, use the [replacement check](docs/REPLACEMENT.md)
to compare effective textures at the same mod priority. It reports larger and
unassessed paths and offers explicit reference fitting. Every generated texture must
come from an original source; previous outputs supply comparison metadata only. File validation is
followed by an in-game comparison; retain the working output until that passes.

- Resolve winning textures, including loaded BSAs and loose files.
- Prefer existing lower mip levels to avoid an additional compression pass.
- Preserve formats, color space and shader channels by default.
- Review proposed changes, protection rules and estimated payload reduction.
- Resume verified work and publish an independently removable output mod.
- Use the same engine, presets and validation from the desktop and CLI.

Run the desktop with .NET 10: `dotnet run --project src/PenguTEX.Desktop -c Release`.
Build the CLI: `dotnet build src/PenguTEX.Cli -c Release`. Run regression tests
with `bash tools/test.sh` on Linux or `dotnet test PenguTEX.slnx -c Release` on Windows. See [CLI commands and source requirements](docs/CLI.md),
the [desktop guide](docs/DESKTOP.md), [design](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md),
[validation strategy](docs/VALIDATION.md) and [contribution guide](CONTRIBUTING.md).

GPL-3.0-only. See [LICENSE](LICENSE) and [provenance](docs/PROVENANCE.md).
Generated textures retain the rights of the installed game and mod assets.

Fresh runs use enabled original sources without a previous optimizer dependency. See [material discovery and coverage](docs/MATERIALS.md) for supported bindings, review and explicit exceptions.
