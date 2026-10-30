# Fresh runs and material evidence

A fresh run starts with the selected original texture folder or enabled MO2/Fluorine
profile. No previous optimized output or historical path list defines completion.
Marked PenguTEX/VRAMr outputs are excluded as original sources. Inactive mods and
archives stay inactive. Removing a previous output does not remove a prerequisite.

New presets apply category limits to ordinary characters and landscapes and enable
material-aware mip completion and validated DDS recovery. [Input recovery](INPUT-RECOVERY.md) describes the accepted layouts and archive bounds. Generated LOD,
interface, cubemap, array, volume and shader resources remain deliberately protected. Their contents are not claimed as assessed. Saved numeric limits and explicit
protection choices are preserved; old policies do not silently gain automatic
completion. The desktop comparison checkbox defaults off, including when loading
old preferences containing a reference path. Enable it explicitly for a comparison.

Coverage describes the selected originals: above-limit dimensions, missing levels,
invalid/unsupported DDS files, deliberate protections and unreadable material meshes.
The CLI `--require-complete true` refuses a build with unresolved coverage. Protected
resources remain listed as deliberate exclusions; successful coverage is not proof
of visual quality or measured resident VRAM.

## Binding resolution

Mesh paths use the same original root list, enabled plugin/archive activation,
archive order and loose-file priority as textures. The metadata reader supports
Skyrim SE and LE NIF block tables and ordinary triangle shape lighting properties.
Each proposed completion retains representative mesh/container, shape, shader,
texture-slot, shader flags and alpha evidence. Review these details and the source /
proposed preview in the desktop before building. Explicit path processing rules
can override inferred semantics; explicit protections still take precedence.

Known slots cover diffuse color, tangent/model normals, parallax height, environment
mask data, and ordinary glow. Declared TruePBR materials additionally identify
emissive, displacement and packed RMAOS slots. RMAOS channels are filtered
independently in linear space, without channel rearrangement. Feature textures,
effect shaders, unsupported shapes, orphaned sets, conflicting roles and unbound
paths need explicit rules. Filenames alone never authorize pixel filtering.

Normal filtering renormalizes vectors and averages ancillary alpha; unsigned BC5
uses XY reconstruction only for tangent normals. sRGB normal/data declarations,
signed channels, unsupported precision and swizzles are not silently reinterpreted.
Color uses linear-light filtering. Standard straight-alpha blends use alpha-weighted
color without coverage scaling. Cutout alpha uses the material's greater/greater-equal
threshold; BC1 output explicitly encodes the resulting binary mask. Coverage is approximate in tiny mips. Animated, externally controlled,
vertex-dependent or nonstandard alpha tests remain unresolved.

Available original mip levels are retained byte-for-byte. Only absent levels are
filtered/encoded, preserving the DDS format. Source hashes, semantics, threshold and
binding examples appear in manifests. Mesh fingerprints are rechecked before work
and publication; changes require analysis again. A malformed mesh is diagnosed,
and any readable texture-set paths are retained as unknown uses. Unreadable meshes
prevent a complete-coverage claim.

NIF evidence cannot describe all runtime shader behavior, plugin texture swaps or
external material overrides. This is a bounded inference system with visible
exceptions, not an assertion that every asset in every modlist is understood.
Gameplay acceptance remains tracked separately.

## Format sources and provenance

The block-table reader is adapted from PenguLOD's
`src/AnvilLOD.Meshes/Nif/NifFile.cs` at
`db8a7303bf3514eca171b608d259e32bc1731561` (GPL-3.0-only), with namespace/documentation
changes. It is compiled into PenguTEX; no neighboring checkout is required.
Material and alpha layouts follow [NIF XML](https://github.com/niftools/nifxml).
TruePBR identification and slots follow
[TruePBR loading](https://github.com/community-shaders/skyrim-community-shaders/blob/main/src/TruePBR.cpp)
and [material declarations](https://github.com/community-shaders/skyrim-community-shaders/blob/main/src/TruePBR/BSLightingShaderMaterialPBR.h).
`kMenuScreen` is bit 55 of the combined shader flags (second word, bit 23), as
specified by [CommonLibSSE-NG](https://github.com/CharmedBaryon/CommonLibSSE-NG/blob/main/include/RE/B/BSShaderProperty.h).
All committed test assets are synthetic metadata and pixels.
