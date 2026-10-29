# Opt-in CPU mip rebuilding

All presets default to copying existing compressed mip levels. Rebuilding introduces
another lossy encoding pass and can increase payload when repairing missing mips.
Choose it only for known texture sets; inspect output before installation.

Add a path rule to a JSON policy (or the desktop's custom-rule editor):

```json
{
  "Schema": 1,
  "Name": "Known materials",
  "Rules": [
    {"Pattern": "textures/mymod/*_d.dds", "Maximum": 1024, "RebuildMips": true, "Semantic": "SrgbColor"},
    {"Pattern": "textures/mymod/*_n.dds", "Maximum": 1024, "RebuildMips": true, "Semantic": "NormalRgb"},
    {"Pattern": "textures/mymod/*_p.dds", "Maximum": 512, "RebuildMips": true, "Semantic": "LinearData"}
  ]
}
```

These example suffixes are not proof of shader semantics. Verify the actual material
and channels first. Last matching processing rule wins. A rule with an explicit
Maximum also clears default path protection; a Protect rule still prevents changes.
Rules without RebuildMips use normal mip copying. Missing semantics reject the policy.

| Semantic | Filtering |
| --- | --- |
| SrgbColor | Decode RGB to linear light, filter, re-encode sRGB; alpha averages independently as data |
| LinearColor | Average RGB and alpha independently in linear space |
| LinearData | Average each packed channel independently; never alpha-mask RGB |
| NormalRgb | Average signed XYZ directions and normalize; alpha averages independently |
| NormalRg | Unsigned BC5 XY, reconstruct positive Z, average/normalize, retain XY |
| OpacitySrgb | Linear-light RGB weighted by opacity, independent alpha with approximate 0.5-threshold coverage preservation |

Legacy DDS often has no explicit color-space flag. Explicit semantics declare how
its values are interpreted; the original header/format stays unchanged. An sRGB
header cannot be combined with data/normal semantics. Premultiplied alpha, signed
BC4/BC5, swizzled normal layouts, cube/array/volume textures and unsupported formats
are not rebuilt. Existing-mip copying handles signed and premultiplied formats.

BC1/2/3/4/5/7 encoding uses BCnEncoder.NET 2.3.0 Balanced. Source compression format,
DXGI/FourCC, sRGB flag and alpha metadata are preserved. Every encoded level is
decoded before publication, then the DDS structure, payload and output hash are
verified. The content cache includes source hash, policy, semantic and encoder/filter
identity. CPU decoding is limited to 16 million source pixels and one texture per
process at a time; streaming workers may continue other files. Larger inputs remain
available to the streaming mip-copy operation.

Filtering uses area-weighted boxes, including odd image edges, and generates through
1×1. Opacity coverage is approximate after alpha quantization, especially in tail
mips. Normal conventions and alpha-test thresholds vary by material; these filters
do not establish universal in-game quality. GPU acceleration and in-game acceptance
remain separate roadmap work.

References: [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET),
[DDS alpha-mode metadata](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10).
