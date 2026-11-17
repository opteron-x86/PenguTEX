# BC7 encoder evaluation — 2026-11-17

BC7E Basic was selected for native Linux/Windows x64 packages. It met the speed
target while improving normal-map angular error on the sampled real textures.
BC7E Fast and bc7f were faster, but their worst normal-map errors were larger.
No in-game acceptance or release approval is implied by these measurements.

Machine: Ryzen 9 7950X3D, 16 cores / 32 threads, Linux x64. The corpus contains
36 real BC7 mip-completion samples from the profile used for PR #63 and 20
synthetic edge/channel stress cases. Real samples total 1,937,413 pixels. Each
sample is a tile up to 256×256 from the first mip actually encoded after applying
the policy cap, with identical filtered bytes supplied to every encoder.

## Encoding comparison

| Encoder | Real-sample MP/s, one thread | Worst real normal sample: p99 angular error |
| --- | ---: | ---: |
| BCnEncoder.NET 2.3.0 Balanced | 0.027 | 9.02° |
| BC7E Basic | 2.54 | 8.37° |
| BC7E Fast | 5.84 | 14.78° |
| bc7f analytical | 60.74 | 17.44° |
| bc7f partially analytical | 36.02 | 17.38° |

Throughput is total pixels divided by the sum of per-sample median encoding
times, with warmup and three repeated timings. It includes native block packing
and the constant-alpha correction. RDO and internal multithreading are off. These
are pilot measurements: other task processes sometimes ran concurrently, and
CPU clocks/OS caches were not controlled. They establish a large difference,
not a precise portable speed multiplier. The existing PR's 0.05 MP/s observation
used different samples; this corpus includes especially slow normal/alpha cases.

Pillow independently decoded every candidate's 56 outputs. BC7E Basic improved
both average and p99 angular error for all seven nontrivial real normal samples;
the eighth was a one-pixel tail with equal results. On the eight real alpha-test
samples, its largest coverage error was 0.029 percentage points, versus 0.037
for Balanced. RGB and alpha tradeoffs vary by sample: for example, Basic's
aggregate diffuse alpha PSNR was lower than Balanced's, even while its RGB fit
improved. Per-channel results are in the [machine-readable report](benchmarks/bc7-2026-11-17.json).
This is a sampled quality gate, not proof for every material.

Review sheets cover reference, decoded output, and amplified error. Checked
normal-map detail and dark cutout/hair samples did not show gross channel changes.
The native encoder originally changed some constant alpha endpoints. A wrapper
correction now preserves entirely zero/opaque blocks; it is independently tested
on 112 small/odd-width fixtures. Mixed-alpha compression remains lossy.

## Complete mip-completion workflow

A local snapshot contains the 36 complete original textures (about 96 MiB),
with explicit rules reproducing their original planned completions. Retained
mip payloads are compared byte-for-byte. Storage is tmpfs; an empty application
cache does not mean a flushed OS cache. Each row is one pilot cold/warm pair.

| Backend | Workers | Empty cache | Warm cache |
| --- | ---: | ---: | ---: |
| BCnEncoder Balanced | 16 | 300.11 s | 0.146 s |
| BC7E Basic | 1 | 17.67 s | 0.289 s |
| BC7E Basic | 16 | 4.60 s | 0.135 s |

All warm runs reused all 36 results. Timings include decode, filtering, encoding,
I/O and output verification. Peak process memory in the harness includes its
extra retained-byte comparisons and runtime GC; it is not the decoded-pixel
reservation. The unchanged pixel-budget regression suite passes.

## Validation and remaining acceptance

- 252 engine tests and 13 desktop tests pass locally with native encoding required.
- Pillow verifies 22 existing DDS fixtures / 87 mip levels and 112 native edge fixtures.
- The native C ABI tests cover concurrent first initialization, deterministic
  results, short-buffer rejection, edge padding and batch boundaries.
- AddressSanitizer/UndefinedBehaviorSanitizer pass for the C++ wrapper tests;
  ISPC-generated objects are not sanitizer-instrumented.
- A forced SSE2 build passes the C ABI tests. SSE2 and AVX2 can produce different
  blocks, so the actual ISA is included in the cache identity.
- The Linux portable package passes CLI build/cache/source-preservation smoke tests.

Full-profile timings and Windows/packaging CI results are recorded when complete.
In-game checks remain under [GAME-VALIDATION.md](GAME-VALIDATION.md). Test outputs
are separate from original mods and are not automatically enabled in the profile.
