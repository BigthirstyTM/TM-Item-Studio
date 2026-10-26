"""Pinned Sparkler bitmap embedding with strictly sequential first-use nodes.

This deliberately patches recorded NodeRef integer positions only. It never
deserializes/reserializes unsupported particle chunks or globally replaces
integer values. Native preload is still required before the result is usable.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from create_builtin_sparkler import DEFAULT_SOURCE, SOURCE_SHA256
from create_native_bitmap_control import NATIVE_BITMAP
from gbx_container import GbxContainer


NATIVE_SHA256 = "A12AD7C6CAC09BB7182AA6DFE3CD99BB2D5CFE2DBA6CCB5A5C2FFDA650A56C0D"
# Offset points at the NodeRef index, followed by its inline class when present.
# Verified on the pinned uncompressed source body; placement is after both FX.
NODE_DEFINITIONS = (
    (306, 1, 0x09145000), (342, 2, 0x09159000),
    (354, 3, 0x090BB000), (406, 4, 0x0901E000),
    (470, 5, 0x09056000), (1794, 6, 0x090FD000),
    (2005, 7, 0x0900C000), (2395, 8, 0x0915C000),
    (2450, 9, 0x090B3000), (2470, 10, 0x090B2000),
    (2534, 11, 0x090B5000), (3598, 12, 0x090C5000),
    (3714, 13, 0x090C6000), (4324, 14, 0x090B3000),
    (4344, 15, 0x090B2000), (4408, 16, 0x090B5000),
    (5472, 17, 0x090C5000), (5588, 18, 0x090C6000),
    (6257, 19, 0x2E020000), (6379, 20, 0x09187000),
)
REMAPPING = {**{n: n for n in range(1, 14)},
             **{n: n + 2 for n in range(14, 19)},
             19: 23, 20: 24, 21: 14, 22: 21}
SLOTS = ((3814, 21, 14, 15), (5688, 22, 21, 22))


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def validate_source(data: bytes) -> GbxContainer:
    if sha(data) != SOURCE_SHA256:
        raise ValueError("source is not the pinned visually proven Sparkler")
    container = GbxContainer.read(data)
    if len(container.body) != 6561 or container.node_count != 23:
        raise ValueError("unexpected pinned Sparkler container layout")
    if [ref["nodeIndex"] for ref in container.references] != [21, 22]:
        raise ValueError("unexpected external bitmap refs")
    for offset, index, class_id in NODE_DEFINITIONS:
        actual = struct.unpack_from("<II", container.body, offset)
        if actual != (index, class_id):
            raise ValueError(f"recorded NodeRef {index}@{offset} differs: {actual}")
    for offset, old, _, _ in SLOTS:
        if struct.unpack_from("<II", container.body, offset - 4) != (0, old):
            raise ValueError("recorded primary bitmap NodeRef changed")
    return container


def generate(source: Path, native_bitmap: Path, output: Path,
             parent_lookback_initialized: bool = True) -> dict:
    original = source.read_bytes()
    container = validate_source(original)
    native_data = native_bitmap.read_bytes()
    if sha(native_data) != NATIVE_SHA256:
        raise ValueError("native tiny bitmap is not the pinned control")
    native = GbxContainer.read(native_data)
    if native.references or native.node_count != 2:
        raise ValueError("native bitmap control must have two nodes and no refs")
    marker = struct.pack("<4I", 0x09011030, 5, 1, 0x0902F000)
    if native.body.count(marker) != 1:
        raise ValueError("native Image NodeRef signature changed")
    image_offset = native.body.index(marker) + 8
    bitmap_body = native.body
    lookback_offset = None
    if parent_lookback_initialized:
        # chunk036 version1: null U01 NodeRef, first-file Id null, null U06
        # NodeRef. The standalone bitmap starts its Id stream with version3;
        # the enclosing item has already initialized that same shared stream.
        # Empty Id means there are no dictionary entries to remap.
        lookback_marker = struct.pack("<7I", 0x09011036, 1, 0xFFFFFFFF,
                                      3, 0xFFFFFFFF, 0xFFFFFFFF, 0x09011037)
        if bitmap_body.count(lookback_marker) != 1:
            raise ValueError("native bitmap empty-Id lookback signature changed")
        lookback_offset = bitmap_body.index(lookback_marker) + 12
        bitmap_body = bitmap_body[:lookback_offset] + bitmap_body[lookback_offset + 4:]
    patched = bytearray(container.body)
    edits = []
    for offset, old, class_id in NODE_DEFINITIONS:
        new = REMAPPING[old]
        if new != old:
            struct.pack_into("<I", patched, offset, new)
            edits.append({"sourceBodyOffset": offset, "old": old, "new": new,
                          "classId": f"0x{class_id:08X}", "kind": "NodeRef"})
    insertions = []
    for offset, old, new, image in reversed(SLOTS):
        bitmap = bytearray(bitmap_body)
        struct.pack_into("<I", bitmap, image_offset, image)
        node = struct.pack("<I", 0x09011000) + bitmap
        struct.pack_into("<I", patched, offset, new)
        patched[offset + 4:offset + 4] = node
        edits.append({"sourceBodyOffset": offset, "old": old, "new": new,
                      "classId": "0x09011000", "kind": "external-to-inline NodeRef"})
        insertions.append({"sourceBodyOffset": offset + 4, "bytes": len(node),
                           "bitmapNodeIndex": new, "imageNodeIndex": image,
                           "imageIndexOffsetInBitmapBody": image_offset})
    body = bytes(patched)
    # Recover the original body exactly, first removing insertions from low to
    # high source positions, then undoing the recorded integer-only patches.
    recovered = body
    for insertion in sorted(insertions, key=lambda row: row["sourceBodyOffset"]):
        at = insertion["sourceBodyOffset"]
        recovered = recovered[:at] + recovered[at + insertion["bytes"]:]
    recovered = bytearray(recovered)
    for edit in edits:
        struct.pack_into("<I", recovered, edit["sourceBodyOffset"], edit["old"])
    if bytes(recovered) != container.body:
        raise ValueError("recorded edits do not recover the source exactly")
    # New first-use sequence: 1..13, bitmap14/image15, old14..18→16..20,
    # bitmap21/image22, and placement23/magnet24.
    first_use = [REMAPPING[n] for n in range(1, 14)] + [14, 15]
    first_use += [REMAPPING[n] for n in range(14, 19)] + [21, 22, 23, 24]
    if first_use != list(range(1, 25)):
        raise ValueError("embedded graph is not sequential")
    header_size = struct.unpack_from("<I", original, 13)[0]
    prefix = container.prefix[:17 + header_size] + struct.pack("<II", 25, 0)
    result = GbxContainer(prefix, body, container.compression, 25, ()).write(body)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {"generator": "Tests/FxStandalone/reindex.py",
                "output": str(output.resolve()), "bytes": len(result), "sha256": sha(result),
                "sourceSha256": sha(original), "nativeBitmapSha256": sha(native_data),
                "nodeCount": 25, "externalReferences": 0,
                "firstUseNodeIndices": first_use,
                "oldToNewNodeIndex": REMAPPING,
                "recordedNodeRefEdits": sorted(edits, key=lambda row: row["sourceBodyOffset"]),
                "insertions": sorted(insertions, key=lambda row: row["sourceBodyOffset"]),
                "nativeBitmapBodyExactExceptImageNodeIndexAndLookbackVersion": True,
                "parentLookbackInitialized": parent_lookback_initialized,
                "removedStandaloneLookbackVersionOffset": lookback_offset,
                "bitmapIdIsNullNoDictionaryRemapping": parent_lookback_initialized,
                "sourceBodyExactlyRecoverable": True,
                "nativeVerdict": "not tested; isolated sequential-node preload control"}
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--native-bitmap", type=Path, default=NATIVE_BITMAP)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--parent-lookback-initialized", action="store_true", default=True)
    parser.add_argument("--preserve-standalone-lookback", action="store_true",
                        help="reproduce the known-invalid negative control only")
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.native_bitmap, args.output,
                              args.parent_lookback_initialized and not args.preserve_standalone_lookback), indent=2))
