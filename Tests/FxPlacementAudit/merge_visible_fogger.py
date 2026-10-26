"""Merge proven Fogger FX into the exact visible Sparkler carrier, offline.

Bounded source hashes and explicit native NodeRef/lookback fields only. The
baseline keeps the complete visible static mesh/material/collision/prefab
header. A second file changes its 24 positions to a distinct tapered bell.
No staging, runtime operation or rendering claim is made by this generator.
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
from deform_mesh import find_position_stream
from visible_fogger import splice

SPARKLER = ROOT / "artifacts/fx-clones/fx-fid-path-matrix-20261025/TMIS_Sparkler_FidStadiumBackslash_20261025.Item.Gbx"
SPARKLER_SHA = "3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44"
FOGGER = ROOT / "artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/visibility/TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx"
FOGGER_SHA = "82E1540DF19C9E3B226375BA176A1B5A80DEDAA483D2A9E1B5FA851BB660A079"
TEXTURE = r"Stadium\Media\Texture\FoggerSmoke.Texture.gbx"
IMPORTED_DEFINITIONS = ((0, 7, 0x0915C000), (53, 8, 0x090B3000),
    (73, 9, 0x090B2000), (137, 10, 0x090B5000),
    (1201, 11, 0x090C5000), (1301, 12, 0x090C6000))
LOOKBACK_ALIASES = (49, 121, 1706)
CLASSES = (0x09145000, 0x09159000, 0x090BB000, 0x0901E000,
    0x09056000, 0x090FD000, 0x0900C000, 0x0915C000,
    0x090B3000, 0x090B2000, 0x090B5000, 0x090C5000,
    0x090C6000, 0x2E020000, 0x09187000)


def unique(data, needle):
    if data.count(needle) != 1: raise ValueError("structural signature is not unique: " + needle.hex())
    return data.index(needle)


def graph_range(body, node, last_expression):
    start = unique(body, struct.pack("<III", node, 0x0915C000, 0x0915C000))
    at = unique(body, struct.pack("<I", len(last_expression)) + last_expression)
    if at < start: raise ValueError("native emission expression precedes its graph")
    for _ in range(10):
        length = struct.unpack_from("<I", body, at)[0]
        if length > 2048 or at + 4 + length > len(body): raise ValueError("invalid bounded emitter tail string")
        body[at + 4:at + 4 + length].decode("utf-8")
        at += 4 + length
    if struct.unpack_from("<I", body, at)[0] not in (0, 1): raise ValueError("native emitter bool differs")
    at += 4
    for _ in range(2):
        length = struct.unpack_from("<I", body, at)[0]
        if length > 2048 or at + 4 + length > len(body): raise ValueError("invalid bounded hue string")
        at += 4 + length
    trailer = struct.pack("<5I", 0xffffffff, 0xffffffff, 0, 55, 0xfacade01)
    if body[at:at + 20] != trailer: raise ValueError("native FX trailer differs")
    end = at + 20
    if end + 36 > len(body): raise ValueError("missing complete prefab owner")
    return start, end, end + 36


def generate(folder, bell):
    sparkler_raw = SPARKLER.read_bytes(); fogger_raw = FOGGER.read_bytes()
    if sha(sparkler_raw) != SPARKLER_SHA or sha(fogger_raw) != FOGGER_SHA:
        raise ValueError("requires the exact independently native-visible sources")
    carrier = GbxContainer.read(sparkler_raw); donor = GbxContainer.read(fogger_raw)
    if carrier.references or donor.references or carrier.node_count != 23 or donor.node_count != 15:
        raise ValueError("source graph/reference counts differ")
    spark_at, spark_end, spark_owner_end = graph_range(carrier.body, 8, b"float3(0,0,10)")
    fog_at, fog_end, fog_owner_end = graph_range(donor.body, 7, b"float3(0,3.5,0)")
    if spark_at != 2395 or fog_at != 6347: raise ValueError("reviewed native graph boundaries differ")
    imported = bytearray(donor.body[fog_at:fog_owner_end])
    owner = struct.pack("<7fII", 0, 0, 0, 1, 0, 0, 0, 0xffffffff, 0)
    if imported[-36:] != owner: raise ValueError("Fogger owner must be identity in item coordinates")
    root_name = struct.pack("<III", 0, 0x40000000, 9) + b"Fogger16m" + struct.pack("<II", 1, 4)
    if imported[20:49] != root_name: raise ValueError("Fogger Parallel/root name/one-child schema differs")
    # Existing Sparkler carrier has author definition1 and no material-name
    # definition. The imported Parallel name therefore becomes definition2.
    # These are actual typed Id fields, not a global replacement of float bits.
    if imported[81:89] != struct.pack("<II", 0x090B202D, 4):
        raise ValueError("recorded submodel v4 lookback field chunk differs")
    if imported[1702:1706] != struct.pack("<I", 0x090B3001):
        raise ValueError("recorded emitter-name lookback field chunk differs")
    for offset in LOOKBACK_ALIASES:
        if struct.unpack_from("<I", imported, offset)[0] != 0x40000004:
            raise ValueError("recorded Fogger lookback alias differs")
        struct.pack_into("<I", imported, offset, 0x40000002)
    for offset, old, class_id in IMPORTED_DEFINITIONS:
        if struct.unpack_from("<II", imported, offset) != (old, class_id):
            raise ValueError("recorded Fogger NodeRef definition differs")
        struct.pack_into("<I", imported, offset, old + 1)
    recovered_donor = bytearray(imported)
    for offset in LOOKBACK_ALIASES: struct.pack_into("<I", recovered_donor, offset, 0x40000004)
    for offset, old, _ in IMPORTED_DEFINITIONS: struct.pack_into("<I", recovered_donor, offset, old)
    if recovered_donor != donor.body[fog_at:fog_owner_end]: raise ValueError("complete Fogger FX+owner not exactly restorable")
    patches = [{"offset": spark_at, "old": carrier.body[spark_at:spark_owner_end],
                "new": bytes(imported), "kind": "one remapped native Fogger FX graph plus its identity owner"}]
    if bell:
        position_offset, old_positions, points = find_position_stream(carrier)
        if len(points) != 24 or set(round(p.y, 4) for p in points) != {0.0, 4.0}:
            raise ValueError("expected the exact native 24-vertex visible cube")
        replacement = []
        for p in points:
            t = p.y / 4.0; radius = 1.4 - .8 * t
            replacement.append((p.x / 2.0 * radius, 3.0 * t, p.z / 2.0 * radius))
        new_positions = b"".join(struct.pack("<fff", *p) for p in replacement)
        patches.append({"offset": position_offset, "old": old_positions, "new": new_positions,
                        "kind": "24 Float3 positions form 3m bell, 2.8m base and 1.2m top"})
    placement = []
    for old, new, class_id in ((19, 14, 0x2E020000), (20, 15, 0x09187000)):
        offset = unique(carrier.body, struct.pack("<II", old, class_id))
        if offset < spark_owner_end: raise ValueError("placement definition precedes FX owner end")
        patches.append({"offset": offset, "old": struct.pack("<I", old), "new": struct.pack("<I", new), "kind": "placement NodeRef reindex"})
        placement.append({"sourceOffset": offset, "before": old, "after": new, "classId": hex(class_id)})
    body = splice(carrier.body, patches)
    if splice(body, patches, True) != carrier.body: raise ValueError("complete Sparkler carrier not exactly restorable")
    prefix = bytearray(carrier.prefix)
    node_count_at = 17 + struct.unpack_from("<I", prefix, 13)[0]
    if struct.unpack_from("<II", prefix, node_count_at) != (23, 0): raise ValueError("source zero-reference table differs")
    struct.pack_into("<I", prefix, node_count_at, 16)
    restored_prefix = bytearray(prefix); struct.pack_into("<I", restored_prefix, node_count_at, 23)
    if restored_prefix != carrier.prefix: raise ValueError("source prefix not exactly restorable")
    result = GbxContainer(bytes(prefix), body, carrier.compression, 16, ()).write(body)
    reopened = GbxContainer.read(result); _, gpu = gpu_record(reopened)
    if reopened.node_count != 16 or reopened.references or not gpu.IsFogEmitter or gpu.FogEmissionRate != 100.0 or gpu.texture != TEXTURE:
        raise ValueError("merged native Fogger config differs")
    definitions = []
    for index, class_id in enumerate(CLASSES, 1):
        definitions.append({"index": index, "classId": hex(class_id),
            "offset": unique(reopened.body, struct.pack("<II", index, class_id))})
    if [d["index"] for d in sorted(definitions, key=lambda d: d["offset"])] != list(range(1, 16)):
        raise ValueError("merged graph is not sequential in native first-use order")
    file_name = "TMIS_Fogger_VisibleCarrier" + ("Bell" if bell else "Baseline") + "_20261025.Item.Gbx"
    output = folder / file_name
    if output.exists(): raise FileExistsError("preserve previous evidence: " + str(output))
    folder.mkdir(parents=True, exist_ok=True); output.write_bytes(result)
    report = {"generator": "Tests/FxPlacementAudit/merge_visible_fogger.py",
              "carrier": str(SPARKLER), "carrierSha256": sha(sparkler_raw),
              "fxDonor": str(FOGGER), "fxDonorSha256": sha(fogger_raw),
              "output": str(output.resolve()), "outputSha256": sha(result), "bytes": len(result),
              "stageAt": "Items/TMIS_FX_SequentialProof/" + output.name,
              "headerAndBodyIds": "original native null IDs retained", "author": "5X7FYPqoTyWnW8fpyvCv5Q",
              "mesh": "24-vertex 3m tapered bell" if bell else "exact original native visible 4m cube",
              "allStaticMaterialNormalsUvsIndicesCollisionPrefabFlagsPreserved": True,
              "collisionNote": "Original visible Sparkler collision shape remains; Bell visual deformation does not claim collision parity.",
              "nativePoint": "float3(0,3.5,0)", "fxOwner": "identity, from native-visible Fogger",
              "bitmapFid": gpu.texture, "isFogEmitter": True, "fogEmissionRate": 100.0,
              "lookback": {"carrierAuthorSlot": 1, "importedParallelNameSlot": 2,
                  "oldDonorAlias": 4, "newAlias": 2, "recordedRelativeAliasOffsets": LOOKBACK_ALIASES,
                  "submodelAliasSchema": "090B202D v4 u10", "emitterAliasSchema": "090B3001 name"},
              "nodeDefinitions": definitions, "importedNodeRefPatches": [{"relativeOffset": o, "before": n, "after": n + 1, "classId": hex(c)} for o, n, c in IMPORTED_DEFINITIONS],
              "placementPatches": placement,
              "sourceCarrierBodyExactlyRestorable": True, "sourceFoggerGraphAndOwnerExactlyRestorable": True,
              "originalHeaderAndRefTablePreservedExceptNodeCount23To16": True,
              "sourceFxRanges": {"carrier": [spark_at, spark_end, spark_owner_end], "donor": [fog_at, fog_end, fog_owner_end]},
              "nativeVerdict": "Offline only. Both source behaviors observed separately; merged mesh/smoke/native save and standalone behavior not yet tested."}
    output.with_suffix(".merge-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    p = argparse.ArgumentParser(); p.add_argument("--output-folder", type=Path, required=True); a = p.parse_args()
    print(json.dumps([generate(a.output_folder, False), generate(a.output_folder, True)], indent=2))
