"""Offline stock-FID Fogger visibility controls; never stage or operate the game.

The source mesh and item-space emitter are already bounded edits. Copy the
known-visible Sparkler material configuration, retaining the Fogger's existing
material-name lookback definition, and add one native LOD distance. The optional
sprite control changes only IsFogEmitter in addition to its unique catalog ID.
"""
from __future__ import annotations

import argparse
import io
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from fogger import gpu_record, sha
from full_path_ids_control import replace_definition
from gbx_container import GbxContainer
from visible_fogger import splice
from src.gbx_structs import body_chunks

SOURCE = ROOT / "artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/TMIS_CustomFoggerTower_FullPath_20261025.Item.Gbx"
SOURCE_HASH = "E633DBDA9FCD10D285FCD8A11D66BEEB42FBFEE64F8296E871B3167F03D742D1"
OLD_ID = rb"TMIS_FX_SequentialProof\TMIS_CustomFoggerTower_FullPath_20261025.Item.Gbx"
DONOR = ROOT / "artifacts/fx-clones/fx-fid-path-matrix-20261025/TMIS_Sparkler_FidStadiumBackslash_20261025.Item.Gbx"
DONOR_HASH = "3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44"
TEXTURE = r"Stadium\Media\Texture\FoggerSmoke.Texture.gbx"


def material(data, expected_offset, expected_end):
    signature = struct.pack("<IIII", 6, 0x090FD000, 0x090FD000, 11) + b"\x01"
    if data.count(signature) != 1 or data.index(signature) != expected_offset:
        raise ValueError("native material node/version/using-game-material signature differs")
    if data[expected_end - 4:expected_end] != struct.pack("<I", 0xFACADE01):
        raise ValueError("material node end differs")
    return data[expected_offset:expected_end]


def parse_material(data, offset):
    context = {"lookbackstring_table": {1: "source-id", 2: "source-author"},
               "lookbackstring_index": 2, "lookbackstring_version": True}
    stream = io.BytesIO(data)
    stream.seek(offset + 12)
    typed = body_chunks[0x090FD000].parse_stream(stream, gbx_data=context,
        nodes=[None] * 15, load_external_file=lambda _: None)
    return typed, context


