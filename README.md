# PenguTEX

A Linux-first texture optimizer for Skyrim Special Edition modlists. PenguTEX reads
Fluorine and Mod Organizer 2 profiles directly, explains proposed changes, and writes
a separate texture override mod. It does not need Wine or a running game.

Development starts with format-preserving DDS mip reduction, followed by a native
desktop workflow and material-aware processing. The [roadmap](docs/ROADMAP.md)
distinguishes implementation from planned work and in-game validation.

- Resolve winning textures, including loaded BSAs and loose files.
- Prefer existing lower mip levels to avoid an additional compression pass.
- Preserve formats, color space and shader channels by default.
- Review proposed changes, protection rules and estimated payload reduction.
- Resume verified work and publish an independently removable output mod.
- Use the same engine, presets and validation from the desktop and CLI.

See the [design](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md),
[validation strategy](docs/VALIDATION.md) and [contribution guide](CONTRIBUTING.md).

GPL-3.0-only. See [LICENSE](LICENSE) and [provenance](docs/PROVENANCE.md).
Generated textures retain the rights of the installed game and mod assets.
