#!/usr/bin/env python3
"""Independently decode benchmark blocks with Pillow and score against filtered RGBA."""
import io
import json
import math
from pathlib import Path
import struct
import sys
import numpy as np
from PIL import Image

corpus, output = map(Path, sys.argv[1:3])
samples = json.loads((corpus / 'samples.json').read_text())
results = []
for sample in samples:
    w, h = sample['Width'], sample['Height']
    raw = (corpus / (sample['Id'] + '.rgba')).read_bytes()
    reference = np.frombuffer(raw, np.uint8).reshape(h, w, 4).astype(float)
    for backend in range(5):
        path = output / f"{sample['Id']}-{backend}.bc7"
        if not path.exists():
            continue
        blocks = path.read_bytes()
        assert len(blocks) == ((w + 3) // 4) * ((h + 3) // 4) * 16
        header = bytearray(148)
        header[:4] = b'DDS '
        for at, value in {4: 124, 8: 0x81007, 12: h, 16: w, 20: len(blocks), 28: 1,
                          76: 32, 80: 4, 108: 0x1000, 128: 98, 132: 3, 140: 1}.items():
            struct.pack_into('<I', header, at, value)
        header[84:88] = b'DX10'
        with Image.open(io.BytesIO(header + blocks)) as image:
            decoded = np.array(image.convert('RGBA')).astype(float)
        mse = np.mean((reference - decoded) ** 2, axis=(0, 1))
        psnr = [None if x == 0 else 10 * math.log10(255 ** 2 / x) for x in mse]
        endpoint_errors = int(np.sum(((reference[:, :, 3] == 0) | (reference[:, :, 3] == 255)) & (reference[:, :, 3] != decoded[:, :, 3])))
        coverage = float(abs(np.mean(reference[:, :, 3] >= sample['Cutoff']) - np.mean(decoded[:, :, 3] >= sample['Cutoff'])))
        angle_mean = angle_p99 = None
        if sample['Semantic'] == 'NormalRgb':
            a, b = reference[:, :, :3] / 127.5 - 1, decoded[:, :, :3] / 127.5 - 1
            a /= np.maximum(np.linalg.norm(a, axis=2, keepdims=True), 1e-10)
            b /= np.maximum(np.linalg.norm(b, axis=2, keepdims=True), 1e-10)
            angles = np.degrees(np.arccos(np.clip(np.sum(a * b, axis=2), -1, 1)))
            angle_mean, angle_p99 = float(np.mean(angles)), float(np.percentile(angles, 99))
        results.append(dict(Id=sample['Id'], Backend=backend, Semantic=sample['Semantic'], Source=sample['Source'],
                            Pixels=w*h, Mse=mse.tolist(), Psnr=psnr, AlphaEndpointErrors=endpoint_errors,
                            CoverageError=coverage, NormalMeanDegrees=angle_mean, NormalP99Degrees=angle_p99))
        # Lossless contact sheets are local review artifacts; source assets are never checked in.
        if w >= 32 and h >= 32:
            canvas = Image.new('RGB', (w * 3, h))
            for i, pixels in enumerate((reference[:, :, :3], decoded[:, :, :3], np.minimum(255, np.abs(reference[:, :, :3] - decoded[:, :, :3]) * 8))):
                canvas.paste(Image.fromarray(pixels.astype(np.uint8)), (w*i, 0))
            canvas.save(output / f"{sample['Id']}-{backend}.png")
(output / 'quality.json').write_text(json.dumps(results, indent=2) + '\n')
for backend in range(5):
    rows = [x for x in results if x['Backend'] == backend]
    if not rows:
        continue
    weights = np.array([x['Pixels'] for x in rows])
    mse = np.average(np.array([x['Mse'] for x in rows]), axis=0, weights=weights)
    print(backend, 'RGBA PSNR', np.round(10*np.log10(255**2/mse), 2), 'endpoint errors', sum(x['AlphaEndpointErrors'] for x in rows))
