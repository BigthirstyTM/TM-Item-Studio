"""Build an audit control with exact tiny, native inline bitmap/image payloads.

Unlike the DDS conversion candidate this copies an actual native pixel bitmap
without changing its image format, dimensions, pixels or bitmap flags. Only its
Image node index is allocated in the enclosing Sparkler item. This is not a
visually equivalent Sparkler, and is not a deliverable item.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from create_builtin_sparkler import DEFAULT_SOURCE, SOURCE_SHA256, SLOTS
from gbx_container import GbxContainer


NATIVE_BITMAP = Path(r"C:\Users\PC\OpenplanetNext\Extract\GameData\Techno3\Media\Texture\LightPosIRad.Texture.Gbx")


def generate(source: Path, native_bitmap: Path, output: Path) -> dict:
    original = source.read_bytes()
    if hashlib.sha256(original).hexdigest().upper() != SOURCE_SHA256:
        raise ValueError("source differs from the pinned real Sparkler fixture")
    container = GbxContainer.read(original)
    native_data = native_bitmap.read_bytes()
    native = GbxContainer.read(native_data)
    if struct.unpack_from("<I", native_data, 9)[0] != 0x09011000 or native.references or native.node_count != 2:
        raise ValueError("control must be an actual two-node zero-reference bitmap")
    marker = struct.pack("<4I", 0x09011030, 5, 1, 0x0902F000)
    if native.body.count(marker) != 1:
        raise ValueError("native control lacks the expected inline FileGen Image")
    bitmap_image_index = native.body.index(marker) + 8
    body = container.body
    insertions = []
    for offset, bitmap_index, _ in reversed(SLOTS):
        start = offset + 16
        if body[start - 4:start + 4] != struct.pack("<II", 0, bitmap_index):
            raise ValueError("Sparkler primary bitmap branch changed")
        image_index = bitmap_index + 2
        bitmap_body = (native.body[:bitmap_image_index] + struct.pack("<I", image_index)
                       + native.body[bitmap_image_index + 4:])
        node = struct.pack("<I", 0x09011000) + bitmap_body
        body = body[:start + 4] + node + body[start + 4:]
        insertions.append({"sourceBodyOffset": start + 4, "bytes": len(node),
                           "bitmapNodeIndex": bitmap_index, "imageNodeIndex": image_index})
    header_size = struct.unpack_from("<I", original, 13)[0]
    prefix = container.prefix[:17 + header_size] + struct.pack("<II", 25, 0)
    result = GbxContainer(prefix, body, container.compression, 25, ()).write(body)
    restored = body
    for insertion in sorted(insertions, key=lambda row: row["sourceBodyOffset"]):
        start = insertion["sourceBodyOffset"]
        restored = restored[:start] + restored[start + insertion["bytes"]:]
    if restored != container.body:
        raise ValueError("control changed original FX/mesh bytes")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {"generator": "Tests/FxStandalone/create_native_bitmap_control.py",
                "output": str(output.resolve()), "bytes": len(result),
                "sha256": hashlib.sha256(result).hexdigest().upper(),
                "nativeBitmap": str(native_bitmap.resolve()),
                "nativeBitmapSha256": hashlib.sha256(native_data).hexdigest().upper(),
                "nativeBitmapBodyExactExceptImageNodeIndex": True,
                "originalFxAndMeshBytesRestorableExactly": True,
                "nodeCount": 25, "externalReferences": 0,
                "purpose": "preload-only hypothesis isolation; changes particle appearance, no deliverable claim",
                "nativeVerdict": "not run; earlier DDS-to-RGBA32F candidate was in flight during process exit"}
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--native-bitmap", type=Path, default=NATIVE_BITMAP)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.native_bitmap, args.output), indent=2))