def generate(output_folder: Path, sprites: bool):
    raw = SOURCE.read_bytes(); donor_raw = DONOR.read_bytes()
    if sha(raw) != SOURCE_HASH or sha(donor_raw) != DONOR_HASH:
        raise ValueError("source/donor must be the exact reviewed native controls")
    source = GbxContainer.read(raw); donor = GbxContainer.read(donor_raw)
    old = material(source.body, 6072, 6254)
    native = material(donor.body, 1794, 1936)
    original_name = struct.pack("<II", 0x40000000, 15) + b"TM_AddSelfIllum"
    if old[17:40] != original_name or native[17:21] != b"\xff" * 4:
        raise ValueError("material-name dictionary definitions differ")
    replacement = native[:17] + original_name + native[21:]
    expected_path = rb"Stadium\Media\Material\PlatformTech"
    if replacement.count(expected_path) != 1:
        raise ValueError("expected the exact visible Sparkler game-material link")
    old_typed, old_context = parse_material(source.body, 6072)
    material_typed, material_context = parse_material(replacement, 0)
    donor_typed, _ = parse_material(native, 0)
    if material_context != old_context or material_typed.materialName != old_typed.materialName:
        raise ValueError("material definition slots or existing name changed")
    for key in donor_typed:
        if key.startswith("_") or key == "materialName": continue
        if material_typed[key] != donor_typed[key]:
            raise ValueError("replacement differs from donor material configuration: " + key)

    file_name = "TMIS_CustomFoggerTower_" + ("SpriteVisible" if sprites else "VolumeVisible") + "_20261025.Item.Gbx"
    new_id = ("TMIS_FX_SequentialProof\\" + file_name).encode()
    header_size, count = struct.unpack_from("<II", source.prefix, 13)
    first_id, first_size = struct.unpack_from("<II", source.prefix, 21)
    header_tag = 21 + count * 8 + 4
    body_tag = source.body.index(struct.pack("<I", 0x2E00100B), 0, 350) + 4
    if first_id != 0x2E001003 or not 1 <= count <= 32:
        raise ValueError("collector header layout differs")
    old_definition = struct.pack("<II", 0x40000000, len(OLD_ID)) + OLD_ID
    new_definition = struct.pack("<II", 0x40000000, len(new_id)) + new_id
    if source.body[body_tag:body_tag + len(old_definition)] != old_definition:
        raise ValueError("source collector body identity differs")
    if struct.unpack_from("<iII", source.body, 5977) != (-1, 0, 1):
        raise ValueError("empty source LOD array signature differs")
    patches = [
        {"offset": body_tag, "old": old_definition, "new": new_definition, "kind": "unique existing body ID definition"},
        {"offset": 5981, "old": struct.pack("<I", 0), "new": struct.pack("<If", 1, 1000000.0), "kind": "one native LOD maximum distance"},
        {"offset": 6072, "old": old, "new": replacement, "kind": "exact PlatformTech configuration, retaining existing material-name definition"},
    ]
    selector_offset = None
    if sprites:
        tail = struct.pack("<IIf3f", 1, 1, 100.0, 0.0, 0.0, 2.0)
        if source.body.count(tail) != 1:
            raise ValueError("independently typed native positive-fog tail differs")
        selector_offset = source.body.index(tail) + 4
        patches.append({"offset": selector_offset, "old": struct.pack("<I", 1),
                        "new": struct.pack("<I", 0), "kind": "IsFogEmitter false only"})
    body = splice(source.body, patches)
    if splice(body, patches, reverse=True) != source.body:
        raise ValueError("all source graph bytes are not exactly restorable")
    delta = len(new_id) - len(OLD_ID)
    prefix = bytearray(replace_definition(source.prefix, header_tag, OLD_ID, new_id))
    struct.pack_into("<I", prefix, 13, header_size + delta)
    struct.pack_into("<I", prefix, 25, (first_size & 0x80000000) | ((first_size & 0x7fffffff) + delta))
    restored = bytearray(replace_definition(prefix, header_tag, new_id, OLD_ID))
    struct.pack_into("<I", restored, 13, header_size); struct.pack_into("<I", restored, 25, first_size)
    if restored != source.prefix: raise ValueError("source header is not exactly restorable")
    result = GbxContainer(bytes(prefix), body, source.compression, source.node_count, source.references).write(body)
    reopened = GbxContainer.read(result); _, gpu = gpu_record(reopened)
    if reopened.node_count != 15 or reopened.references or gpu.texture != TEXTURE or not gpu.isTextureFid:
        raise ValueError("native Fogger graph or accepted stock path changed")
    if bool(gpu.IsFogEmitter) != (not sprites) or gpu.FogEmissionRate != 100.0:
        raise ValueError("fog selector/rate differs from the single-field control")
    point = b"float3(0,3.5,0)"
    if reopened.body.count(point) != 1: raise ValueError("native point changed")
    output_folder.mkdir(parents=True, exist_ok=True); output = output_folder / file_name
    if output.exists(): raise FileExistsError("preserve existing evidence: " + str(output))
    output.write_bytes(result)
    report = {
        "generator": "Tests/FxPlacementAudit/resolved_fogger_visibility.py",
        "source": str(SOURCE), "sourceSha256": sha(raw), "donor": str(DONOR), "donorSha256": sha(donor_raw),
        "output": str(output), "outputSha256": sha(result), "bytes": len(result),
        "stageAt": "Items/" + new_id.decode().replace("\\", "/"), "headerAndBodyId": new_id.decode(),
        "materialNameDefinitionRetained": "TM_AddSelfIllum", "materialConfigurationMatchesDonorExceptName": True,
        "lookbackDefinitionSlotsPreserved": True, "nativeMaterialLink": expected_path.decode(), "lodDistances": [1000000],
        "meshBounds": {"x": [-1.3, 1.3], "y": [0, 3], "z": [-1.3, 1.3]},
        "nativePoint": point.decode(), "bitmapFid": TEXTURE, "isFogEmitter": bool(gpu.IsFogEmitter),
        "fogEmissionRate": gpu.FogEmissionRate, "spriteSourceSelectorOffset": selector_offset,
        "nodeCount": 15, "externalReferenceCount": 0, "authorNameDescriptionUnchanged": True,
        "sourceExactlyRestorable": True,
        "patches": [{"offset": p["offset"], "oldBytes": len(p["old"]), "newBytes": len(p["new"]), "kind": p["kind"]} for p in patches],
        "nativeVerdict": "Offline only; native load/visibility/save/reopen unverified. No staging or game operations."
    }
    output.with_suffix(".visibility-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--output-folder", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps([generate(args.output_folder, False), generate(args.output_folder, True)], indent=2))
