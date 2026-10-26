"""Give the corrected transparent tiny bitmap control opaque white pixels."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


SOURCE_SHA = "EB40C5576230FA37CE22827FB6FB3E3FB2970CAB98A4F4916BA2A67B6D35704E"


def generate(source: Path, output: Path) -> dict:
    original = source.read_bytes()
    if hashlib.sha256(original).hexdigest().upper() != SOURCE_SHA:
        raise ValueError("expected the exact corrected sequential tiny control")
    container = GbxContainer.read(original)
    marker = struct.pack("<4I6I5I", 0x0902F000, 0x80000006, 9, 6,
                         1, 1, 1, 4, 8, 1, 0, 0, 0, 0, 0)
    body = bytearray(container.body)
    offsets = []
    start = 0
    while (offset := body.find(marker, start)) >= 0:
        pixel = offset + len(marker)
        if body[pixel:pixel + 16] != bytes(16):
            raise ValueError("original native tiny pixels are not transparent zero")
        offsets.append(pixel)
        body[pixel:pixel + 16] = struct.pack("<4f", 1, 1, 1, 1)
        start = pixel + 16
    if len(offsets) != 2:
        raise ValueError("expected exactly two tiny image nodes")
    restored = bytearray(body)
    for offset in offsets:
        restored[offset:offset + 16] = bytes(16)
    if restored != container.body:
        raise ValueError("pixel inverse failed to preserve opaque bytes")
    result = container.write(bytes(body))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "generator": "Tests/FxStandalone/create_white_tiny_bitmap_control.py",
        "source": str(source.resolve()), "sourceSha256": SOURCE_SHA,
        "output": str(output.resolve()), "outputBytes": len(result),
        "outputSha256": hashlib.sha256(result).hexdigest().upper(),
        "pixelBodyOffsets": offsets, "beforeRGBA": [0, 0, 0, 0], "afterRGBA": [1, 1, 1, 1],
        "allNonPixelBytesExactlyPreserved": True, "originalBodyExactlyRestorable": True,
        "purpose": "visible white-sprite control; original tiny pixels are fully transparent",
        "nativeVerdict": "untested; no rendering claim"
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output), indent=2))
