# PenguTEX 0.1.0-alpha.6

Replacement output now comes exclusively from original textures. Reference outputs
supply comparison metadata; carrying their files into a new output is no longer
supported. Legacy carry-forward policies and saved plans are rejected. The desktop
disables the retired preference with a notice and requires a fresh analysis.

- Complete partial mip chains with an explicit `CompleteMipChain` rule and declared
  channel semantics. Retain available original blocks and generate only missing levels.
- Recover an underreported mip count when an original BC texture's exact payload
  matches a complete standard chain. This is opt-in; unrelated malformed data stays rejected.
- Record original containers, hashes and recovered counts in manifests. Distinguish
  copying, completion, full rebuilding and header repair; keep cache operations separate.
- Reject marked optimizer outputs as direct folder sources while allowing comparison.
- Report legacy mixed outputs as `OriginalsOnly: false`. CLI verification returns
  exit 3 for them even if file-integrity checks pass. They remain readable for safe replacement.
- Keep missing originals, unsupported layouts and incomplete coverage visible. No
  disabled-mod assets are automatically imported, and no profile is changed.

Existing alpha.4/alpha.5 outputs containing reference copies are mixed outputs;
this release does not convert them in place. Keep a working output until a newly
analyzed originals-only candidate passes the replacement check and controlled game
validation. Use unmounted original Data for a fresh profile scan.

Quality caps remain color 2048 and normal/material/height 1024. Saved numeric limits
remain unchanged. Completion/rebuilding still requires explicit semantics; filename
suffixes alone do not establish shader meaning. Material-assisted rule discovery,
other malformed-layout recovery, HDR rebuilding and physical GPU acceleration
remain separate work. This release does not establish a universal VRAM/FPS gain.

Validation includes synthetic engine/desktop tests, CLI workflows, independent
Pillow decoding of 20 DDS fixtures and all 79 mip levels, native GUI startup, and
portable/AppImage checks on supported Linux environments. Packages include source,
dependency notices and checksums.
