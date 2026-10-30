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
    run("build", "--folder", root / "input", "--reference", root / "output",
        "--carry-reference", "true", "--output", root / "carried", "--cache", root / "cache", expected=1)
    assert not (root / "carried").exists()
    assert candidate["Manifest"]["OriginalsOnly"]
    assert candidate["Manifest"]["Files"][0]["SourceContainer"] == str(source / "example.dds")
    legacy_manifest = root / "candidate" / "pengutex-manifest.json"
    legacy = json.loads(legacy_manifest.read_text())
    legacy["Files"][0]["Operation"] = "ReferenceCopy"
    legacy_manifest.write_text(json.dumps(legacy))
    assert not run("verify", root / "candidate", expected=3)["OriginalsOnly"]
    partial = bytearray(target.read_bytes()[:148 + 64]); struct.pack_into("<I", partial, 28, 1)
    (root / "partial").mkdir(); (root / "partial/example.dds").write_bytes(partial)
    (root / "reference").mkdir(); (root / "reference/example.dds").write_bytes(target.read_bytes())
    fresh_gap = run("analyze", "--folder", root / "partial", "--require-complete", "true", expected=3)
    assert fresh_gap["Comparison"] is None and fresh_gap["Coverage"]["IncompleteMips"] == 1
    assert fresh_gap["Source"]["ReferenceFolder"] is None
    mip_gap = run("analyze", "--folder", root / "partial", "--reference", root / "reference", "--require-parity", "true", expected=3)
    assert mip_gap["Comparison"]["IncompleteMipCount"] == 1
    run("analyze", "--folder", root / "partial", "--reference", root / "reference",
        "--carry-reference", "true", expected=1)
    completion_policy = root / "completion.json"
    completion_policy.write_text(json.dumps({"Rules": [{"Pattern": "*", "Maximum": 8,
        "CompleteMipChain": True, "Semantic": "LinearData"}]}))
    completed = run("build", "--folder", root / "partial", "--reference", root / "reference",
                    "--policy", completion_policy, "--require-parity", "true",
                    "--output", root / "completed", "--cache", root / "cache")
    assert completed["Manifest"]["OriginalsOnly"] and not completed["Manifest"]["Comparison"]["HasUnresolved"]
    assert completed["Manifest"]["Files"][0]["Operation"] == "CompleteMips"
    assert (root / "completed/textures/example.dds").read_bytes()[148:148 + 64] == partial[148:]
    damaged_header = bytearray(original); struct.pack_into("<I", damaged_header, 28, 2)
    (root / "damaged").mkdir(); (root / "damaged/example.dds").write_bytes(damaged_header)
    assert run("analyze", "--folder", root / "damaged", "--recover-mip-counts", "false")["Items"][0]["Decision"] == "Invalid"
    repaired = run("build", "--folder", root / "damaged", "--recover-mip-counts", "true",
                   "--output", root / "repaired", "--cache", root / "cache")
    assert repaired["Manifest"]["Files"][0]["Operation"] == "RepairHeader"
    assert repaired["Manifest"]["Files"][0]["DeclaredSourceMipCount"] == 2
    assert (root / "repaired/textures/example.dds").read_bytes()[148:] == original[148:]
    # Legacy BC1 with bounded per-mip descriptors; every payload byte is synthetic.
    record_header = bytearray(header[:128]); record_header[84:88] = b"DXT1"
    struct.pack_into("<I", record_header, 20, 128); struct.pack_into("<I", record_header, 28, 1)
    framed = bytes(record_header); blocks = b""
    for level in range(5):
        dimension = max(1, 16 >> level); pitch = ((dimension + 3) // 4) * 8
        data = bytes([level + 1]) * (pitch * ((dimension + 3) // 4)); blocks += data
        if level: framed += struct.pack("<IIII", dimension, dimension, pitch, len(data))
        framed += data
    framed += bytes(5)
    (root / "records").mkdir(); (root / "records/a.dds").write_bytes(framed)
    assert run("analyze", "--folder", root / "records", "--recover-mip-records", "false")["Items"][0]["Decision"] == "Invalid"
    recovered = run("build", "--folder", root / "records", "--output", root / "recovered-records", "--cache", root / "cache")
    assert recovered["Manifest"]["OriginalsOnly"] and recovered["Manifest"]["Files"][0]["Operation"] == "RecoverMipRecords"
    assert (root / "recovered-records/textures/a.dds").read_bytes()[128:] == blocks
    target.write_bytes(target.read_bytes()[:-1])
    run("verify", root / "output", expected=1)
    (root / "foreign").mkdir()
    (root / "foreign" / "keep.txt").write_text("keep")
    rejected = subprocess.run([*command, "build", "--folder", str(root / "missing-source"),
                               "--output", str(root / "foreign"), "--cache", str(root / "cache")], capture_output=True, text=True)
    assert rejected.returncode == 1 and "Choose an empty folder" in rejected.stderr
    assert (root / "foreign" / "keep.txt").read_text() == "keep"
print("CLI analyze/build/verify/cache/source-preservation smoke passed.")
