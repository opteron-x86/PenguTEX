# DDS capability matrix

Inspection and existing-mip copying support ordinary single-surface 2D textures:

| Storage | Inspect / mip copy | CPU rebuild / preview |
| --- | --- | --- |
| BC1/2/3/4/5/7 | Yes, including existing signed/sRGB/premultiplied data | Existing byte-channel capabilities; signed and premultiplied rebuilds excluded |
| BC6H unsigned/signed float | Yes; HDR blocks remain unchanged | Not yet available; requires HDR filtering/display |
| Legacy 8/16/24/32-bit RGB, luminance and alpha masks | Yes, contiguous nonoverlapping masks | Unsigned channels of at most eight bits; explicit semantics for rebuilding |
| Explicit DXGI uncompressed formats listed below | Yes | Byte layouts 28/29, 49, 61, 65, 85/86/87/88/91/93/115; other precision remains copy-only |
| Cubemaps, arrays and volumes | Explicitly excluded | Explicitly excluded |

DXGI uncompressed support: 2, 10, 11, 16, 24, 26, 28, 29, 31, 34, 35, 37, 41,
49, 51, 54, 56, 58, 61, 63, 65, 67, 85, 86, 87, 88, 91, 93 and 115. These are
typed, fixed-size formats, not typeless/depth/video/planar resources. Copying a mip
does not decode or change its precision, alpha declaration or color space.

Uncompressed rows may be tightly packed or DWORD-aligned. Complete file length must
uniquely justify the layout (or both layouts must be identical). Padding stays with
each retained row; the output header records row pitch, not compressed linear size.
Other alignments and mismatched payloads are rejected rather than guessed. Legacy
FourCC numeric format aliases and unsupported flag combinations remain excluded.

The parser treats the format and storage layout independently of preview/rebuild
capabilities. Adding a copyable format must not route it through a byte decoder that
would lose HDR, signed channels or higher precision. A completed output does not
establish parity with an existing optimizer; compare effective winners and test in
game before replacing one.

References: [Microsoft DDS programming guide](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide),
[DXGI formats](https://learn.microsoft.com/en-us/windows/win32/api/dxgiformat/ne-dxgiformat-dxgi_format).
