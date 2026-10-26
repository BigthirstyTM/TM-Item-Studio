"""Decompressed-body-preserving native FX emitter probe.

Native Editor++ clones may be LZO compressed. Always decompress their body
before locating a bounded ParticleEmitter expression tail, then recompress.
The exact header/reference bytes and all other decompressed body bytes remain
intact. Compressed bytes may change globally; literal ASCII in LZO input must
never be treated as a serialized-field offset.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "FxStandalone"))
from gbx_container import GbxContainer

OLD = b"float3(0)"


def patch(source: Path, output: Path, expression: bytes):
    data = source.read_bytes()
    container = GbxContainer.read(data)
    body = container.body
    # A finished emitter-model node, null JointName, then LocalOffsetExpr.
    anchor = struct.pack("<IiI", 0xFACADE01, -1, len(OLD)) + OLD
    starts = [i for i in range(len(body)) if body.startswith(anchor, i)]
    if len(starts) != 1:
        raise ValueError(f"expected one bounded ParticleEmitter tail, found {len(starts)} in {source}")
    field = starts[0] + 8
    if struct.pack("<I", 0x0915C000) not in body[:field]:
        raise ValueError("emitter marker has no preceding CPlugFxSystem graph")
    cursor = field
    fields = []
    for _ in range(10):
        if cursor + 4 > len(body):
            raise ValueError("truncated ParticleEmitter expression tail")
        size = struct.unpack_from("<I", body, cursor)[0]
        if size > 4096 or cursor + 4 + size > len(body):
            raise ValueError("invalid ParticleEmitter expression length")
        fields.append(body[cursor + 4:cursor + 4 + size].decode("utf-8"))
        cursor += 4 + size
    if fields[0] != OLD.decode() or not fields[1].startswith("float3("):
        raise ValueError("bounded expression tail does not match local/world offset layout")
    if struct.unpack_from("<I", body, cursor)[0] not in (0, 1):
        raise ValueError("invalid ParticleEmitter DOVAndUpAreLocalSpace boolean")
    if len(expression) != len(OLD):
        raise ValueError("this isolation probe requires a nine-byte replacement expression")
    start = field + 4
    patched_body = body[:start] + expression + body[start + len(OLD):]
    if patched_body[:start] + OLD + patched_body[start + len(OLD):] != body:
        raise ValueError("unrelated decompressed body bytes changed")
    result = container.write(patched_body)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    return {
        "source": str(source.resolve()),
        "output": str(output.resolve()),
        "sourceBytes": len(data),
        "outputBytes": len(result),
        "sourceSha256": hashlib.sha256(data).hexdigest().upper(),
        "outputSha256": hashlib.sha256(result).hexdigest().upper(),
        "patchedField": "CPlugFxSystemNode_ParticleEmitter.LocalOffsetExpr",
        "oldValue": OLD.decode(),
        "newValue": expression.decode(),
        "nativeOffsetWritten": True,
        "sourceCompression": container.compression,
        "compressionModeRetained": True,
        "fieldOffsetInDecompressedBody": field,
        "headerAndReferenceBytesPreservedExactly": True,
        "allOtherDecompressedBodyBytesPreservedExactly": True,
        "compressedBytesMayDiffer": container.compression == "C",
        "nativeRuntimeVerdict": "pending Fids preload and placement test",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fogger", type=Path, required=True)
    parser.add_argument("--sparkler", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expression", default="float3(1)")
    args = parser.parse_args()
    expression = args.expression.encode("ascii")
    rows = [
        patch(args.fogger, args.output / "TMIS_Fogger_NativeOffsetProbe.Item.Gbx", expression),
        patch(args.sparkler, args.output / "TMIS_Sparkler_NativeOffsetProbe.Item.Gbx", expression),
    ]
    manifest = {
        "schemaVersion": 1,
        "generator": "Tests/FxNativePatch/raw_patch.py",
        "replacement": expression.decode(),
        "rows": rows,
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "fx-native-offset-probe-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()

