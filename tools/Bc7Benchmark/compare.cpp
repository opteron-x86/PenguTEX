#include "bc7e.h"
#include "basisu_transcoder.h"
#include "basisu_transcoder_internal.h"
#include <cstdint>
#include <cstring>
#include <mutex>
#include <algorithm>
#include "../../native/bc7/preserve_alpha.h"
extern "C" int encode(int backend, const uint8_t* rgba, int width, int height, uint8_t* output) {
    static std::once_flag init;
    std::call_once(init, [] { ispc::bc7e_compress_block_init(); basist::basisu_transcoder_init(); });
    if (!rgba || !output || width < 1 || height < 1 || backend < 1 || backend > 4) return 1;
    ispc::bc7e_compress_block_params params;
    if (backend == 1) ispc::bc7e_compress_block_params_init_basic(&params, false);
    else ispc::bc7e_compress_block_params_init_fast(&params, false);
    alignas(32) uint32_t pixels[64 * 16];
    alignas(32) uint64_t blocks[64 * 2];
    int nx = (width + 3) / 4, ny = (height + 3) / 4;
    for (int base = 0; base < nx * ny; base += 64) {
        int n = std::min(64, nx * ny - base);
        for (int b = 0; b < n; b++) {
            int bx = (base + b) % nx, by = (base + b) / nx;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                std::memcpy(pixels + b * 16 + y * 4 + x, rgba + (size_t(std::min(by * 4 + y, height - 1)) * width + std::min(bx * 4 + x, width - 1)) * 4, 4);
        }
        if (backend <= 2) ispc::bc7e_compress_blocks(n, blocks, pixels, &params);
        else for (int b = 0; b < n; b++) {
            uint32_t flags = backend == 3 ? basist::bc7f::cPackBC7FlagDefault : basist::bc7f::cPackBC7FlagDefaultPartiallyAnalytical;
            basist::bc7f::fast_pack_bc7_auto_rgba(reinterpret_cast<uint8_t*>(blocks + b * 2), reinterpret_cast<const basist::color_rgba*>(pixels + b * 16), flags);
        }
        for (int b = 0; b < n; b++) preserve_constant_alpha(reinterpret_cast<uint8_t*>(blocks + b * 2), pixels + b * 16);
        std::memcpy(output + size_t(base) * 16, blocks, n * 16);
    }
    return 0;
}
