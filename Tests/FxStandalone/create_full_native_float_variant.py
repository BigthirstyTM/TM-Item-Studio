"""Copy final Studio metadata/mesh/point while replacing only bitmap config."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from create_float_bitmap_controls import ORIGINAL_U01, FLOAT_U01, ORIGINAL_FLAGS2, FLOAT_FLAGS2
from gbx_container import GbxContainer


def sha(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest().upper()


def generate(source: Path, output: Path, expected_sha: str, expected_bitmaps: int) -> dict:
    original = source.read_bytes()
    if sha(original) != expected_sha.upper() or expected_bitmaps not in (1, 2):
        raise ValueError("source SHA or expected bitmap count differs from the audited final export")
    container = GbxContainer.read(original)
    if container.references:
        raise ValueError("final variant must already be a zero-external-reference inline-pixel item")
    marker = struct.pack("<3I3f4I", *ORIGINAL_U01, 0, 1, 0,
                         0, 0x09011032, 0, ORIGINAL_FLAGS2)
    replacement = struct.pack("<3I3f4I", *FLOAT_U01, 0, 1, 0,
                              0, 0x09011032, 0, FLOAT_FLAGS2)
    if container.body.count(marker) != expected_bitmaps:
        raise ValueError("final export does not contain exactly the audited DDS bitmap configuration records")
    body = bytearray(container.body)
    offsets = []
    start = 0
    while (offset := body.find(marker, start)) >= 0:
        offsets.append(offset)
        body[offset:offset + len(marker)] = replacement
        start = offset + len(replacement)
    restored = bytearray(body)
    for offset in offsets:
        restored[offset:offset + len(marker)] = marker
    if restored != container.body:
        raise ValueError("native config inverse did not restore the exact source")
    result = container.write(bytes(body))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "generator": "Tests/FxStandalone/create_full_native_float_variant.py",
        "source": str(source.resolve()), "sourceSha256": sha(original),
        "output": str(output.resolve()), "outputSha256": sha(result), "outputBytes": len(result),
        "bitmapConfigurationBodyOffsets": offsets,
        "beforeU01": list(ORIGINAL_U01), "afterU01": list(FLOAT_U01),
        "beforeFlags2": ORIGINAL_FLAGS2, "afterFlags2": FLOAT_FLAGS2,
        "originalBodyExactlyRestorable": True,
        "headerMetadataIdentityMeshPointFxPixelsExactlyPreserved": True,
        "externalReferenceCount": len(container.references), "nodeCount": container.node_count,
        "nativeVerdict": "untested full native float wrapper control; visual evidence pending"
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--expected-bitmaps", type=int, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output, args.expected_sha, args.expected_bitmaps), indent=2))
