# PenguTEX 0.1.0-alpha.5

This release fixes the alpha.4 comparison gate: smaller files can still lack the
reference's smallest mip levels. Incomplete chains now fail strict parity, appear
explicitly in the desktop, and can be carried unchanged from the reference when
that option is selected. Re-analyze alpha.4 candidates before installing them.

Check a replacement against the previous optimizer's effective output before
installing it. Smaller files than the originals alone do not establish replacement
parity.

- Compare dimensions, formats, mip payloads, missing sources and protected paths at
  the previous output's actual profile priority. Higher-priority sources remain unchanged.
- Opt into reference fitting while retaining source formats. The CLI can refuse
  publication if larger or unassessed paths remain with `--require-parity true`.
- Optionally carry valid unresolved reference files byte for byte. These are labeled
  separately, hashed and counted; they are not claimed as native optimizations.
- Add existing-mip copying for verified raw DDS layouts and BC6H. Byte-channel raw
  textures also support preview and explicit semantic-aware rebuilding.
- Bound rebuild concurrency to one decoded texture and process codecs in strips,
  supporting up to 8192×8192 source pixels. Preview remains limited to 16 million pixels.
- Independently decode synthetic outputs with Pillow in CI alongside engine,
  rendered desktop, native startup and extracted package checks.

Newly selected Quality uses color 2048, normal/material/height 1024. Balanced uses
2048/1024/512/512; Compact uses 1024/512/512/256. Saved numeric settings remain
unchanged: select a preset explicitly to adopt its limits. Review protected paths
and format-density tradeoffs using the [replacement guide](https://github.com/opteron-x86/PenguTEX/blob/master/docs/REPLACEMENT.md).

Every completed build is a candidate requiring a controlled in-game comparison.
Payload estimates are not resident VRAM or FPS. Keep the working previous output
until appearance and performance pass. Use unmounted original Data for profile
scans and install candidates at the compared priority. PenguTEX does not change
profiles or enable output. HDR/high-precision rebuilding, cube/array/volume processing,
automatic material inference and physical GPU acceleration remain outside this alpha.

Portable Linux x64 and AppImage packages include source, dependency notices and checksums.
