"""Thicken the native Fogger's window-frame topology using positions only.

The 129 vertices use only two absolute source levels: 8 and approximately
7.60319. They describe double-sided borders of six cube faces, not filled
panels. Preserve every native index, normal, UV, material and FX byte.
"""
from pathlib import Path
import argparse
import json
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from gbx_container import GbxContainer
from fogger import gpu_record, sha
from full_path_ids_control import replace_definition
from deform_mesh import find_position_stream
from visible_fogger import splice

SOURCE = ROOT / "artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/visibility/TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx"
PINNED = "82E1540DF19C9E3B226375BA176A1B5A80DEDAA483D2A9E1B5FA851BB660A079"
ORIGINAL = ROOT / "artifacts/fx-clones/fx-resolved-fogger-20261025/TMIS_Fogger_FidStadiumBackslash_20261025.Item.Gbx"
ORIGINAL_HASH = "C041F94AABCB8B8D25A34E76BB561E43D0E1A1831DD2926DAD217171822B21AE"
OLD_ID = rb"TMIS_FX_SequentialProof\TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx"
WIDTH = 0.18


def is_outer(value):
    a = abs(value)
    if abs(a - 8.0) < 0.00001: return True
    if abs(a - 7.603190898895264) < 0.00001: return False
    raise ValueError("source vertex is outside the independently observed two-level frame topology")


def generate(output):
    raw = SOURCE.read_bytes(); original_raw = ORIGINAL.read_bytes()
    if sha(raw) != PINNED or sha(original_raw) != ORIGINAL_HASH:
        raise ValueError("requires the exact source topology and smoke-visible tower")
    source = GbxContainer.read(raw); original = GbxContainer.read(original_raw)
    offset, old, current = find_position_stream(source)
    _, _, points = find_position_stream(original)
    if len(current) != 129 or len(points) != 129:
        raise ValueError("expected the exact 129-vertex original window frame")
    expected_old = []
    new_points = []
    for p in points:
        t = min(1.0, max(0.0, (p.y + 8.0) / 16.0))
        radius = 1.3 - 1.05 * t
        expected_old.append((p.x / 8.0 * radius, 3.0 * t, p.z / 8.0 * radius))
        y = (3.0 if p.y > 0 else 0.0) if is_outer(p.y) else (3.0 - WIDTH if p.y > 0 else WIDTH)
        radius = 1.3 - 1.05 * (y / 3.0)
        x = (1 if p.x > 0 else -1) * (radius if is_outer(p.x) else radius - WIDTH)
        z = (1 if p.z > 0 else -1) * (radius if is_outer(p.z) else radius - WIDTH)
        new_points.append((x, y, z))
    if b"".join(struct.pack("<fff", *p) for p in expected_old) != old:
        raise ValueError("current tower no longer has the exact original vertex correspondence")
    replacement = b"".join(struct.pack("<fff", *p) for p in new_points)
    if len(replacement) != len(old): raise ValueError("position stream size changed")
    new_id = ("TMIS_FX_SequentialProof\\" + output.name).encode()
    if not output.name.endswith(".Item.Gbx") or b".." in new_id:
        raise ValueError("requires a simple unique item filename")
    header_size, count = struct.unpack_from("<II", source.prefix, 13)
    first_id, first_size = struct.unpack_from("<II", source.prefix, 21)
    if not 1 <= count <= 32 or first_id != 0x2E001003: raise ValueError("collector header layout differs")
    header_tag = 21 + count * 8 + 4
    body_tag = source.body.index(struct.pack("<I", 0x2E00100B), 0, 350) + 4
    patches = [{"offset": body_tag, "old": struct.pack("<II", 0x40000000, len(OLD_ID)) + OLD_ID,
                "new": struct.pack("<II", 0x40000000, len(new_id)) + new_id, "kind": "unique existing body catalog ID"},
               {"offset": offset, "old": old, "new": replacement, "kind": "129 Float3 positions only"}]
    body = splice(source.body, patches)
    if splice(body, patches, True) != source.body: raise ValueError("source body is not exactly restorable")
    delta = len(new_id) - len(OLD_ID)
    prefix = bytearray(replace_definition(source.prefix, header_tag, OLD_ID, new_id))
    struct.pack_into("<I", prefix, 13, header_size + delta)
    struct.pack_into("<I", prefix, 25, (first_size & 0x80000000) | ((first_size & 0x7fffffff) + delta))
    restored = bytearray(replace_definition(prefix, header_tag, new_id, OLD_ID))
    struct.pack_into("<I", restored, 13, header_size); struct.pack_into("<I", restored, 25, first_size)
    if restored != source.prefix: raise ValueError("source header is not exactly restorable")
    result = GbxContainer(bytes(prefix), body, source.compression, source.node_count, source.references).write(body)
    reopened = GbxContainer.read(result); _, reread, final_points = find_position_stream(reopened)
    _, gpu = gpu_record(reopened)
    if reread != replacement or reopened.node_count != 15 or reopened.references or not gpu.IsFogEmitter or gpu.FogEmissionRate != 100.0:
        raise ValueError("position or native FX fields changed unexpectedly")
    if output.exists(): raise FileExistsError("preserve existing evidence: " + str(output))
    output.parent.mkdir(parents=True, exist_ok=True); output.write_bytes(result)
    report = {"generator": "Tests/FxPlacementAudit/fogger_thick_frame.py", "source": str(SOURCE), "sourceSha256": sha(raw),
              "originalTopology": str(ORIGINAL), "originalTopologySha256": sha(original_raw),
              "output": str(output.resolve()), "outputSha256": sha(result), "bytes": len(result),
              "stageAt": "Items/" + new_id.decode().replace("\\", "/"), "headerAndBodyId": new_id.decode(),
              "topology": "six native double-sided rectangular window-frame faces, unchanged index buffer",
              "positionCount": 129, "triangleCount": 96, "borderThicknessMetres": WIDTH,
              "bounds": {axis: [min(getattr(p, axis) for p in final_points), max(getattr(p, axis) for p in final_points)] for axis in ("x", "y", "z")},
              "nativeLocalOffsetUnchanged": "float3(0,3.5,0)",
              "allIndicesNormalsUvsMaterialsLodCollisionFxAndUnknownBytesPreserved": True,
              "lookbackDefinitionSlotsPreserved": True, "sourceExactlyRestorable": True,
              "patches": [{"offset": p["offset"], "oldBytes": len(p["old"]), "newBytes": len(p["new"]), "kind": p["kind"]} for p in patches],
              "nativeVerdict": "Unstaged offline geometry-only control; source smoke observed, thick-frame visibility still unverified."}
    output.with_suffix(".thick-frame-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    p = argparse.ArgumentParser(); p.add_argument("--output", type=Path, required=True); a = p.parse_args()
    print(json.dumps(generate(a.output), indent=2))
