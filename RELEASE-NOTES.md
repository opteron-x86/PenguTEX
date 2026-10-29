# PenguTEX 0.1.0-alpha.1

First Linux desktop and CLI alpha for Skyrim SE/AE texture optimization.

- Native MO2/Fluorine profile and loose-folder analysis, including activated BSA members.
- Explainable policies and protections, searchable review, RGB/alpha channel previews.
- Format-preserving compressed-mip reduction; explicit material-aware CPU mip rebuilding.
- Verified content cache, resumable work, cancellation and separate output mods.
- Portable Linux x64 and AppImage packages, source, license notices and SHA-256 checksums.

Use original, unmounted Data for profile scans. A merged Fluorine Data mount is
refused as an original baseline. Signed BC4/BC5 can be mip-copied but not decoded or
CPU-rebuilt. CPU rebuilding is explicit and limited to 16 million source pixels;
cube maps, arrays, volumes and unsupported semantic cases remain unchanged.

This alpha has automated engine/desktop/package checks. In-game appearance, resident
VRAM/frame-time improvements and physical GPU acceleration are not yet validated.
Generated textures retain the rights of their source assets. Install the result as
a separate mod and inspect representative scenes before adopting it in a modlist.
