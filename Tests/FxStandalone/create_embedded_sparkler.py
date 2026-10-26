"""Embed the real source Sparkler's two bitmap+pixel nodes into one item.

Inline texture payloads, recorded node indexes and the reference table change.
Bitmap first-ID fields share the parent lookback context. FX settings and mesh
stay intact. All source graph bytes are recoverable by reversing exact edits.
Native loading/rendering and map save/reopen remain mandatory separate tests.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from create_builtin_sparkler import DEFAULT_SOURCE, SOURCE_SHA256
from gbx_container import GbxContainer
from inline_bitmap import build_inline_bitmap, validate_native_pixel_examples
from reindex import NODE_DEFINITIONS, REMAPPING, SLOTS, validate_source


DEFAULT_ASSETS = DEFAULT_SOURCE.parent / "SparklerBitmapAssetsV3"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def generate(source: Path, assets: Path, output: Path) -> dict:
    original = source.read_bytes()
    if sha(original) != SOURCE_SHA256:
        raise ValueError("source differs from the visually proven, pinned Sparkler fixture")
    container = validate_source(original)
    body = bytearray(container.body)
    node_edits = []
    for offset, old, class_id in NODE_DEFINITIONS:
        new = REMAPPING[old]
        if new != old:
            struct.pack_into("<I", body, offset, new)
            node_edits.append({"sourceBodyOffset": offset, "old": old, "new": new,
                               "classId": f"0x{class_id:08X}"})
    patches = []
    names = {21: "Sparkler_D.Texture.gbx", 22: "SparklerEnd_D.Texture.gbx"}
    for start, old_bitmap_index, bitmap_index, image_index in reversed(SLOTS):
        gpu_offset = start - 16
        if body[gpu_offset:gpu_offset + 8] != struct.pack("<II", 0x090C6002, 22):
            raise ValueError("pinned GPU-model chunk/version signature changed")
        if body[start - 4:start + 4] != struct.pack("<II", 0, old_bitmap_index):
            raise ValueError("pinned GPU-model bitmap branch/node index changed")
        filename = names[old_bitmap_index]
        wrapper = assets / filename
        dds = assets / "Image" / filename.replace(".Texture.gbx", ".dds")
        inline_node = build_inline_bitmap(wrapper, dds, image_index)
        replacement = struct.pack("<I", bitmap_index) + inline_node
        body = body[:start] + replacement + body[start + 4:]
        node_edits.append({"sourceBodyOffset": start, "old": old_bitmap_index,
                           "new": bitmap_index, "classId": "0x09011000"})
        patches.append({"sourceBodyOffset": start, "sourceBitmapNodeIndex": old_bitmap_index,
                        "bitmapNodeIndex": bitmap_index,
                        "imageNodeIndex": image_index, "inlineNodeBytes": len(inline_node),
                        "bitmapWrapper": str(wrapper.resolve()), "wrapperSha256": sha(wrapper.read_bytes()),
                        "imageDds": str(dds.resolve()), "ddsSha256": sha(dds.read_bytes())})
    body = bytes(body)
    header_size = struct.unpack_from("<I", original, 13)[0]
    node_count_offset = 17 + header_size
    prefix = container.prefix[:node_count_offset] + struct.pack("<II", 25, 0)
    result = GbxContainer(prefix, container.body, container.compression, 25, ()).write(body)
    reopened = GbxContainer.read(result)
    if reopened.references or reopened.node_count != 25:
        raise ValueError("embedded candidate has an unexpected node count/reference table")
    # Restore source by deleting exactly the two appended inline nodes.
    restored = body
    for patch in sorted(patches, key=lambda p: p["sourceBodyOffset"]):
        start = patch["sourceBodyOffset"] + 4
        restored = restored[:start] + restored[start + patch["inlineNodeBytes"]:]
    restored = bytearray(restored)
    for edit in node_edits:
        struct.pack_into("<I", restored, edit["sourceBodyOffset"], edit["old"])
    if bytes(restored) != container.body:
        raise ValueError("opaque Sparkler bytes changed outside the recorded inline/index edits")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "schemaVersion": 1, "generator": "Tests/FxStandalone/create_embedded_sparkler.py",
        "source": str(source.resolve()), "sourceSha256": sha(original),
        "output": str(output.resolve()), "outputBytes": len(result), "outputSha256": sha(result),
        "sourceCompression": container.compression, "compressionRetained": True,
        "sourceBodyBytes": len(container.body), "outputBodyBytes": len(body),
        "sourceBodySha256": sha(container.body), "outputBodySha256": sha(body),
        "sourceNodeCount": 23, "outputNodeCount": reopened.node_count,
        "externalReferenceCount": len(reopened.references),
        "nodeIndexRemapping": REMAPPING,
        "recordedNodeRefEdits": sorted(node_edits, key=lambda row: row["sourceBodyOffset"]),
        "strictSequentialFirstUseNodeIndices": list(range(1, 25)),
        "patches": sorted(patches, key=lambda p: p["sourceBodyOffset"]),
        "allOriginalDecompressedBodyBytesExactlyRestorable": True,
        "bitmapSettings": "all original bitmap wrapper settings retained",
        "lookbackContext": "standalone version3 before bitmap chunk036 null ID omitted; parent item context already initialized",
        "imageEncoding": "native CPlugFileGen Pixels kind9 v6, RGBA32F original DDS decoded texels",
        "fxIdentity": "actual source Sparkler, both original emitters retained",
        "mesh": "original source mesh retained for isolated texture-embedding test",
        "dependencySemantics": "all two bitmap nodes and all image pixels inline; no external files or builtin texture FIDs",
        "nativeVerdict": "pending preload, placement, visible particles, save/reopen and saved-map embedding",
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--assets", type=Path, default=DEFAULT_ASSETS)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--validate-native-examples", type=Path)
    args = parser.parse_args()
    if args.validate_native_examples:
        print(json.dumps({"nativePixelExamples": validate_native_pixel_examples(args.validate_native_examples)}, indent=2))
    print(json.dumps(generate(args.source, args.assets, args.output), indent=2))
