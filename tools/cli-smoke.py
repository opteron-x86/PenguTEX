#!/usr/bin/env python3
"""Run a complete CLI workflow using generated DDS data only."""
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile

command = sys.argv[1:]
if not command:
    raise SystemExit("usage: cli-smoke.py /path/to/PenguTEX (or dotnet /path/to/PenguTEX.dll)")


def run(*args, expected=0, progress=()):
    result = subprocess.run([*command, *map(str, args)], capture_output=True, text=True)
    if result.returncode != expected:
        raise RuntimeError(f"{args[0]} returned {result.returncode}: {result.stderr}")
    for message in progress:
        assert message in result.stderr, f"Missing progress {message!r}: {result.stderr}"
    return json.loads(result.stdout) if result.stdout.strip() else None


with tempfile.TemporaryDirectory(prefix="pengutex-cli-") as temporary:
    root = Path(temporary)
    source = root / "input" / "textures" / "armor"
    source.mkdir(parents=True)
    header = bytearray(148)
    header[:4] = b"DDS "
    for offset, value in {4: 124, 8: 0xA1007, 12: 16, 16: 16, 20: 256, 28: 5,
                          76: 32, 80: 4, 108: 0x401008, 128: 98, 132: 3, 140: 1}.items():
        struct.pack_into("<I", header, offset, value)
    header[84:88] = b"DX10"
    payload = b"".join(bytes([64]) + bytes(15) for _ in range(23))
    original = bytes(header) + payload
    (source / "example.dds").write_bytes(original)
    plan = run("analyze", "--folder", root / "input", "--max", 8)
    assert plan["ChangedCount"] == 1
    (root / "output").mkdir()  # File-picker users commonly create the destination first.
    built = run("build", "--folder", root / "input", "--max", 8,
                "--output", root / "output", "--cache", root / "cache",
                progress=("Build 1/1:", "Verify 0/1:", "Verify 1/1:", "Publish 0/1:", "Publish 1/1:"))
    assert built["Files"] == 1 and built["Reused"] == 0
    target = root / "output" / "textures" / "armor" / "example.dds"
    assert target.read_bytes()[148:] == payload[256:]
    assert run("inspect", target)["Width"] == 8
    assert len(run("verify", root / "output", progress=("Verify 0/1:", "Verify 1/1:"))["Files"]) == 1
    assert run("build", "--folder", root / "input", "--max", 8,
               "--output", root / "output", "--cache", root / "cache", progress=("Cleanup 0/1:", "Cleanup 1/1:"))["Reused"] == 1
    assert hashlib.sha256((source / "example.dds").read_bytes()).digest() == hashlib.sha256(original).digest()
    comparison = run("analyze", "--folder", root / "input", "--max", 16, "--reference", root / "output", "--require-parity", "true", expected=3)
    assert comparison["Comparison"]["LargerCount"] == 1
    blocked = run("build", "--folder", root / "input", "--max", 16, "--reference", root / "output",
                  "--require-parity", "true", "--output", root / "blocked", "--cache", root / "cache", expected=3)
    assert blocked["HasUnresolved"] and not (root / "blocked").exists()
    candidate = run("build", "--folder", root / "input", "--max", 16, "--reference", root / "output",
                    "--fit-reference", "true", "--require-parity", "true", "--output", root / "candidate", "--cache", root / "cache")
    assert not candidate["Manifest"]["Comparison"]["HasUnresolved"]
    assert target.read_bytes() == (root / "candidate" / "textures" / "armor" / "example.dds").read_bytes()
    carried = run("build", "--folder", root / "input", "--max", 16, "--reference", root / "output",
                  "--carry-reference", "true", "--require-parity", "true", "--output", root / "carried", "--cache", root / "cache")
    assert carried["Manifest"]["Comparison"]["CarriedCount"] == 1
    assert carried["Manifest"]["Files"][0]["Operation"] == "ReferenceCopy"
    assert target.read_bytes() == (root / "carried" / "textures" / "armor" / "example.dds").read_bytes()
    partial = bytearray(target.read_bytes()[:148 + 64]); struct.pack_into("<I", partial, 28, 1)
    (root / "partial").mkdir(); (root / "partial/example.dds").write_bytes(partial)
    (root / "reference").mkdir(); (root / "reference/example.dds").write_bytes(target.read_bytes())
    mip_gap = run("analyze", "--folder", root / "partial", "--reference", root / "reference", "--require-parity", "true", expected=3)
    assert mip_gap["Comparison"]["IncompleteMipCount"] == 1
    mip_fixed = run("analyze", "--folder", root / "partial", "--reference", root / "reference", "--carry-reference", "true", "--require-parity", "true")
    assert mip_fixed["Comparison"]["CarriedCount"] == 1 and not mip_fixed["Comparison"]["HasUnresolved"]
    target.write_bytes(target.read_bytes()[:-1])
    run("verify", root / "output", expected=1)
    (root / "foreign").mkdir()
    (root / "foreign" / "keep.txt").write_text("keep")
    rejected = subprocess.run([*command, "build", "--folder", str(root / "missing-source"),
                               "--output", str(root / "foreign"), "--cache", str(root / "cache")], capture_output=True, text=True)
    assert rejected.returncode == 1 and "Choose an empty folder" in rejected.stderr
    assert (root / "foreign" / "keep.txt").read_text() == "keep"
print("CLI analyze/build/verify/cache/source-preservation smoke passed.")
