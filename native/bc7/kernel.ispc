// Keep the upstream file unmodified and expose the actual dispatched SIMD width
// so caches distinguish SSE2 and AVX2 output (their rounding can differ).
#include "vendor/bc7e.ispc"
export uniform int pengutex_bc7_lanes() { return programCount; }
