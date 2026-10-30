# Application design

## Product and workflow

PenguTEX is an offline Linux texture optimizer with a native desktop and CLI. Initial
game support targets Skyrim Special Edition / Anniversary Edition. A folder source
supports mod authors. VR, Windows packages and other games need separate validation.
The program does not modify plugins, game INIs or load order.

The workflow is **Source → Analyze → Review → Build → Validate → Install**. Choose a
profile/folder and preset, review changes and exceptions, and produce a separate mod.
Installation is explicit in the user's mod manager. Report successful, unchanged,
protected, unsupported and failed files separately. Cancellation preserves published
output and resumable work.

```mermaid
flowchart LR
  Source[Profile or folder] --> Resolve[Winning asset catalog]
  Resolve --> Inspect[DDS inspection]
  Inspect --> Plan[Policy and change plan]
  Plan --> Review[Desktop or JSON review]
  Review --> Process[Mip copy or CPU conversion]
  Process --> Validate[Validation]
  Validate --> Publish[Output mod and manifest]
```

The .NET 10 engine is UI-independent; Avalonia supplies the desktop. Initial CPU
processing requires no Wine or GPU driver. Future GPU adapters must declare supported
formats, dimensions and alpha/data semantics, pass correctness comparisons, and show
complete-job speed improvements before automatic selection.

No neighboring PenguLOD checkout is required. Reuse narrow audited components with
exact source revisions and notices. Extract a common versioned library only after
the shared contract is stable.

## Asset resolution

Read instance settings, profile modlist, enabled plugins, load order and applicable
INI archives directly. Resolve Wine drives without Wine. Game paths are case
insensitive; host paths retain their actual spelling. Report same-layer case
collisions rather than choose enumeration order.

Load archives activated by INIs/enabled plugins, not every installed BSA. Resolve
duplicate archive filenames through mod priority, archive members through archive
order, and loose files above archived files. Include Overwrite, respect `.mohidden`,
and report inaccessible sources/missing required archives. Do not fall back to an
older asset when the winning one is unreadable.

Recognize PenguTEX manifests and VRAMr output markers and exclude them from a fresh
optimization baseline. List exclusions in reports; do not guess from generic names
containing “output.” Protect generated LOD/atlases, interface and shader resources.

## DDS processing

Validate magic, header sizes/flags, dimensions, surface type, format, mip count and
payload bounds with checked arithmetic. Transform ordinary 2D BC1/2/3/4/5/6H/7
textures and defined uncompressed layouts, including sRGB and signed variants for mip copying. Report cube maps,
arrays, volumes and unsupported formats without flattening or guessing.
The [DDS capability matrix](DDS-FORMATS.md) separates copying from decoding/rebuilding.

Select an existing mip at/below the maximum dimension, retain aspect ratio and all
smaller levels, update width/height/count/top-level size/flags, and copy compressed
blocks unchanged. This reduces resolution; it is not lossless relative to the
original largest image. Existing mip quality is retained rather than repaired.

CPU re-encoding is explicit. Filter color in its appropriate color space; process
normal directions independently of gloss/data alpha. Opacity coverage only applies
to opacity. Packed masks stay linear. Never discard RGB because alpha contains zero
or convert BC1 to BC7 merely to label it optimized. Unsupported semantic cases stay
unchanged with reasons. Independently reopen output before accepting it.

## Policy and review

Versioned JSON presets contain maximum dimensions, category rules, exclusions and
path overrides. Named presets are starting points, not hardware promises. Default
protections cover large landscapes/rocks/dragons, faces, generated LOD, interface and
unusual surfaces. Known path/suffix conventions yield a visible classification and
reason; user overrides take precedence. Later NIF/material inference needs separate
acceptance work. Ambiguity never enables automatic channel-dropping conversions.

Plans contain virtual path, winning source, format/dimensions/mips, category,
operation, target dimensions, payload estimates and reason, plus settings/source-set
fingerprints. Filter by status, category, mod and path. Settings changes invalidate
the reviewed plan. Separate texture payload, output disk size and measured VRAM:
installed texture totals do not predict resident VRAM or FPS.

## Jobs, cache and output

Bound workers and decoded-memory use. Read loose files directly and extract selected
archive entries only. Cache by source-content hash, settings/schema and encoder
identity. Same-size/mtime changes must invalidate output. Verify cached hashes and
headers. Persist per-item state and timings; interrupted/failed jobs are resumable.

Stage on the destination filesystem. Reject source overlap, unsafe relative paths,
symlink escapes, conflicting jobs and foreign output directories. Publish complete
validated manifests only. Replace only recognized PenguTEX output, preserve prior
output throughout generation, and recover interrupted directory swaps. Replace the
owned output tree to remove stale generated files; never edit original mods or
hard-link mutable output to sources/cache.

Manifests record provenance/source hashes, operations, output hashes, policy/software
versions and status. Local detailed reports can contain paths; sharing should support
redaction. Do not upload local diagnostics automatically.

## Frontends and distribution

Desktop: source/profile setup, preset editor, analysis table, texture inspection,
progress/cancel and final installation/result view. Use XDG preferences, actionable
errors and background work. CLI: inspect/analyze/build/verify, JSON reports and
documented exit codes, independent of desktop preferences.

Ship self-contained Linux x64 portable packages first, then AppImage. Check extracted
packages on Ubuntu, Debian and Fedora. Include guides, licenses, corresponding
source, version and checksums. CI uses synthetic fixtures and native X11 startup.

PenguLOD handoff uses a versioned manifest or normal mod priority. Keep ownership of
generated LOD separate; explicitly choose original versus optimized full-texture
inputs and include that choice in generation fingerprints.
