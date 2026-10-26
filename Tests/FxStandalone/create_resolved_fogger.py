"""Bind the positive-rate native Fogger to the successful stock texture root.

Retains the source header, identity, node table, mesh, expressions, fog selector
and positive rate. Only the GPU model's primary texture branch is spliced.
No staging or engine operations; native fog rendering remains to be observed.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import struct

from fogger import DEFAULT_SOURCE, SOURCE_SHA256, gpu_record, sha
from gbx_container import GbxContainer


TEXTURE = r"Stadium\Media\Texture\FoggerSmoke.Texture.gbx"
NATIVE_BITMAP = Path(r"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Media\Texture\FoggerSmoke.Texture.gbx")


def generate(source: Path, output: Path) -> dict:
    raw = source.read_bytes()
    if sha(raw) != SOURCE_SHA256:
        raise ValueError("requires the pinned actual native positive-rate Fogger")
    original = GbxContainer.read(raw)
    if original.references or original.node_count != 15:
        raise ValueError("unexpected native Fogger graph/reference counts")
    offset, typed = gpu_record(original)
    if not typed.IsFogEmitter or typed.FogEmissionRate != 100.0 or typed.isTextureFid or typed.texture._index != -1:
        raise ValueError("expected the original positive100 volumetric Fogger and null bitmap")
    if NATIVE_BITMAP.name != TEXTURE.split("\\")[-1] or struct.unpack_from("<I", NATIVE_BITMAP.read_bytes(), 9)[0] != 0x09011000:
        raise ValueError("stock FoggerSmoke wrapper name/class differs")
    start = offset + 12
    before = struct.pack("<II", 0, 0xFFFFFFFF)
    if original.body[start:start + 8] != before:
        raise ValueError("native primary texture branch differs")
    texture_bytes = TEXTURE.encode("utf-8")
    replacement = struct.pack("<II", 1, len(texture_bytes)) + texture_bytes
    body = original.body[:start] + replacement + original.body[start + 8:]
    restored = body[:start] + before + body[start + len(replacement):]
    if restored != original.body:
        raise ValueError("complete original Fogger body was not restored exactly")
    result = original.write(body)
    reopened = GbxContainer.read(result)
    _, new_gpu = gpu_record(reopened)
    if reopened.prefix != original.prefix or reopened.node_count != 15 or reopened.references:
        raise ValueError("metadata/identity/header/reference table changed")
    if not new_gpu.isTextureFid or new_gpu.texture != TEXTURE or not new_gpu.IsFogEmitter or new_gpu.FogEmissionRate != 100.0:
        raise ValueError("texture/positive fog fields did not independently reparse")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    report = {
        "generator": "Tests/FxStandalone/create_resolved_fogger.py",
        "source": str(source.resolve()), "sourceSha256": sha(raw),
        "output": str(output.resolve()), "outputSha256": sha(result), "bytes": len(result),
        "stageAt": "Items/TMIS_FX_SequentialProof/" + output.name,
        "bitmapFid": TEXTURE, "nativeBitmapVerifiedAt": str(NATIVE_BITMAP),
        "nativeBitmapSha256": sha(NATIVE_BITMAP.read_bytes()),
        "sourceTextureBranchOffset": start, "beforeHex": before.hex(), "afterHex": replacement.hex(),
        "isTextureFid": True, "isFogEmitter": True, "fogEmissionRate": 100.0,
        "originalHeaderIdentityAndReferenceTableExactlyPreserved": True,
        "completeOriginalDecompressedBodyExactlyRestored": True,
        "allFxMeshEmitterAndPlacementBytesOutsideTextureBranchExactlyPreserved": True,
        "nodeCount": 15, "externalReferenceTableEntries": 0,
        "dependencies": "stock Trackmania bitmap/image only; no additional user texture files",
        "identityPolicy": "original source header/body Id definitions retained byte-for-byte; filename is a unique preload control",
        "nativeVerdict": "pending isolated preload, bitmap binding inspection, visible smoke and native save/reopen",
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output), indent=2))
