"""Make one real-Sparkler probe using the engine's bitmap FID string branch.

The original graph is not deserialized/reserialized. Only its two verified
GPU-model texture slots change; their now-unused external-table entries are
removed. This is a candidate for native testing, not proof of standalone FX.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


DEFAULT_SOURCE = Path(r"C:\Users\PC\Documents\Trackmania\Items\ChatGPT\PackedOnlyV2\TestItem_Sparkler_BitmapRefs_v3.Item.Gbx")
SOURCE_SHA256 = "AE59D3DBA5FEBEB909E9D48C833A75CF1F7B06DCCA861A9924F6827DC32BF851"
SLOTS = ((3798, 21, "Sparkler_D.Texture.gbx"), (5672, 22, "SparklerEnd_D.Texture.gbx"))


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def generate(source: Path, output: Path, texture_root: str) -> dict:
    original = source.read_bytes()
    if sha(original) != SOURCE_SHA256:
        raise ValueError("source differs from the visually proven, pinned Sparkler fixture")
    container = GbxContainer.read(original)
    if container.node_count != 23 or len(container.body) != 6561:
        raise ValueError("unexpected real-Sparkler container/node layout")
    if [entry["nodeIndex"] for entry in container.references] != [21, 22]:
        raise ValueError("unexpected real-Sparkler bitmap node references")
    body = container.body
    patches = []
    # Work backwards so source offsets stay meaningful throughout the splice.
    for offset, index, filename in reversed(SLOTS):
        expected = struct.pack("<II", 0x090C6002, 22)
        if body[offset:offset + 8] != expected:
            raise ValueError("pinned GPU-model chunk/version signature changed")
        start = offset + 12
        old = struct.pack("<II", 0, index)
        if body[start:start + 8] != old:
            raise ValueError("pinned GPU-model texture branch/node index changed")
        # Other bitmap slots in this exact version22 record must all be null.
        for relative in (84, 124, 164):
            if struct.unpack_from("<i", container.body, offset + relative)[0] != -1:
                raise ValueError("unexpected additional bitmap slot; cannot remove dependencies")
        separator = "\\" if "\\" in texture_root else "/"
        path = (texture_root.rstrip("/\\") + separator + filename if texture_root else filename).encode("utf-8")
        replacement = struct.pack("<II", 1, len(path)) + path
        body = body[:start] + replacement + body[start + len(old):]
        patches.append({"sourceBodyOffset": start, "sourceNodeIndex": index,
                        "oldHex": old.hex(), "newHex": replacement.hex(),
                        "bitmapFidPath": path.decode("utf-8")})
    header_size = struct.unpack_from("<I", original, 13)[0]
    node_count_end = 17 + header_size + 4
    # Node indexes1..20 keep their exact identities; unused21/22 remain reserved.
    prefix = container.prefix[:node_count_end] + struct.pack("<I", 0)
    output_container = GbxContainer(prefix, container.body, container.compression,
                                    container.node_count, ())
    result = output_container.write(body)
    reopened = GbxContainer.read(result)
    if reopened.references or reopened.node_count != container.node_count:
        raise ValueError("candidate still has external bitmap references or changed node count")
    restored = body
    # Undo the exact splices to demonstrate every other decompressed byte is intact.
    delta = 0
    for patch in sorted(patches, key=lambda p: p["sourceBodyOffset"]):
        start = patch["sourceBodyOffset"]
        new = bytes.fromhex(patch["newHex"])
        old = bytes.fromhex(patch["oldHex"])
        if restored[start:start + len(new)] != new:
            raise ValueError("cannot restore the exact source texture splice")
        restored = restored[:start] + old + restored[start + len(new):]
    if restored != container.body:
        raise ValueError("opaque FX graph bytes changed outside the two texture fields")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "schemaVersion": 1,
        "generator": "Tests/FxStandalone/create_builtin_sparkler.py",
        "source": str(source.resolve()), "sourceSha256": sha(original),
        "output": str(output.resolve()), "outputBytes": len(result), "outputSha256": sha(result),
        "sourceCompression": container.compression, "compressionRetained": True,
        "sourceBodyBytes": len(container.body), "outputBodyBytes": len(body),
        "sourceBodySha256": sha(container.body), "outputBodySha256": sha(body),
        "originalNodeCountRetained": container.node_count,
        "externalReferenceCount": len(reopened.references),
        "patches": sorted(patches, key=lambda p: p["sourceBodyOffset"]),
        "allOtherDecompressedBodyBytesExactlyRestorable": True,
        "fxIdentity": "actual source Sparkler, both original emitters retained",
        "mesh": "original source mesh retained for isolated texture-resolution test",
        "dependencySemantics": "requires immutable builtin Trackmania bitmap assets; no user texture files",
        "nativeVerdict": "pending preload, placement, visible particles, save/reopen and saved-map embedding",
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--texture-root", default="GameData/Stadium/Media/Texture")
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output, args.texture_root), indent=2))


if __name__ == "__main__":
    main()
