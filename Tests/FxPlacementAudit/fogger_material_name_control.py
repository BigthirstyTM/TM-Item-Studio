"""Single-material-field controls for the smoke-visible stock-FID tower.

Only its existing material-name definition changes, plus the existing complete
catalog IDs needed for isolated native caching. No native or staging actions.
"""
from pathlib import Path
import argparse
import json
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from fogger import gpu_record, sha
from gbx_container import GbxContainer
from full_path_ids_control import replace_definition
from resolved_fogger_visibility import parse_material
from visible_fogger import splice

SOURCE = ROOT / "artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/visibility/TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx"
PINNED = "82E1540DF19C9E3B226375BA176A1B5A80DEDAA483D2A9E1B5FA851BB660A079"
OLD_ID = rb"TMIS_FX_SequentialProof\TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx"
OLD_NAME = b"TM_AddSelfIllum"


def generate(folder, name, label, source_path=SOURCE, pinned=PINNED, old_id=OLD_ID):
    raw = source_path.read_bytes()
    if sha(raw) != pinned: raise ValueError("requires the exact smoke-visible source tower")
    source = GbxContainer.read(raw)
    file_name = "TMIS_FoggerTower_Name" + label + "_20261025.Item.Gbx"
    new_id = ("TMIS_FX_SequentialProof\\" + file_name).encode()
    old_name = struct.pack("<II", 0x40000000, len(OLD_NAME)) + OLD_NAME
    new_name = struct.pack("<II", 0x40000000, len(name)) + name
    if source.body.count(old_name) != 1: raise ValueError("existing unique material-name definition differs")
    name_offset = source.body.index(old_name)
    material_offset = name_offset - 17
    before, before_context = parse_material(source.body, material_offset)
    if before.materialName != OLD_NAME.decode() or before.link != r"Stadium\Media\Material\PlatformTech":
        raise ValueError("source material does not match the expected name/link control")
    header_size, count = struct.unpack_from("<II", source.prefix, 13)
    first_id, first_size = struct.unpack_from("<II", source.prefix, 21)
    if not 1 <= count <= 32 or first_id != 0x2E001003: raise ValueError("header metadata schema differs")
    header_tag = 21 + count * 8 + 4
    body_tag = source.body.index(struct.pack("<I", 0x2E00100B), 0, 350) + 4
    old_definition = struct.pack("<II", 0x40000000, len(old_id)) + old_id
    new_definition = struct.pack("<II", 0x40000000, len(new_id)) + new_id
    patches = [{"offset": body_tag, "old": old_definition, "new": new_definition, "kind": "unique existing body catalog ID"},
               {"offset": name_offset, "old": old_name, "new": new_name, "kind": "existing material-name definition text"}]
    body = splice(source.body, patches)
    if splice(body, patches, True) != source.body: raise ValueError("source body is not exactly restorable")
    delta = len(new_id) - len(old_id)
    prefix = bytearray(replace_definition(source.prefix, header_tag, old_id, new_id))
    struct.pack_into("<I", prefix, 13, header_size + delta)
    struct.pack_into("<I", prefix, 25, (first_size & 0x80000000) | ((first_size & 0x7fffffff) + delta))
    restored = bytearray(replace_definition(prefix, header_tag, new_id, old_id))
    struct.pack_into("<I", restored, 13, header_size); struct.pack_into("<I", restored, 25, first_size)
    if restored != source.prefix: raise ValueError("source header is not exactly restorable")
    after, after_context = parse_material(body, material_offset + delta)
    if after.materialName != name.decode() or before_context["lookbackstring_index"] != after_context["lookbackstring_index"]:
        raise ValueError("material name or dictionary-slot count differs")
    for key in before:
        if key.startswith("_") or key == "materialName": continue
        if before[key] != after[key]: raise ValueError("unrelated material field changed: " + key)
    result = GbxContainer(bytes(prefix), body, source.compression, source.node_count, source.references).write(body)
    reopened = GbxContainer.read(result); _, gpu = gpu_record(reopened)
    if reopened.node_count != 15 or reopened.references or not gpu.IsFogEmitter or gpu.FogEmissionRate != 100.0:
        raise ValueError("native source graph or fog controls changed")
    output = folder / file_name
    if output.exists(): raise FileExistsError("preserve existing evidence: " + str(output))
    folder.mkdir(parents=True, exist_ok=True); output.write_bytes(result)
    report = {"generator": "Tests/FxPlacementAudit/fogger_material_name_control.py",
              "source": str(source_path), "sourceSha256": sha(raw), "output": str(output.resolve()),
              "outputSha256": sha(result), "bytes": len(result), "headerAndBodyId": new_id.decode(),
              "stageAt": "Items/" + new_id.decode().replace("\\", "/"),
              "oldMaterialName": OLD_NAME.decode(), "newMaterialName": name.decode(),
              "linkUnchanged": after.link, "allOtherMaterialFieldsExactlyEqual": True,
              "materialNameDictionaryDefinitionSlotRetained": True,
              "allFxMeshLodPointAndUnknownBytesExactlyPreserved": True,
              "sourceExactlyRestorable": True, "isFogEmitter": True, "fogEmissionRate": 100.0,
              "patches": [{"offset": p["offset"], "oldBytes": len(p["old"]), "newBytes": len(p["new"]), "kind": p["kind"]} for p in patches],
              "nativeVerdict": "Unstaged offline isolation. Source smoke was observed by runtime owner; control mesh/smoke/native-save not yet tested."}
    output.with_suffix(".material-name-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    p = argparse.ArgumentParser(); p.add_argument("--output-folder", type=Path, required=True)
    p.add_argument("--thick-frame", action="store_true"); a = p.parse_args()
    if a.thick_frame:
        source = SOURCE.parent.parent / "TMIS_CustomFoggerTower_ThickFrame_20261025.Item.Gbx"
        report = [generate(a.output_folder, b"TMIS_FoggerThickFrameB", "UniqueThickFrame", source,
            "391C764E18CCC5C16F2D25ABFE7ADB95877BB42D3BCD9862706EB27988C0B372",
            rb"TMIS_FX_SequentialProof\TMIS_CustomFoggerTower_ThickFrame_20261025.Item.Gbx")]
    else:
        report = [generate(a.output_folder, b"PlatformTech", "PlatformTech"),
                  generate(a.output_folder, b"TMIS_FoggerMeshA", "Unique")]
    print(json.dumps(report, indent=2))
