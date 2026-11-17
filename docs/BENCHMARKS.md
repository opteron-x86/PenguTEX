# Reproducible synthetic diagnostics

For native BC7 encoding and full mip-completion comparisons, see
[BC7-ENCODER.md](BC7-ENCODER.md) and the [2026-11-17 evaluation](BC7-BENCHMARK.md).

After `bash tools/build.sh`, run:

```sh
python3 tools/benchmark.py --binary dist/linux-x64/PenguTEX --files 32 --size 1024 --workers 2 --report .artifacts/benchmarks/synthetic.json
```

The fixture contains deterministic, distinct BC7 DDS chains and no game/mod assets.
The report records application revision, platform, dimensions, workers, whole-process
wall time and engine build time. Mip copies are not cached: the repeated run reads
and hashes every source, writes and verifies every output and publishes a new
directory again, so both runs do the same work. The OS page cache is not flushed.
Process startup dominates small fixtures.

The same run damages published output, removes one source, and verifies recovery
and source hashes. Regression tests separately cover
cancellation, conflicting writers, linked paths and interrupted directory swaps.
CI uploads the report alongside test results and rendered desktop screenshots.

Use repeated runs on a representative storage device for engineering comparisons;
record OS cache state and do not compare unrelated hardware as an algorithm result.
This fixture measures the mip-copy workflow and integrity behavior. CPU encoding
throughput, detailed phase profiling, peak RSS and real-modlist archive throughput
need separate experiments. These timings are not game FPS, stutter or VRAM results.
