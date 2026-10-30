# Replacing a previous optimizer output

Reducing original textures does not necessarily match a previous optimizer. An
omitted override exposes the original winner again. The replacement check compares
the effective textures with and without the previous output, including unchanged
and protected files, instead of comparing only generated files.

Use original sources and select the previous output as Reference. In a profile,
PenguTEX infers its mod name from an installed mod folder; for an external copy,
enter Replace mod to identify its profile slot. The mod may be disabled. The selected
reference is excluded from original inputs even without an optimizer marker.
Loose textures above that slot and Overwrite remain unchanged in both scenarios;
archive textures are below loose overrides. A folder comparison assumes the output
is above the folder's loose sources and does not model a full game profile.

The candidate must be installed at the compared priority. Disable other optimizer
outputs for the comparison run; recognized optimizer outputs are excluded from
original inputs, so leaving one enabled changes the modeled setup. Exclude any
unrecognized outputs explicitly. Do not use merged/mounted game Data as originals.

## Explicit limits

Maximum dimensions for newly selected presets:

| Preset | Color | Normal | Material | Height |
| --- | ---: | ---: | ---: | ---: |
| Quality | 2048 | 1024 | 1024 | 1024 |
| Balanced | 2048 | 1024 | 512 | 512 |
| Compact | 1024 | 512 | 512 | 256 |

These are dimension policies, not universal quality or performance guarantees.
Saved numeric settings stay unchanged; select a preset explicitly to adopt its new
limits. Missing HeightMaximum in older policies uses MaterialMaximum. Landscape,
character, generated LOD, interface and shader protections still apply by default.

Fit reference selects a retained mip no larger than the reference in either dimension
or total texture payload, and requires its chain to reach at least as small a final
mip in both dimensions. A smaller base texture with fewer required tail levels is
still an incomplete replacement. It preserves the source's format and channels; when the
reference uses denser compression, fitting may require a smaller resolution. Review
those visual tradeoffs. Fitting protected reference textures is a separate opt-in for
landscape and character paths already covered by the reference. It does not bypass
explicit Protect rules or generated LOD/interface/shader protection. Missing mips,
unsupported layouts and explicit protections remain visible gaps.

## Carry-forward compatibility mode

Carry unresolved reference files unchanged is optional and off by default. It can
retain a valid previous file when the original is larger, unreadable or missing.
This makes the candidate self-contained without inventing channel semantics or
discarding paths found only in the previous output. Each retained file is copied
byte for byte, source/output hashes are recorded, and it is labeled ReferenceCopy
in the texture review and ReferenceRetained in the comparison. A separate carried
count remains visible in the report and manifest. These files are not native
PenguTEX optimizations; keep their provenance and relevant mod redistribution terms.

This option never imports an unreadable reference file or bypasses a protection
rule. Such gaps still fail the required-parity gate. Use `--carry-reference true`
in the CLI. Native coverage can improve later by restoring missing originals or
adding reviewed rebuild rules with known semantics. Turning carry-forward off
shows those remaining native gaps again.

## Acceptance

The report includes format, dimensions, mip count, payload and decision reason.
Larger dimensions or payload, incomplete mip coverage, missing originals and unreadable DDS layouts require
review. A changed reference path set, file length or DDS layout invalidates the
comparison before publication. This metadata fingerprint is not a pixel-content
hash or an assertion of visual equivalence. Reference and source folders are protected
from output/cache overlap.

The CLI can enforce this file-level gate:

```sh
PenguTEX build --instance /games/modlist --profile Example \
  --reference /games/modlist/mods/PreviousOutput --preset quality \
  --fit-reference true --fit-protected true --require-parity true \
  --output /generated/Candidate --cache /cache/PenguTEX --workers 2
```

Exit 3 means larger or unassessed paths remain; it publishes no candidate. The desktop
allows a partial candidate for inspection and keeps its unresolved counts visible.
The manifest retains the comparison, including local paths; treat exported reports
as local data unless deliberately sharing a sanitized copy.

After file verification, follow [game validation](GAME-VALIDATION.md): the same scene,
settings, load order, frame generation state and warm-up; record frame times and
resident VRAM; inspect representative materials. Payload totals are not resident
VRAM. Keep the working previous output until that comparison passes. PenguTEX does
not enable the candidate or change the game profile.
