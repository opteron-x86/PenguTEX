# Changelog

Notable changes to PenguTEX. Each release section becomes its GitHub release notes and
its Nexus Mods changelog. Releases before 0.1.0-alpha.11 are described on the
[releases page](https://github.com/opteron-x86/PenguTEX/releases).

## [Unreleased]

## [0.1.0-alpha.11] - 2026-11-17

Build your output again: more textures are completed, protections changed and the work cache has a new layout.

- Added mip completion for textures on shaders with the Remappable Textures flag, which covers most meshes.
- Added a native BC7 encoder to the Windows and Linux packages; BC7 mip completion is much faster.
- Added `prune-cache` and a Clean work cache button to remove old cache files.
- Added `--analyze-workers`; textures and meshes are inspected in parallel.
- Added a warning when PenguLOD output is enabled during a scan; its textures are left unchanged.
- Changed the work cache to keep only rebuilt and completed textures. Run `prune-cache` once to remove the old files.
- Changed cap rules to keep generated LOD, interface and shader textures protected unless the rule sets `OverrideGeneratedProtection`.
- Changed `analyze --report` to print a summary instead of the full plan.
- Changed builds to read each file fewer times and to recheck sources once.
- Fixed textures in archives that store identical files once being reported as invalid.
- Fixed wrongly coloured blocks in completed BC1, BC2 and BC3 mip levels, mostly in normal maps.
- Fixed mip completion processing one texture at a time.
- Fixed builds on Windows failing with "Access to the path is denied".
- Fixed the desktop showing raw errors for invalid numbers, and slow searching in large plans.
- Removed the staging marker file from published outputs.
