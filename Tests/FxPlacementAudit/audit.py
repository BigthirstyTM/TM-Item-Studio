"""Compare FX placement fixtures and create a bounded native-mesh experiment.

This script is offline only. It does not stage files in Trackmania's Items
directory or call Openplanet. The native Fogger source has unsupported particle
chunks, so its complete body is deliberately never rebuilt through gbx-py.
"""

from __future__ import annotations

import hashlib
import json
import struct
import sys
from collections.abc import Mapping
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
NICE = Path(r"C:\Users\PC\Downloads\Blender Addons\blendermania-addon\blendermania-addon\NICE")
sys.path.insert(0, str(NICE))
from src.parser import parse_file  # noqa: E402

ORIGINAL = Path(r"C:\Users\PC\Documents\Trackmania\Items\ShadocAlex Items\FogVolumetric2\FogVolumetric1_FogEmissionRate_0.Item.Gbx")
NATIVE = ROOT / "artifacts/fx-clones/native-embedded-20261025/TMIS_FoggerEmbeddedNativeClone.Item.Gbx"
CANDIDATES = ROOT / "artifacts/fx-clones/fx-native-patched-20261025"
OUT = ROOT / "artifacts/fx-clones/fx-native-mesh-proof-20261025"


def clean(value):
    if isinstance(value, Mapping):
        return {f"0x{k:08X}" if isinstance(k, int) else str(k): clean(v)
                for k, v in value.items() if not str(k).startswith("_")}
    if isinstance(value, (list, tuple)):
        return [clean(v) for v in value]
    if isinstance(value, (str, float, int, bool)) or value is None:
        return value
    if isinstance(value, bytes):
        return {"bytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}
    return str(value)


def mesh(document):
    return document.body[0x2E002019]["EntityModel"]["body"]["Ents"][0]["model"]["body"]["Mesh"]["body"][0x090BB000]


def positions(mesh_body):
    visual = mesh_body["visuals"][0]["body"][0x0900600F]
    stream = visual["vertexStreams"][0]["body"][0x09056000]
    if str(stream["DataDecl"][0]["header"]["Name"]) != "Position":
        raise ValueError("first native mesh declaration is not Position")
    return stream["Data"][0]


def blob(points):
    return b"".join(struct.pack("<fff", p["x"], p["y"], p["z"]) for p in points)


def report(path):
    doc = parse_file(str(path), with_nodes=False, recursive=False)
    row = {
        "path": str(path), "bytes": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper(),
        "nodeCount": int(doc.numNodes), "compression": str(doc.bodyCompression),
        "header": clean(doc.header.data[0]),
        "bodyIdent": clean(doc.body[0x2E00100B]["author"]),
        "bodyChunks": [f"0x{k:08X}" if isinstance(k, int) else k for k in doc.body],
        "placement": clean(doc.body.get(0x2E00201C)),
        "archetype": clean(doc.body.get(0x2E00201E)),
        "entityDecoded": "EntityModel" in doc.body.get(0x2E002019, {}),
    }
    if row["entityDecoded"]:
        m = mesh(doc)
        points = positions(m)
        row["mesh"] = {
            "version": int(m["version"]), "visualCount": len(m["visuals"]),
            "shadedGeomCount": len(m["shadedGeoms"]),
            "materialCount": int(m["materialCount"]),
            "customMaterialCount": len(m["customMaterials"]),
            "positionCount": len(points),
            "bounds": {axis: [min(p[axis] for p in points), max(p[axis] for p in points)]
                       for axis in ("x", "y", "z")},
        }
    else:
        row["parserLimit"] = "NICE cannot decode native-saved CPlugParticleEmitterSubModel chunk 0x090B5000. Remaining placement/archetype are unknown to this parser, not absent."
    return row


def make_native_proof(native, source_positions, target_name, deform):
    data = native.read_bytes()
    if data[7:8] != b"U":
        raise ValueError("native mesh proof requires an uncompressed source")
    source_id = rb"TMIS_FX_NativeCopy\TMIS_FoggerEmbeddedNativeClone.Item.Gbx"
    target_id = ("TMIS_FX_MeshProofs\\" + target_name + ".Item.Gbx").encode("ascii")
    if len(target_id) != len(source_id) or data.count(source_id) != 2:
        raise ValueError("identity replacement must preserve both native path lengths")
    old_blob = blob(source_positions)
    if data.count(old_blob) != 1:
        raise ValueError("native position stream is not uniquely identical to the typed source")
    start = data.index(old_blob)
    result = data.replace(source_id, target_id)
    edits = [
        {"kind": "header/body identity", "old": source_id.decode(), "new": target_id.decode(), "occurrences": 2},
    ]
    if deform:
        # Preserve the 129-vertex topology, all normals/UVs, material links,
        # index buffers, node references, placement fields and complete FX.
        # The original +/-8m cube becomes a 3m tapered square nozzle tower.
        new_points = []
        for p in source_positions:
            t = min(1.0, max(0.0, (p.y + 8.0) / 16.0))
            radius = 1.3 - 1.05 * t
            new_points.append({"x": p.x / 8.0 * radius, "y": t * 3.0, "z": p.z / 8.0 * radius})
        replacement = blob(new_points)
        result = result[:start] + replacement + result[start + len(old_blob):]
        edits.append({"kind": "vertex positions", "offset": start, "length": len(old_blob), "positionCount": len(source_positions),
                      "description": "3m square nozzle tower, 2.6m base and 0.5m top; native topology/materials retained"})
    if len(result) != len(data):
        raise AssertionError("byte length changed")
    allowed = set(range(start, start + len(old_blob))) if deform else set()
    at = 0
    while True:
        at = data.find(source_id, at)
        if at < 0:
            break
        allowed.update(range(at, at + len(source_id)))
        at += len(source_id)
    changes = [i for i, (a, b) in enumerate(zip(data, result)) if a != b]
    if any(i not in allowed for i in changes):
        raise AssertionError("unrelated byte changed")
    path = OUT / (target_name + ".Item.Gbx")
    path.write_bytes(result)
    return {
        "file": str(path), "stageAt": "Items/" + target_id.decode().replace("\\", "/"),
        "bytes": len(result), "sha256": hashlib.sha256(result).hexdigest().upper(),
        "source": str(native), "sourceSha256": hashlib.sha256(data).hexdigest().upper(),
        "edits": edits, "changedByteCount": len(changes),
        "unrelatedBytesPreserved": True, "nativeRuntimeVerdict": "pending preload, placement, screenshot and save/reopen",
    }


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    files = [ORIGINAL, NATIVE, CANDIDATES / "TMIS_Fogger_NativePyramid.Item.Gbx",
             CANDIDATES / "TMIS_Fogger_NativePyramid_inplace.Item.Gbx",
             CANDIDATES / "TMIS_Fogger_NativePyramid_nodetable.Item.Gbx"]
    rows = [report(p) for p in files if p.exists()]
    source = parse_file(str(ORIGINAL), with_nodes=False, recursive=False)
    points = positions(mesh(source))
    candidates = [
        make_native_proof(NATIVE, points, "TMIS_FoggerIdentityOnlyControl", False),
        make_native_proof(NATIVE, points, "TMIS_FoggerCustomTowerMeshTest", True),
    ]
    output = {"date": "2026-10-25", "generator": "Tests/FxPlacementAudit/audit.py", "rows": rows,
              "findings": [
                  "Python candidate changes header identity but leaves body identity empty; native Save-As sets both to the Items-relative filename.",
                  "Python synthetic mesh removes source shaded geometry and materials (1 to 0), so it lacks the source render mapping.",
                  "Source and Python candidate placement/archetype fields are equal; native Save-As trailing chunks are not decoded by NICE.",
                  "These differences are hypotheses for native failure; a previously open editor dialog may separately have caused PlaceItems=false.",
                  "New candidate uses fixed-width identity and vertex edits on the already native-placeable source; FX and all other bytes are preserved.",
              ], "candidates": candidates}
    (OUT / "placement-audit.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(json.dumps({"report": str(OUT / "placement-audit.json"), "candidates": candidates}, indent=2))


if __name__ == "__main__":
    main()
