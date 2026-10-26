"""Read-only native saved-item graph/pixel audit without node reserialization."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer
from inline_bitmap import filegen_pixels_rgba32f


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def audit(path: Path, expected_pixels: dict[str, str]) -> dict:
    data = path.read_bytes()
    container = GbxContainer.read(data)
    body = container.body
    images = []
    start = 0
    signature = struct.pack("<4I", 0x0902F000, 0x80000006, 9, 6)
    while (offset := body.find(signature, start)) >= 0:
        start = offset + 4
        dimensions = struct.unpack_from("<6I", body, offset + 16)
        width, height, depth, components, image_format, mips = dimensions
        if not 0 < width <= 4096 or not 0 < height <= 4096 or depth != 1 or components != 4 or image_format != 8 or mips != 1:
            raise ValueError(f"unexpected native image parameters {dimensions}")
        if body[offset + 40:offset + 60] != bytes(20):
            raise ValueError("native image arrays/string configuration changed")
        pixel_bytes = width * height * depth * components * 4
        end = offset + 60 + pixel_bytes
        if end > len(body):
            raise ValueError("truncated native image texels")
        pixels = body[offset + 60:end]
        digest = sha(pixels)
        images.append({"bodyOffset": offset, "nodeIndex": struct.unpack_from("<I", body, offset - 4)[0],
                       "width": width, "height": height, "depth": depth, "components": components,
                       "format": "RGBA32F", "mipLevels": mips, "pixelBytes": pixel_bytes,
                       "pixelSha256": digest, "matchesOriginalTexture": expected_pixels.get(digest),
                       "archiveClass": "CPlugFileGen Pixels version6"})
    gpu = []
    marker = struct.pack("<II", 0x090C6002, 22)
    start = 0
    while (offset := body.find(marker, start)) >= 0:
        start = offset + 4
        branch, index = struct.unpack_from("<Ii", body, offset + 12)
        inline = branch == 0 and index > 0 and body[offset + 20:offset + 24] == struct.pack("<I", 0x09011000)
        gpu.append({"bodyOffset": offset, "isTextureFid": bool(branch), "textureNodeIndex": index,
                    "inlineBitmapClassImmediatelyFollows": inline})
    null_ids_parent = struct.pack("<6I", 0x09011036, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0x09011037)
    wrong_standalone = struct.pack("<7I", 0x09011036, 1, 0xFFFFFFFF, 3, 0xFFFFFFFF, 0xFFFFFFFF, 0x09011037)
    return {"path": str(path.resolve()), "bytes": len(data), "sha256": sha(data),
            "compression": container.compression, "bodyBytes": len(body), "nodeCount": container.node_count,
            "externalReferences": list(container.references), "fileGenImages": images, "gpuPrimaryTextures": gpu,
            "parentContextBitmapIdChunks": body.count(null_ids_parent),
            "incorrectStandaloneBitmapIdVersions": body.count(wrong_standalone),
            "allImageTexelsMatchOriginals": bool(images) and all(image["matchesOriginalTexture"] for image in images),
            "verdict": "offline saved-file evidence; native rendering/reopen is separate"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    texture_root = Path(r"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Media\Texture\Image")
    expected = {}
    for name in ("Sparkler_D.dds", "SparklerEnd_D.dds", "FoggerSmoke.dds"):
        archive, _ = filegen_pixels_rgba32f((texture_root / name).read_bytes())
        expected[sha(archive[60:])] = name
    result = {"generator": "Tests/FxStandalone/audit_saved_items.py",
              "items": [audit(path, expected) for path in args.root.rglob("*.Gbx") if path.name.lower().endswith(".item.gbx")]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
