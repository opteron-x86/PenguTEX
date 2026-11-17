#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <thread>
#include <vector>
#include <array>
#include <iostream>
extern "C" int pengutex_bc7_encode(const uint8_t*, size_t, uint32_t, uint32_t, uint8_t*, size_t, int);
static void check(bool condition) { if (!condition) std::abort(); }
int main()
{
    // Exercise simultaneous first use, edge padding, batch boundaries and output bounds.
    std::array<std::vector<uint8_t>, 8> results;
    std::vector<std::thread> threads;
    for (int t = 0; t < 8; t++) threads.emplace_back([&, t] {
        for (uint32_t w : {1u, 2u, 3u, 4u, 7u, 255u, 256u, 257u})
        for (uint32_t rows = 1; rows <= 4; rows++) {
            std::vector<uint8_t> pixels(w * rows * 4);
            for (size_t i = 0; i < pixels.size(); i++) pixels[i] = uint8_t(i * 41 + i / 17);
            size_t bytes = ((w + 3) / 4) * 16;
            std::vector<uint8_t> guarded(bytes + 32, 0xCD);
            check(pengutex_bc7_encode(pixels.data(), pixels.size(), w, rows, guarded.data() + 16, bytes, 0) == 0);
            check(std::all_of(guarded.begin(), guarded.begin() + 16, [](uint8_t b) { return b == 0xCD; }));
            check(std::all_of(guarded.end() - 16, guarded.end(), [](uint8_t b) { return b == 0xCD; }));
            for (size_t i = 16; i < bytes + 16; i += 16) check(guarded[i] != 0);
            results[t].insert(results[t].end(), guarded.begin(), guarded.end());
            auto before = guarded;
            check(pengutex_bc7_encode(pixels.data(), pixels.size() - 1, w, rows, guarded.data() + 16, bytes, 0) == 1);
            check(pengutex_bc7_encode(pixels.data(), pixels.size(), w, rows, guarded.data() + 16, bytes - 1, 0) == 1);
            check(pengutex_bc7_encode(pixels.data(), pixels.size(), w, 5, guarded.data() + 16, bytes, 0) == 1);
            check(pengutex_bc7_encode(pixels.data(), pixels.size(), w, rows, guarded.data() + 16, bytes, 2) == 1);
            check(guarded == before);
        }
    });
    for (auto& thread : threads) thread.join();
    for (const auto& result : results) check(result == results[0]);
    check(pengutex_bc7_encode(nullptr, 0, 0, 0, nullptr, 0, 0) == 1);
    std::cout << "BC7 C ABI bounds, padding and concurrent initialization passed\n";
}
