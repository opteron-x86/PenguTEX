#include "bc7e.h"
#include "preserve_alpha.h"
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <mutex>

#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif

// No allocations proportional to image size and no internal worker threads.
// Input is one row of BC7 blocks (1-4 pixel rows); missing edge texels repeat.
API int pengutex_bc7_abi() { return 2; }
API int pengutex_bc7_isa() { return ispc::pengutex_bc7_lanes(); }
API int pengutex_bc7_encode(const uint8_t* rgba, size_t rgba_size, uint32_t width, uint32_t rows,
    uint8_t* output, size_t output_size, int quality)
{
    if (!rgba || !output || !width || rows < 1 || rows > 4 || quality < 0 || quality > 1) return 1;
    const uint64_t count = (uint64_t(width) + 3) / 4;
    if (rgba_size != uint64_t(width) * rows * 4 || output_size != count * 16) return 1;
    try {
        static std::once_flag initialized;
        std::call_once(initialized, [] { ispc::bc7e_compress_block_init(); });
        ispc::bc7e_compress_block_params params;
        // Equal channel weights: packed data and gloss alpha are not expendable color detail.
        if (quality == 0) ispc::bc7e_compress_block_params_init_basic(&params, false);
        else ispc::bc7e_compress_block_params_init_fast(&params, false);
        alignas(32) uint32_t pixels[64 * 16];
        alignas(32) uint64_t blocks[64 * 2];
        for (uint64_t base = 0; base < count; base += 64) {
            uint32_t n = uint32_t(std::min(uint64_t(64), count - base));
            for (uint32_t b = 0; b < n; b++)
                for (uint32_t y = 0; y < 4; y++) for (uint32_t x = 0; x < 4; x++) {
                    size_t at = (uint64_t(std::min(y, rows - 1)) * width + std::min((base + b) * 4 + x, uint64_t(width - 1))) * 4;
                    std::memcpy(pixels + b * 16 + y * 4 + x, rgba + at, 4);
                }
            ispc::bc7e_compress_blocks(n, blocks, pixels, &params);
            for (uint32_t b = 0; b < n; b++)
                preserve_constant_alpha(reinterpret_cast<uint8_t*>(blocks + b * 2), pixels + b * 16);
            std::memcpy(output + base * 16, blocks, n * 16);
        }
        return 0;
    } catch (...) { return 2; } // Never propagate C++ exceptions across the C ABI.
}
