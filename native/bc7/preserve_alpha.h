#pragma once
#include <cstdint>

// BC7 modes 4/5 can rotate alpha into a color endpoint; modes 6/7 share
// endpoint p-bits across RGBA. Preserve a block whose input alpha is entirely
// 0 or entirely 255. Adjusting shared p-bits changes RGB by at most one unit.
inline void preserve_constant_alpha(uint8_t* block, const uint32_t* pixels)
{
    const uint32_t alpha = pixels[0] >> 24;
    if (alpha != 0 && alpha != 255) return;
    for (int i = 1; i < 16; i++) if ((pixels[i] >> 24) != alpha) return;
    int mode = 0;
    while (mode < 8 && !(block[0] & (1 << mode))) mode++;
    auto fill = [&](int start, int count) {
        for (int bit = start; bit < start + count; bit++) {
            const uint8_t mask = uint8_t(1 << (bit % 8));
            if (alpha) block[bit / 8] |= mask;
            else block[bit / 8] &= uint8_t(~mask);
        }
    };
    if (mode == 4 || mode == 5) {
        int rotation = (block[0] >> (mode + 1)) & 3;
        int color_bits = mode == 4 ? 5 : 7, alpha_bits = mode == 4 ? 6 : 8;
        if (rotation) fill(8 + (rotation - 1) * 2 * color_bits, 2 * color_bits);
        else fill(8 + 6 * color_bits, 2 * alpha_bits);
    } else if (mode == 6) {
        fill(49, 14); fill(63, 2);
    } else if (mode == 7) {
        fill(74, 20); fill(94, 4);
    }
}
