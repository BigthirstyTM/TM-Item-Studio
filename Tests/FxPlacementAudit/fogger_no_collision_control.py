"""Match the visible Sparkler's static-object collision selection.

The exact v3 archive schema is Mesh NodeRef, boolbyte IsMeshCollidable and,
when false, Shape NodeRef. Insert a null Shape while disabling generated mesh
collision. No model, mesh, material, emitter or particle field changes.
"""
from pathlib import Path
import argparse
import json
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from fogger import gpu_record, sha
from full_path_ids_control import replace_definition
from gbx_container import GbxContainer
from visible_fogger import splice

SOURCE = ROOT / "artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/material-name-controls/TMIS_FoggerTower_NameUniqueThickFrame_20261025.Item.Gbx"
PINNED = "0140B2B6E40780BE4F1016A9B98D858BF1F60C44CFB59CA5CAAEC3588B72AC07"
OLD_ID = rb"TMIS_FX_SequentialProof\TMIS_FoggerTower_NameUniqueThickFrame_20261025.Item.Gbx"


def generate(output):
    raw = SOURCE.read_bytes()
    if sha(raw) != PINNED: raise ValueError("requires the exact reviewed unique-name thick frame")
    source = GbxContainer.read(raw)
    fx_signature = struct.pack("<III", 7, 0x0915C000, 0x0915C000)
    if source.body.count(fx_signature) != 1: raise ValueError("native FX node identity differs")
    fx_at = source.body.index(fx_signature)
    owner = struct.pack("<7fII", 0, 0, 0, 1, 0, 0, 0, 0xffffffff, 0)
    flag_at = fx_at - len(owner) - 1
    if source.body[flag_at - 4:fx_at] != struct.pack("<I", 0xfacade01) + b"\x01" + owner:
        raise ValueError("static mesh-end/collision/owner boundary differs")
    static = struct.pack("<III", 2, 0x09159000, 3)
    if source.body.count(static) != 1 or not source.body.index(static) < flag_at:
        raise ValueError("static-object v3 archive is not the expected sole carrier")
    new_id = ("TMIS_FX_SequentialProof\\" + output.name).encode()
    header_size, count = struct.unpack_from("<II", source.prefix, 13)
    first_id, first_size = struct.unpack_from("<II", source.prefix, 21)
    if not 1 <= count <= 32 or first_id != 0x2e001003: raise ValueError("collector header layout differs")
    header_tag = 21 + count * 8 + 4
    body_tag = source.body.index(struct.pack("<I", 0x2e00100b), 0, 350) + 4
    patches = [{"offset": body_tag, "old": struct.pack("<II", 0x40000000, len(OLD_ID)) + OLD_ID,
                "new": struct.pack("<II", 0x40000000, len(new_id)) + new_id, "kind": "unique existing body ID"},
               {"offset": flag_at, "old": b"\x01", "new": b"\x00\xff\xff\xff\xff",
                "kind": "IsMeshCollidable false plus schema-required null Shape NodeRef"}]
    body = splice(source.body, patches)
    if splice(body, patches, True) != source.body: raise ValueError("source body not exactly restorable")
    delta = len(new_id) - len(OLD_ID)
    prefix = bytearray(replace_definition(source.prefix, header_tag, OLD_ID, new_id))
    struct.pack_into("<I", prefix, 13, header_size + delta)
    struct.pack_into("<I", prefix, 25, (first_size & 0x80000000) | ((first_size & 0x7fffffff) + delta))
    restored = bytearray(replace_definition(prefix, header_tag, new_id, OLD_ID))
    struct.pack_into("<I", restored, 13, header_size); struct.pack_into("<I", restored, 25, first_size)
    if restored != source.prefix: raise ValueError("source header not exactly restorable")
    result = GbxContainer(bytes(prefix), body, source.compression, source.node_count, source.references).write(body)
    reopened = GbxContainer.read(result); _, gpu = gpu_record(reopened)
    if reopened.node_count != 15 or reopened.references or not gpu.IsFogEmitter or gpu.FogEmissionRate != 100.0:
        raise ValueError("FX or native graph changed unexpectedly")
    if output.exists(): raise FileExistsError("preserve existing evidence: " + str(output))
    output.parent.mkdir(parents=True, exist_ok=True); output.write_bytes(result)
    report = {"generator": "Tests/FxPlacementAudit/fogger_no_collision_control.py", "source": str(SOURCE), "sourceSha256": sha(raw),
              "output": str(output.resolve()), "outputSha256": sha(result), "bytes": len(result),
              "stageAt": "Items/" + new_id.decode().replace("\\", "/"), "headerAndBodyId": new_id.decode(),
              "sourceCollisionFlagOffset": flag_at, "IsMeshCollidable": False, "ShapeNodeRef": -1,
              "schema": "CPlugStaticObjectModel v3 Mesh, boolbyte IsMeshCollidable, conditional Shape NodeRef",
              "sourceExactlyRestorable": True, "allOtherMeshMaterialLodFxPointAndUnknownBytesPreserved": True,
              "nodeCount": 15, "externalReferences": 0, "nativeVerdict": "Unstaged offline control; native behavior unverified."}
    output.with_suffix(".collision-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    p = argparse.ArgumentParser(); p.add_argument("--output", type=Path, required=True); a = p.parse_args()
    print(json.dumps(generate(a.output), indent=2))
