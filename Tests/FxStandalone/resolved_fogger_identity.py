"""Give the resolved native Fogger control its own full catalog identity.

Only the existing header/body Id definition text and lengths change. Both
dictionary slots, author, name, description and opaque graph are retained.
"""
from pathlib import Path
import argparse
import json
import struct

from fogger import SOURCE_ID, gpu_record, sha
from full_path_ids_control import replace_definition
from gbx_container import GbxContainer
from create_resolved_fogger import TEXTURE

PINNED = "C041F94AABCB8B8D25A34E76BB561E43D0E1A1831DD2926DAD217171822B21AE"


def generate(source: Path, output: Path) -> dict:
    original = source.read_bytes()
    if sha(original) != PINNED:
        raise ValueError("requires the exact resolved-path Fogger control")
    container = GbxContainer.read(original)
    new_id = ("TMIS_FX_SequentialProof\\" + output.name).encode("utf-8")
    if not output.name.endswith(".Item.Gbx") or b".." in new_id:
        raise ValueError("expected a simple unique native item filename")
    header_size, count = struct.unpack_from("<II", container.prefix, 13)
    first_id, first_size = struct.unpack_from("<II", container.prefix, 21)
    payload = 21 + count * 8
    if not 1 <= count <= 32 or first_id != 0x2E001003 or struct.unpack_from("<I", container.prefix, payload)[0] != 3:
        raise ValueError("native collector header layout differs")
    header_tag = payload + 4
    body_tag = container.body.index(struct.pack("<I", 0x2E00100B), 0, 350) + 4
    prefix = bytearray(replace_definition(container.prefix, header_tag, SOURCE_ID, new_id))
    delta = len(new_id) - len(SOURCE_ID)
    struct.pack_into("<I", prefix, 13, header_size + delta)
    struct.pack_into("<I", prefix, 25, (first_size & 0x80000000) | ((first_size & 0x7FFFFFFF) + delta))
    body = replace_definition(container.body, body_tag, SOURCE_ID, new_id)
    restored_prefix = bytearray(replace_definition(prefix, header_tag, new_id, SOURCE_ID))
    struct.pack_into("<I", restored_prefix, 13, header_size)
    struct.pack_into("<I", restored_prefix, 25, first_size)
    if bytes(restored_prefix) != container.prefix or replace_definition(body, body_tag, new_id, SOURCE_ID) != container.body:
        raise ValueError("complete original metadata/graph was not exactly restored")
    result = GbxContainer(bytes(prefix), body, container.compression, container.node_count, container.references).write(body)
    reopened = GbxContainer.read(result)
    _, gpu = gpu_record(reopened)
    if reopened.node_count != 15 or reopened.references or not gpu.IsFogEmitter or gpu.FogEmissionRate != 100.0 or not gpu.isTextureFid or gpu.texture != TEXTURE:
        raise ValueError("resolved native Fogger fields changed")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    report = {"generator": "Tests/FxStandalone/resolved_fogger_identity.py",
              "source": str(source.resolve()), "sourceSha256": sha(original),
              "output": str(output.resolve()), "outputSha256": sha(result), "bytes": len(result),
              "stageAt": "Items/" + new_id.decode().replace("\\", "/"),
              "oldHeaderAndBodyId": SOURCE_ID.decode(), "newHeaderAndBodyId": new_id.decode(),
              "originalHeaderAndBodyDefinitionSlotsPreserved": True,
              "authorNameDescriptionAndEveryOtherMetadataFieldPreserved": True,
              "allGraphMeshPlacementEmitterAndTextureBytesPreserved": True,
              "originalSourceCompletelyRestorable": True,
              "bitmapFid": TEXTURE, "isFogEmitter": True, "fogEmissionRate": 100.0,
              "nativeVerdict": "pending native preload/catalog/placement/render/save/reopen of this exact identity"}
    output.with_suffix(".identity-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output), indent=2))
