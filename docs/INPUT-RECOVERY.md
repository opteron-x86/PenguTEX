# Input recovery and archive boundaries

DDS layout and archive storage are validated separately. A correct texture header
cannot authorize reading beyond its archive member, and a valid archive cannot
make unexplained DDS payload bytes safe to discard.

## Recognized DDS recovery

`RecoverMipCounts` accepts an underreported compressed mip count only when the
complete file length exactly matches the standard full chain. It changes the
header and retains all existing blocks.

`RecoverMipRecords` additionally recognizes a narrow legacy BC1/DXT1 layout: one
DDS top level followed by a 16-byte little-endian width/height/row-pitch/slice-size
record before every remaining level, ending with exactly five zero bytes. The
original header must declare one level. The complete stored length must match the
calculated full chain plus every descriptor and footer. Each dimension, pitch and
slice length is checked before output publication; unknown trailers, truncated
records and contradictions are rejected. The output is canonical DDS with the
original mip blocks, with only oversized levels removed when requested.

New presets enable both validated recoveries. Saved policies retain their explicit
settings. CLI switches are `--recover-mip-counts` and `--recover-mip-records`, each
accepting `true` or `false`. The desktop exposes them separately. Record recovery
supports streaming archive inputs and previewing retained mip levels. Explicit
pixel rebuilding of a framed source requires canonicalization first.

Manifests record the original source hash, declared mip count, recognized record
layout and `RecoverMipRecords` operation. Cache entries are independently verified.
The hash covers the unmodified source member, including its framing. Originals are
never rewritten. Every generated output must pass strict canonical DDS inspection.

## BSA validation

The reader checks Skyrim BSA 104/105 folder and filename tables, counts, metadata
bounds, physical member offsets, embedded-name prefixes and stored sizes. Unreferenced
bytes inside the declared filename-table region are reported as metadata padding;
they cannot alter member names or payload boundaries, and no texture bytes are
removed. Duplicate
or overlapping payload ranges and out-of-file sizes produce archive-specific
errors. A malformed member can remain an explicit invalid source while unrelated
members retain their priority; an invalid archive index blocks a complete source
inventory.

Compressed input streams are bounded before decoding. SSE LZ4 frame structure,
block boundaries, optional content length and exact frame endpoint are checked.
Decoded streams enforce the declared output length. BSA 104 uses bounded zlib.
SSE's equal-size raw fallback is retained. Reading a DDS can never borrow bytes
from the following archive member. Member-table changes require analysis again.

Unexplained bytes within a correctly bounded decoded DDS remain a DDS error.
There is no generic trailing-byte trim, reference-copy fallback or attempt to
repair corrupt compression by guessing boundaries. Restore/repack a malformed
archive or provide a validated original conversion for those exceptions.

Tests use synthetic multi-member archives, compression frames, truncation,
overlap, size contradictions and descriptor faults. A separate Pillow decoder
checks recovered canonical DDS levels; game acceptance remains a separate check.
