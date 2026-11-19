#!/usr/bin/env python3
"""Read synthetic rebuilt DDS files with Pillow's separate native DDS/BC decoder.

Generate the fixtures with PENGUTEX_ORACLE_FIXTURES=/path bash tools/test.sh.
The files contain known solid channel values, including color under zero alpha.
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
            decoded = list(image.convert("RGBA").get_flattened_data())
            if path.name == "cutout-bc1.dds":
                assert all(pixel[3] in (0, 255) for pixel in decoded)
                if mip == 0: assert sum(pixel[3] == 255 for pixel in decoded) == 32
                if mip == 1: assert [pixel[3] for pixel in decoded] == [0, 255, 255, 255] * 4
                offset += size
                continue
            for pixel in decoded:
                channels = [] if kind == 1 else [0] if kind == 4 else [0, 1] if kind == 5 else [0, 1, 2]
                for channel in channels:
                    assert abs(pixel[channel] - (64, 128, 192)[channel]) <= (0 if kind == 0 else 8), (path, mip, pixel)
                if kind not in (4, 5):
                    assert pixel[3] == 0, (path, mip, pixel)
        offset += size
    assert offset == len(data), (path, offset, len(data))
    return mips


paths = sorted(Path(sys.argv[1]).glob("*.dds"))
assert len(paths) == 22, f"Expected 22 synthetic fixtures, found {len(paths)}"
levels = sum(verify(path) for path in paths)
print(f"Pillow {__version__}: independently decoded {len(paths)} DDS files / {levels} mip levels; channels and lengths match.")
native = sorted((Path(sys.argv[1]) / 'bc7-native').glob('*.dds'))
assert len(native) == 112, f'Expected 112 native BC7 edge fixtures, found {len(native)}'
for path in native:
    _, width, height, alpha = path.stem.split('-')
    with Image.open(path) as image:
        image.load()
        assert image.size == (int(width), int(height))
        assert all(a == int(alpha) for a in image.convert('RGBA').getchannel('A').get_flattened_data()), path
print(f'Pillow: independently decoded {len(native)} native BC7 edge fixtures; constant alpha preserved exactly.')

# Uncompressed sources compressed to BC7: each output level against the source level one step larger.
pairs = sorted((Path(sys.argv[1]) / 'encoded').glob('*.source.dds'))
assert len(pairs) == 3, f'Expected 3 encoded fixture pairs, found {len(pairs)}'
for source_path in pairs:
    source, encoded = source_path.read_bytes(), source_path.with_name(source_path.name.replace('.source.', '.bc7.')).read_bytes()
    s = lambda at: struct.unpack_from('<I', source, at)[0]
    e = lambda at: struct.unpack_from('<I', encoded, at)[0]
    assert encoded[84:88] == b'DX10' and e(128) in (98, 99) and e(128) == (99 if s(128) == 29 and source[84:88] == b'DX10' else 98)
    sw, sh, smips, bits = s(16), s(12), s(28), (32 if source[84:88] == b'DX10' else s(88))
    soffset, stride = (148 if source[84:88] == b'DX10' else 128), bits // 8
    ew, eh, emips, eoffset = e(16), e(12), e(28), 148
    assert (ew, eh, emips) == (sw // 2, sh // 2, smips - 1)
    levels = []
    for mip in range(smips):
        w, h = max(1, sw >> mip), max(1, sh >> mip)
        levels.append((w, h, source[soffset:soffset + w * h * stride])); soffset += w * h * stride
    for mip in range(emips):
        w, h = max(1, ew >> mip), max(1, eh >> mip); size = ((w + 3) // 4) * ((h + 3) // 4) * 16
        header = bytearray(encoded[:148])
        for at, value in {8: 0x81007, 12: h, 16: w, 20: size, 28: 1, 108: 0x1000}.items():
            struct.pack_into('<I', header, at, value)
        with Image.open(io.BytesIO(header + encoded[eoffset:eoffset + size])) as image:
            decoded = list(image.convert('RGBA').get_flattened_data())
        lw, lh, raw = levels[mip + 1]
        assert (lw, lh) == (w, h)
        errors = []
        for i, pixel in enumerate(decoded):
            expected = list(raw[i * stride:(i + 1) * stride]) + ([255] if stride == 3 else [])
            errors += [abs(pixel[c] - expected[c]) for c in range(4)]
        assert max(errors) <= 24 and sum(errors) / len(errors) <= 4, (source_path.name, mip, max(errors), sum(errors) / len(errors))
        eoffset += size
    assert eoffset == len(encoded)
print(f'Pillow: independently decoded {len(pairs)} compressed uncompressed-source fixtures; every level matches its source level.')
