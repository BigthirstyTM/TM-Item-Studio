"""Embed real smoke pixels in the pinned native positive-rate Fogger graph.

Raw bounded edits preserve opaque particle chunks. No game or live-file action
is performed; native loading, visible smoke and map save/reopen need proof.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from fogger import DEFAULT_SOURCE, SOURCE_SHA256
from gbx_container import GbxContainer
from inline_bitmap import build_inline_bitmap


TEXTURE = Path(r"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Media\Texture\FoggerSmoke.Texture.gbx")
IMAGE = TEXTURE.parent / "Image/FoggerSmoke.dds"
BITMAP_REF = 7745
TAIL_REFS = ((8295, 13, 15, 0x2E020000), (8429, 14, 16, 0x09187000))


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def generate(source: Path, output: Path, sprites: bool = False) -> dict:
    original = source.read_bytes()
    if sha(original) != SOURCE_SHA256:
        raise ValueError("Fogger source differs from pinned native Save-As fixture")
    container = GbxContainer.read(original)
    if container.node_count != 15 or container.references:
        raise ValueError("expected native 15-node Fogger with no external refs")
    body = bytearray(container.body)
    if body[7729:BITMAP_REF + 4] != struct.pack("<IIfIi", 0x090C6002, 22, 0.0, 0, -1):
        raise ValueError("Fogger GPU bitmap-null branch signature differs")
    if struct.unpack_from("<II", body, 7645) != (12, 0x090C6000):
        raise ValueError("Fogger GPU NodeRef/class differs")
    edits = []
    for offset, old, new, class_id in TAIL_REFS:
        if struct.unpack_from("<II", body, offset) != (old, class_id):
            raise ValueError("Fogger placement NodeRef/class differs")
        struct.pack_into("<I", body, offset, new)
        edits.append({"sourceBodyOffset": offset, "old": old, "new": new,
                      "classId": f"0x{class_id:08X}"})
    flag_offset = 7873
    if struct.unpack_from("<If", body, flag_offset) != (1, 100.0):
        raise ValueError("Fogger selector/rate differ from known positive source")
    if sprites:
        struct.pack_into("<I", body, flag_offset, 0)
        edits.append({"sourceBodyOffset": flag_offset, "old": 1, "new": 0,
                      "kind": "IsFogEmitter"})
    inline = build_inline_bitmap(TEXTURE, IMAGE, 14)
    struct.pack_into("<I", body, BITMAP_REF, 13)
    body[BITMAP_REF + 4:BITMAP_REF + 4] = inline
    edits.append({"sourceBodyOffset": BITMAP_REF, "old": -1, "new": 13,
                  "kind": "primary bitmap NodeRef"})
    restored = body[:BITMAP_REF + 4] + body[BITMAP_REF + 4 + len(inline):]
    for edit in edits:
        struct.pack_into("<i", restored, edit["sourceBodyOffset"], edit["old"])
    if bytes(restored) != container.body:
        raise ValueError("recorded edits do not restore original Fogger body")
    header_size = struct.unpack_from("<I", original, 13)[0]
    prefix = container.prefix[:17 + header_size] + struct.pack("<II", 17, 0)
    result = GbxContainer(prefix, bytes(body), container.compression, 17, ()).write(bytes(body))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {"generator": "Tests/FxStandalone/create_embedded_fogger.py",
                "source": str(source.resolve()), "sourceSha256": sha(original),
                "output": str(output.resolve()), "outputBytes": len(result),
                "outputSha256": sha(result), "outputNodeCount": 17,
                "externalReferenceCount": 0, "firstUseNodeIndices": list(range(1, 17)),
                "sourceBodyExactlyRecoverable": True,
                "recordedEdits": sorted(edits, key=lambda row: row["sourceBodyOffset"]),
                "inlineNodeSourceBodyOffset": BITMAP_REF + 4, "inlineNodeBytes": len(inline),
                "texture": str(TEXTURE), "textureSha256": sha(TEXTURE.read_bytes()),
                "image": str(IMAGE), "imageSha256": sha(IMAGE.read_bytes()),
                "lookbackContext": "bitmap standalone version3 omitted; null Id needs no dictionary remap",
                "IsFogEmitter": not sprites, "FogEmissionRate": 100.0,
                "nativeVerdict": "pending; require corrected Sparkler bitmap proof before native test"}
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--sprites", action="store_true")
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output, args.sprites), indent=2))
