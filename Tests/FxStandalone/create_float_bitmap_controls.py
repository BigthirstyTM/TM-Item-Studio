"""Offline, reversible bitmap-config controls for the corrected inline Sparkler.

Only observed CPlugBitmap configuration words change. Opaque FX, image data,
mesh, indexes and item identity remain byte-identical after decompression.
The native engine must establish whether any setting affects rendering.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


SOURCE_SHA = "FDA42E7DC13285D630F7564DB228F70C7DE1C46A27BE0951F55A8F5C0C81EA7B"
ORIGINAL_U01 = (0x002C0000, 0x10004E12, 0x08003692)
FLOAT_U01 = (0x0026000E, 0x10000E00, 0x08000092)
ORIGINAL_FLAGS2 = 0x48000800
FLOAT_FLAGS2 = 0x08000800


def sha(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest().upper()


def generate(source: Path, output_root: Path) -> list[dict]:
    original = source.read_bytes()
    if sha(original) != SOURCE_SHA:
        raise ValueError("control requires the exact corrected full-pixel Sparkler fixture")
    container = GbxContainer.read(original)
    if container.references or container.node_count != 25:
        raise ValueError("expected corrected zero-reference 25-node fixture")
    signature = struct.pack("<3I3f4I", *ORIGINAL_U01, 0, 1, 0,
                            0, 0x09011032, 0, ORIGINAL_FLAGS2)
    offsets = []
    start = 0
    while (offset := container.body.find(signature, start)) >= 0:
        offsets.append(offset)
        start = offset + len(signature)
    if len(offsets) != 2:
        raise ValueError("expected exactly two original particle bitmap configuration tails")
    reports = []
    for name in ("UsageFloatOnly", "Flags2NativeOnly", "U01NativeOnly", "FullNativeFloat"):
        body = bytearray(container.body)
        edits = []
        for offset in offsets:
            if name == "UsageFloatOnly":
                writes = [(offset, struct.pack("<I", ORIGINAL_U01[0] | 14))]
            elif name == "Flags2NativeOnly":
                writes = [(offset + 36, struct.pack("<I", FLOAT_FLAGS2))]
            elif name == "U01NativeOnly":
                writes = [(offset, struct.pack("<3I", *FLOAT_U01))]
            else:
                writes = [(offset, struct.pack("<3I", *FLOAT_U01)),
                          (offset + 36, struct.pack("<I", FLOAT_FLAGS2))]
            for position, replacement in writes:
                before = bytes(body[position:position + len(replacement)])
                body[position:position + len(replacement)] = replacement
                edits.append({"bodyOffset": position, "beforeHex": before.hex(), "afterHex": replacement.hex()})
        restored = bytearray(body)
        for edit in reversed(edits):
            position = edit["bodyOffset"]
            before = bytes.fromhex(edit["beforeHex"])
            restored[position:position + len(before)] = before
        if restored != container.body:
            raise ValueError("inverse edits did not exactly restore original body")
        result = container.write(bytes(body))
        output = output_root / f"TMIS_Sparkler_{name}.Item.Gbx"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(result)
        manifest = {
            "generator": "Tests/FxStandalone/create_float_bitmap_controls.py",
            "source": str(source.resolve()), "sourceSha256": sha(original),
            "output": str(output.resolve()), "outputSha256": sha(result), "outputBytes": len(result),
            "control": name, "edits": edits, "originalBodyExactlyRestorable": True,
            "headerRefTableNodeCountPreserved": True,
            "imagePixelsFxMeshIdentityPreserved": True,
            "observedNativeSource": "GameData/Techno3/Media/Texture/LightPosIRad.Texture.Gbx",
            "fieldMeaning": "U01 triple opaque except observed low-byte ColorFloat=14; Flags2 extra0x40000000 meaning unconfirmed",
            "nativeVerdict": "untested; no load/rendering claim"
        }
        output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        reports.append(manifest)
    return reports


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output_root), indent=2))
