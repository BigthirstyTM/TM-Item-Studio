"""Deform an existing native mesh while retaining every other decoded byte.

Only CPlugVertexStream Position/Float3 data is read. The opaque particle graph,
node identities, vertex count, index buffer, normals, UVs and materials remain
unchanged. Native placement/rendering must be tested separately by the caller.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
sys.path.insert(0, r"C:\Users\PC\Downloads\Blender Addons\blendermania-addon\blendermania-addon\NICE")
from gbx_container import GbxContainer  # noqa: E402
from src.gbx_structs import body_chunks  # noqa: E402


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def find_position_stream(container):
    signature = struct.pack("<II", 0x09056000, 0x09056000)
    body = container.body
    if body.count(signature) != 1:
        raise ValueError("expected exactly one inline CPlugVertexStream")
    payload = body.index(signature) + 8
    stream = io.BytesIO(body)
    stream.seek(payload)
    typed = body_chunks[0x09056000].parse_stream(
        stream, gbx_data={}, nodes=[None] * container.node_count,
        load_external_file=lambda _: None,
    )
    declaration = typed.DataDecl[0]["header"]
    if str(declaration.Name) != "Position" or str(declaration.Type) != "Float3" or str(declaration.Space) != "Global3D":
        raise ValueError("first stream must be uncompressed Global3D Float3 positions")
    points = typed.Data[0]
    packed = b"".join(struct.pack("<fff", p.x, p.y, p.z) for p in points)
    if body.count(packed) != 1:
        raise ValueError("position byte block is not unique")
    offset = body.index(packed)
    if offset < payload or offset + len(packed) > stream.tell():
        raise ValueError("position byte block escaped the decoded vertex stream")
    return offset, packed, points


def generate(source: Path, output: Path, shape: str):
    data = source.read_bytes()
    container = GbxContainer.read(data)
    offset, old, points = find_position_stream(container)
    new_points = []
    if shape == "sparkler-flare":
        if len(points) != 24 or set(round(p.y, 4) for p in points) != {0.0, 4.0}:
            raise ValueError("sparkler-flare expects the pinned 24-vertex source cube")
        for p in points:
            t = p.y / 4.0
            radius = 0.25 + 0.95 * t
            new_points.append((p.x / 2.0 * radius, 3.0 * t, p.z / 2.0 * radius))
        description = "3m flared square emitter stand; 0.5m base and 2.4m top"
    elif shape == "fogger-tower":
        if len(points) != 129:
            raise ValueError("fogger-tower expects the pinned 129-vertex source mesh")
        for p in points:
            t = min(1.0, max(0.0, (p.y + 8.0) / 16.0))
            radius = 1.3 - 1.05 * t
            new_points.append((p.x / 8.0 * radius, 3.0 * t, p.z / 8.0 * radius))
        description = "3m tapered square nozzle tower; 2.6m base and 0.5m top"
    else:
        raise ValueError("unknown mesh deformation")
    replacement = b"".join(struct.pack("<fff", *p) for p in new_points)
    if len(replacement) != len(old):
        raise ValueError("mesh deformation changed stream length")
    body = container.body[:offset] + replacement + container.body[offset + len(old):]
    if body[:offset] != container.body[:offset] or body[offset + len(old):] != container.body[offset + len(old):]:
        raise ValueError("opaque bytes outside positions changed")
    result = container.write(body)
    reopened = GbxContainer.read(result)
    _, reread, reparsed = find_position_stream(reopened)
    if reread != replacement or len(reparsed) != len(points):
        raise ValueError("deformed positions did not round-trip")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "generator": "Tests/FxPlacementAudit/deform_mesh.py",
        "source": str(source.resolve()), "sourceSha256": sha(data),
        "output": str(output.resolve()), "outputSha256": sha(result),
        "shape": shape, "description": description, "positionCount": len(points),
        "patchedDecodedBodyOffset": offset, "patchedByteLength": len(old),
        "headerReferenceTableByteIdentical": reopened.prefix == container.prefix,
        "allOtherDecodedBodyBytesIdentical": True,
        "bounds": {axis: [min(p[i] for p in new_points), max(p[i] for p in new_points)]
                   for i, axis in enumerate(("x", "y", "z"))},
        "sourceNodeCount": container.node_count, "outputNodeCount": reopened.node_count,
        "sourceExternalReferences": container.references,
        "nativeRuntimeVerdict": "pending preload, placement, screenshot and save/reopen",
        "identityNote": "identity/prefix unchanged; caller must prepare a matching native identity when required",
    }
    output.with_suffix(".mesh-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--shape", choices=("sparkler-flare", "fogger-tower"), required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output, args.shape), indent=2))


if __name__ == "__main__":
    main()
