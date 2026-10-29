# In-game acceptance protocol (M5, not yet completed)

Automated tests establish file/workflow correctness. They do not establish that a
particular modlist's materials look correct or that performance improves. Keep issues
[#14](https://github.com/opteron-x86/PenguTEX/issues/14) and
[#15](https://github.com/opteron-x86/PenguTEX/issues/15) open until their evidence exists.

## Controlled comparison

1. Record game/modlist/profile version, shader mods and presets, display resolution,
   graphics settings, CPU/GPU/VRAM, RAM, driver, kernel, Proton and telemetry tool.
2. Choose a fixed save, weather/time, camera locations and movement route covering
   outdoors, interiors, towns and a transition that typically stutters. Keep LOD and
   grass outputs identical across texture comparisons.
3. Run a baseline with other settings fixed and PenguTEX output disabled. Capture
   frame times, reported VRAM usage and matching lossless screenshots. Separate a
   cold-start run from warmed repeated runs; record how each was prepared.
4. Enable one verified PenguTEX output, changing only that mod's priority/activation.
   Record application revision, policy hash, manifest and excluded/unsupported files.
5. Repeat each route at least three times for both conditions. Compare median, p95
   and p99 frame times, unusually long frames, and observed VRAM telemetry. Record
   run length and sample counts; do not select only the fastest run.
6. Inspect near/mid/far texture transitions and oblique surfaces. Include landscapes,
   rocks, architecture, faces/skin, armor, hair/foliage, transparent effects, normal
   maps with gloss alpha and installed complex/PBR materials. Review masks/normals
   as well as RGB. Note shimmer, seams, halos, changed gloss and missing detail.
7. Repeat CPU rebuild cases separately from mip-copy cases. Record explicit semantic
   choices; do not attribute all changes to resolution alone.

Use output-enabled/disabled comparisons instead of overwriting originals. A report
should describe regressions and unresolved cases, not just aggregate size reduction.
If a default rule repeatedly damages a material family, add a regression fixture or
protection rule before changing that default.

## Evidence record

Attach a concise environment description, routes/saves (where redistributable),
settings/policy, redacted manifest/report, per-run telemetry CSVs, matched screenshots
and observed defects to the tracking issue. Do not publish proprietary texture files
or private filesystem paths. Report measured results with their limitations; texture
payload totals are a separate number from resident VRAM.

## Optional GPU evaluation

The current application has no GPU encoder. Before adding one, define supported
formats/channel semantics, fallback behavior and cancellation. Compare complete
decode/filter/transfer/encode/readback/validation jobs with the CPU backend, using
equivalent quality and the same outputs. Run on physical AMD, Intel and NVIDIA devices
chosen for supported distributions. Software Vulkan/CI success alone cannot establish
physical-device compatibility or justify selecting the GPU automatically.
