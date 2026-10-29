# PenguLOD handoff and output ownership

PenguTEX outputs ordinary loose DDS overrides plus `pengutex-manifest.json` (schema
1). They can be used through normal mod priority. No private PenguLOD build or
automatic modification of its settings is required.

Keep a deliberate input choice for each LOD build:

- **Original textures:** disable the PenguTEX output while resolving full textures for
  LOD generation. Record that choice with the PenguLOD preset/build notes.
- **Optimized textures:** enable the verified PenguTEX mod above its original texture
  providers before resolving inputs. Record the PenguTEX policy hash and output
  hashes with the LOD build. A future API adapter can include those in its fingerprint.

Generated terrain/object/tree LOD and atlases stay owned by PenguLOD or their original
generator. PenguTEX protects LOD/interface/shader paths by default. Do not remove those
protections simply to lower an aggregate size estimate. Rebuild LOD with its generator
when its texture inputs or settings change.

This is a documented workflow and versioned file contract; it does not claim that
PenguLOD currently imports PenguTEX manifests into its cache automatically. Any future
adapter must have its own issue, compatibility tests and versioned acceptance.

The output manifest contains product/schema/software version, creation time, policy
hash, per-file virtual path/source mod, source SHA-256, output SHA-256, dimensions,
removed mip count, file size, operation, semantic and processor identity. It also
counts unchanged, protected, unsupported and invalid textures. Physical source paths
are omitted from this distributable manifest; local analysis reports include them.

Consumers must reject unsupported schema versions, normalize safe relative paths,
verify hashes, and distinguish `MipCopy` from `Rebuild`. The CLI `verify` command checks
the owned tree, file hashes and DDS structures. Manifest presence alone is not proof
that files remain unmodified. Unknown extra files or symlinks block replacement.
Neither manifest nor payload totals establish live VRAM usage or FPS improvement.

PenguTEX excludes its recognized output mods from a fresh profile baseline to avoid
repeated downscaling/recompression. An already merged Fluorine Data mount cannot
reconstruct hidden original game assets; stop that VFS or provide original Data.
