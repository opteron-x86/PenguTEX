# Native BC7 encoding

PenguTEX's Linux x64 and Windows x64 packages use Binomial BC7E **Basic**, with
equal RGBA error weights, for newly generated BC7 blocks. Existing mip payloads,
filtering, DDS metadata and the other BC formats retain their existing paths.
BC7E is single-threaded SIMD code: PenguTEX owns texture concurrency, cancellation
between four-row strips, and the shared decoded-pixel budget.

The native wrapper batches up to 64 blocks using fixed stack buffers, repeats
edge texels for partial blocks, and checks all input/output lengths. Initialization
is synchronized once per process. A block with entirely zero or entirely opaque
alpha retains that value exactly: the wrapper corrects its alpha endpoints, and
where necessary its shared endpoint p-bits. In modes 6/7 this changes RGB by at
most one byte unit. Mixed-alpha blocks remain lossy and require coverage checks.

The original BC7E source is unmodified and pinned to
`b9438627eef73a1157e84201b6fa6eb2ffd6d9f0` of
[bc7enc_rdo](https://github.com/richgel999/bc7enc_rdo).
See `native/bc7/vendor/UPSTREAM.txt` and its Apache-2.0 license. ISPC 1.32.0 builds
SSE2 and AVX2 variants with runtime dispatch; fast-math is not enabled. No ISPC
compiler or GPU driver is needed on the end user's machine. Results are tested
for repeatability within a build/CPU; bit identity across compiler/ISA combinations
is not promised. The actual dispatched ISA is included in the cache identity.

## Build and packaging

Install CMake, a C++17 compiler (MSVC on Windows), Python 3, and the existing .NET
SDK prerequisite. Run `python3 tools/build-native.py` (`python` on Windows) before
a direct `dotnet build/test`. The build, test and packaging scripts do this
automatically. The script downloads and SHA-256-verifies a pinned ISPC archive,
then builds and tests the C ABI. `--ispc PATH` or `PENGUTEX_ISPC` can select an
existing ISPC 1.32.0 installation. Native outputs are under `native/bin/<rid>` and
are copied transitively into .NET build/publish directories.

Native loading failure falls back to BCnEncoder Balanced for source builds without
the native library. `PENGUTEX_REQUIRE_NATIVE_BC7=1` makes automatic fallback an
error; release packaging/CI use it to catch missing native binaries. Unsupported
ABIs and encoding errors fail the job instead of switching encoder mid-output.

For diagnostics, `PENGUTEX_BC7_ENCODER=managed|basic|fast|auto` selects a backend
when a processor is created. This is a developer override, not a new preset.
Explicit processor constructors used by benchmarks can request a backend directly.
Backend, upstream revision, compiler version, profile and wrapper version enter
the processor identity and therefore the generated-output cache recipe. Managed
fallback retains the previous identity. Switching native settings regenerates
all CPU-generated entries for this processor, including non-BC7 formats;
retained-mip copying has its existing identity.

## Reproduce the comparison

The comparison harness is separate from the shipped application. It compares
BCnEncoder Balanced (0), BC7E Basic (1), BC7E Fast (2), bc7f analytical (3), and
bc7f partially analytical (4). All native candidates use the same constant-alpha
correction. RDO is disabled. The comparison's bc7f code comes from Basis Universal
revision `073eb6a0a34a23d77e75e54f41dcd9653a52da83`, supplied as an external checkout:

```sh
python3 tools/Bc7Benchmark/build-comparison.py --basis-source /path/to/basis_universal
dotnet build tools/Bc7Benchmark -c Release
dotnet tools/Bc7Benchmark/bin/Release/net10.0/Bc7Benchmark.dll prepare /path/to/plan.json /tmp/bc7-corpus
dotnet tools/Bc7Benchmark/bin/Release/net10.0/Bc7Benchmark.dll measure /tmp/bc7-corpus .artifacts/bc7/libcompare.so /tmp/bc7-results 3
python3 tools/Bc7Benchmark/quality.py /tmp/bc7-corpus /tmp/bc7-results
dotnet tools/Bc7Benchmark/bin/Release/net10.0/Bc7Benchmark.dll snapshot /path/to/plan.json /tmp/bc7-corpus /tmp/bc7-snapshot
dotnet tools/Bc7Benchmark/bin/Release/net10.0/Bc7Benchmark.dll workflow /tmp/bc7-snapshot /tmp/bc7-basic-16 Basic 16
```

The plan must come from a complete source analysis. Preparation samples up to eight
textures per semantic, spread across sizes, and captures centered tiles up to
256×256 from the first generated mip. It also generates deterministic edge and
channel stress cases. Per-sample hashes establish identical filtered inputs.
Measurements include native block packing and use warmup plus repeated timed
batches. They exclude filtering, disk I/O and validation. The snapshot workflow
instead processes complete original textures, verifies output and retained mip
bytes, and checks empty-cache versus warm-cache behavior. Use a fresh destination
for each run. OS caches are not flushed; `/tmp` may be memory-backed.

The quality script requires Pillow and NumPy. It independently decodes raw BC7
blocks, scores each channel, measures normal angular error and alpha coverage,
and generates lossless review sheets. Endpoint-error counts on mixed-alpha blocks
are diagnostic, not an assertion that lossy BC7 preserves every individual alpha.
Corpora and review images may contain private game/mod assets: keep them local.
The existing mip-copy benchmark does not measure encoder throughput.

Automated and image-level checks do not replace the in-game acceptance protocol
in [GAME-VALIDATION.md](GAME-VALIDATION.md), especially at mip transitions.
