"""Replace only inline image archives with exact native Plain green generator.

This isolates Pixels versus an ordinary procedural color FileGen. All original
Sparkler bitmap configuration, FX, model/mesh, indexes and metadata are retained.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


SOURCE_SHA = "FDA42E7DC13285D630F7564DB228F70C7DE1C46A27BE0951F55A8F5C0C81EA7B"
PLAIN_SOURCE = Path(r"C:\Users\PC\OpenplanetNext\Extract\GameData\Techno3\Media\Texture\DefaultRGB_GreenHue.Texture.Gbx")


def sha(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest().upper()


def generate(source: Path, output: Path) -> dict:
    original = source.read_bytes()
    if sha(original) != SOURCE_SHA:
        raise ValueError("control requires the exact corrected original inline Sparkler")
    container = GbxContainer.read(original)
    native_data = PLAIN_SOURCE.read_bytes()
    native = GbxContainer.read(native_data)
    if native.references or native.node_count != 2 or len(native.body) != 350:
        raise ValueError("native Plain bitmap container changed")
    expected = struct.pack("<4I5I I4f4I", 0x0902F000, 0x80000006, 2, 5,
                           4, 4, 3, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0)
    if len(expected) != 72 or native.body[104:176] != expected:
        raise ValueError("native Plain archive changed; refusing inferred generator bytes")
    marker = struct.pack("<4I6I5I", 0x0902F000, 0x80000006, 9, 6,
                         512, 512, 1, 4, 8, 1, 0, 0, 0, 0, 0)
    old_length = 60 + 512 * 512 * 4 * 4
    positions = []
    start = 0
    while (offset := container.body.find(marker, start)) >= 0:
        positions.append(offset)
        start = offset + old_length
    if len(positions) != 2:
        raise ValueError("expected two full-pixel Sparkler image archives")
    body = container.body
    for offset in reversed(positions):
        body = body[:offset] + expected + body[offset + old_length:]
    restored = body
    for index, offset in reversed(list(enumerate(positions))):
        output_offset = offset + index * (len(expected) - old_length)
        restored = restored[:output_offset] + container.body[offset:offset + old_length] + restored[output_offset + len(expected):]
    if restored != container.body:
        raise ValueError("generator substitution did not retain exact inverse")
    result = container.write(body)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "generator": "Tests/FxStandalone/create_native_plain_control.py",
        "source": str(source.resolve()), "sourceSha256": sha(original),
        "nativePlainBitmap": str(PLAIN_SOURCE), "nativePlainBitmapSha256": sha(native_data),
        "output": str(output.resolve()), "outputSha256": sha(result), "outputBytes": len(result),
        "originalImageBodyOffsets": positions, "nativeImageArchiveBytes": len(expected),
        "nativeGenerator": "Plain(kind2), parameters[4,4,3,BGRA0,0], color(0,1,0,1)",
        "exactNativeImageArchiveCopied": True, "originalBodyExactlyRestorable": True,
        "allOriginalBitmapConfigurationsFxMeshMetadataPreserved": True,
        "nativeVerdict": "untested; isolates raw Pixels archive from general inline color generator"
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output), indent=2))
