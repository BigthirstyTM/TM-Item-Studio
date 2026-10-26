"""Offline RGBA8 Pixels control, retaining original stable DDS bitmap config.

Current Openplanet.h names CPlugFileImg::EPlugImageFormat::RGBA8 = 16.
Native acceptance of Pixels+RGBA8 is still a separate engine test.
"""

from __future__ import annotations

import argparse
from array import array
import hashlib
import json
from pathlib import Path
import struct
import sys

from gbx_container import GbxContainer


def sha(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest().upper()


def generate(source: Path, output: Path, expected_sha: str, expected_images: int) -> dict:
    original = source.read_bytes()
    if sha(original) != expected_sha.upper() or expected_images not in (1, 2):
        raise ValueError("source SHA or expected image count differs from audited fixture")
    container = GbxContainer.read(original)
    if container.references:
        raise ValueError("RGBA8 control requires an already self-contained source")
    marker = struct.pack("<4I", 0x0902F000, 0x80000006, 9, 6)
    patches = []
    start = 0
    while (offset := container.body.find(marker, start)) >= 0:
        width, height, depth, components, image_format, mips = struct.unpack_from("<6I", container.body, offset + 16)
        if (width, height, depth, components, image_format, mips) != (512, 512, 1, 4, 8, 1):
            raise ValueError("control only accepts audited512-square RGBA32F single-mip images")
        if container.body[offset + 40:offset + 60] != bytes(20):
            raise ValueError("FileGen extra arrays/string configuration changed")
        end = offset + 60 + width * height * components * 4
        old = container.body[offset:end]
        if len(old) != 60 + width * height * components * 4:
            raise ValueError("truncated image")
        values = array("f"); values.frombytes(old[60:])
        if sys.byteorder != "little": values.byteswap()
        rgba = bytes(round(value * 255) if 0 <= value <= 1 else 256 for value in values)
        restored_values = array("f", (value / 255 for value in rgba))
        if sys.byteorder != "little": restored_values.byteswap()
        if restored_values.tobytes() != old[60:]:
            raise ValueError("input float pixels are not exactly recoverable original8-bit decoded DDS texels")
        replacement = old[:32] + struct.pack("<I", 16) + old[36:60] + rgba
        patches.append({"offset": offset, "old": old, "new": replacement,
                        "rgbaSha": sha(rgba), "originalFloatSha": sha(old[60:])})
        start = end
    if len(patches) != expected_images:
        raise ValueError("unexpected inline image count")
    body = container.body
    for patch in reversed(patches):
        offset = patch["offset"]
        body = body[:offset] + patch["new"] + body[offset + len(patch["old"]):]
    delta = 0
    for patch in patches:
        patch["outputOffset"] = patch["offset"] + delta
        delta += len(patch["new"]) - len(patch["old"])
    restored = body
    for patch in reversed(patches):
        offset = patch["outputOffset"]
        restored = restored[:offset] + patch["old"] + restored[offset + len(patch["new"]):]
    if restored != container.body:
        raise ValueError("exact inverse image replacement did not restore original body")
    result = container.write(body)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "generator": "Tests/FxStandalone/create_rgba8_control.py",
        "source": str(source.resolve()), "sourceSha256": sha(original),
        "output": str(output.resolve()), "outputSha256": sha(result), "outputBytes": len(result),
        "imagePatches": [{"sourceBodyOffset": p["offset"], "outputBodyOffset": p["outputOffset"],
                          "sourceArchiveBytes": len(p["old"]), "outputArchiveBytes": len(p["new"]),
                          "rgba8PixelSha256": p["rgbaSha"], "originalRgba32fPixelSha256": p["originalFloatSha"]}
                         for p in patches],
        "allOriginalTexelValuesExactlyRecoverable": True, "originalBodyExactlyRestorable": True,
        "bitmapConfigsMetadataIdentityMeshPointFxPreserved": True,
        "formatEvidence": "current Openplanet.h CPlugFileImg::EPlugImageFormat::RGBA8=16",
        "nativeVerdict": "untested encoding control; no native-load or render claim"
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--expected-images", type=int, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output, args.expected_sha, args.expected_images), indent=2))
