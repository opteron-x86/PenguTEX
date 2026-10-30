#!/usr/bin/env python3
"""Read synthetic rebuilt DDS files with Pillow's separate native DDS/BC decoder.

Generate the fixtures with PENGUTEX_ORACLE_FIXTURES=/path bash tools/test.sh.
The 13 files contain known solid channel values, including color under zero alpha.
No game assets are required. This is a build-time oracle, not a runtime dependency.
"""
import io
import struct
import sys
from pathlib import Path
from PIL import Image, __version__


def verify(path):
    data = path.read_bytes()
    get = lambda at: struct.unpack_from("<I", data, at)[0]
    assert data[:4] == b"DDS " and get(4) == 124 and get(76) == 32
    width, height, mips = get(16), get(12), get(28)
    assert (width, height) == ((4, 2) if path.name == "raw-rgba.dds" else (8, 8))
    assert mips == max(width, height).bit_length()
    cc = data[84:88]
    header_size = 148 if cc == b"DX10" else 128
    kind = {71: 1, 72: 1, 74: 2, 77: 3, 80: 4, 83: 5, 98: 7, 99: 7, 28: 0}[get(128)] if header_size == 148 else {
        b"DXT1": 1, b"DXT3": 2, b"DXT5": 3, b"ATI2": 5}[cc]
    offset = header_size
    for mip in range(mips):
        w, h = max(1, width >> mip), max(1, height >> mip)
        size = w * h * 4 if kind == 0 else ((w + 3) // 4) * ((h + 3) // 4) * (8 if kind in (1, 4) else 16)
        header = bytearray(data[:header_size])
        # Pillow lacks BC1/2/3 sRGB DXGI aliases. Their block layout is identical;
        # decode the payload through the UNORM alias without changing the fixture.
        if header_size == 148 and get(128) in (72, 75, 78):
            struct.pack_into("<I", header, 128, get(128) - 1)
        # Pillow opens the first mip. Present each original payload slice as a one-level DDS.
        for at, value in {8: 0x1007 | (8 if kind == 0 else 0x80000), 12: h, 16: w,
                          20: w * 4 if kind == 0 else size, 28: 1, 108: 0x1000}.items():
            struct.pack_into("<I", header, at, value)
        with Image.open(io.BytesIO(header + data[offset:offset + size])) as image:
            image.load()
            assert image.size == (w, h)
            for pixel in image.convert("RGBA").get_flattened_data():
                channels = [] if kind == 1 else [0] if kind == 4 else [0, 1] if kind == 5 else [0, 1, 2]
                for channel in channels:
                    assert abs(pixel[channel] - (64, 128, 192)[channel]) <= (0 if kind == 0 else 8), (path, mip, pixel)
                if kind not in (4, 5):
                    assert pixel[3] == 0, (path, mip, pixel)
        offset += size
    assert offset == len(data), (path, offset, len(data))
    return mips


paths = sorted(Path(sys.argv[1]).glob("*.dds"))
assert len(paths) == 13, f"Expected 13 synthetic fixtures, found {len(paths)}"
levels = sum(verify(path) for path in paths)
print(f"Pillow {__version__}: independently decoded {len(paths)} DDS files / {levels} mip levels; channels and lengths match.")
