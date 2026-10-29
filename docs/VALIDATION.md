# Validation

Use generated fixtures: rectangular/small mip tails, legacy/DX10 headers, BC1–BC7,
signed/sRGB formats, invalid lengths/counts, cube/array/volume flags and overflow
dimensions. Compare retained mip bytes exactly and inspect output independently.

Profile fixtures cover enabled/disabled mods/plugins, reversed modlist priority,
INI archives, duplicate archives/members, loose priority, Overwrite, case collisions,
hidden files, Wine paths and optimizer-output exclusion. Real profile scans are
supplementary and read-only.

Jobs cover content changes with unchanged size/mtime, corrupted cache, cancellation,
retry, concurrency, missing sources, interrupted publication, stale output, overlap,
path traversal and symlinks. No incomplete output is reported as complete.

Rendered desktop checks exercise controls and services. X11 smoke tests open packaged
apps with isolated settings. Extracted CLI packages run analyze → build → verify.

Re-encoding checks independent decoding, color ramps, normal angular error, packed
channels and opacity coverage. Synthetic metrics do not prove universal appearance.
Inspect representative landscapes, faces, hair/foliage, armor and PBR/complex maps.

Measure scan/I/O/process/validation/publication separately, cold and warm runs,
workers, memory and cache hits. Compare complete GPU/CPU jobs, with encoder settings
and quality differences recorded. For game comparisons hold modlist/settings/driver,
save/route/weather/camera fixed and repeat. Record frame-time percentiles, stutters,
VRAM telemetry and screenshots. Disk totals do not establish FPS or resident VRAM gains.
