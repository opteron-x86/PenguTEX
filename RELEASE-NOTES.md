# PenguTEX 0.1.0-alpha.7

Fresh runs now assess enabled original sources against selected limits and mip
coverage. Previous optimizer outputs are optional comparison data, excluded from
originals, and not prerequisites. Historical reference-only paths do not define
completion. No game profile or installed texture output is changed automatically.

- Discover winning mesh bindings with the same profile/archive priorities as DDS.
  Review material provenance and proposed previews before building.
- Complete missing mips for supported lighting and declared TruePBR color, normal,
  height and packed RMAOS bindings. Preserve every available original mip block.
- Distinguish straight-alpha blending from cutout coverage at the material's actual
  supported threshold. Keep conflicting, unbound, unsupported and unreadable uses
  explicit; never guess channel swizzles or reinterpret sRGB normals.
- New presets apply limits to ordinary characters and landscapes. Generated LOD,
  interface and shader resources retain protection; saved custom choices persist.
- Report native coverage independently of optional comparison. CLI
  `--require-complete true` prevents publication with unresolved coverage.
- Recover exact-length underreported mip counts and the narrowly validated legacy
  BC1 descriptor-per-mip layout. Record source hashes and every recovery operation.
- Bound BSA member reads before decompression, validate SSE LZ4 frame endpoints,
  and reject overlaps, truncation and contradictory decoded lengths separately
  from DDS layout errors. Unknown DDS trailing bytes are never silently trimmed.
- Recheck material fingerprints before publication. Preserve source-only manifests,
  verified cache reuse and transactional output publication.

The desktop comparison checkbox is off by default even for old preferences with a
reference path. Explicit policy settings stay intact. Use unmounted original Data
for a profile scan. A working earlier output is not converted in place.

Validation: synthetic engine and rendered desktop tests; CLI workflows; independent
Pillow decoding of 21 DDS fixtures / 83 mip levels; native GUI startup and
portable/AppImage compatibility checks in CI. Runtime shader overrides, visual
quality and measured VRAM/frame-time benefits require game validation. See
[material coverage](docs/MATERIALS.md) and [input recovery](docs/INPUT-RECOVERY.md).
