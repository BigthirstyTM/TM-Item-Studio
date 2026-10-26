"""Rewrite native Fogger header/body identities without rebuilding its graph.

Same-width string substitutions retain header sizes, body offsets and lookback
table slots. This is offline only; stage the output at manifest.stageAt exactly.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


SOURCE_ID = rb"TMIS_FX_NativeCopy\TMIS_FoggerEmbeddedNativeClone.Item.Gbx"
SOURCE_NAME = b"FogVolumetric1"
AUTHOR = b"-oTBhm4_S1-UxnlnBizUDA"
FIXTURES = {
    "DD9850B2C6842F27DA35D0CE136CC51B5D1BDA2FF000719CBB949FA60FD01E83": {
        "id": r"TMIS_FX_SequentialProof\CustomFogger_VolumeTower1.Item.Gbx",
        "name": "VolumeTower FX", "IsFogEmitter": True},
    "5A0AC1D5332C51A6DB87CDA82F23F96BB2622475C5258EEAAD096012BB970B1B": {
        "id": r"TMIS_FX_SequentialProof\CustomFogger_SpriteTower1.Item.Gbx",
        "name": "SpriteTower FX", "IsFogEmitter": False},
}


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def patch_string(data: bytes, old: bytes, new: bytes, field: str) -> tuple[bytes, dict]:
    if len(old) != len(new):
        raise ValueError(f"{field} rewrite must retain exact UTF-8 byte length")
    if data.count(old) != 1:
        raise ValueError(f"{field} must occur exactly once in its bounded section")
    offset = data.index(old)
    if struct.unpack_from("<I", data, offset - 4)[0] != len(old):
        raise ValueError(f"{field} is not the expected length-prefixed string")
    result = data[:offset] + new + data[offset + len(old):]
    return result, {"field": field, "offset": offset, "bytes": len(old),
                    "old": old.decode("ascii"), "new": new.decode("ascii")}


def generate(source: Path, output_folder: Path) -> dict:
    original = source.read_bytes()
    source_hash = sha(original)
    if source_hash not in FIXTURES:
        raise ValueError("source is not one of the pinned embedded Fogger tower fixtures")
    fixture = FIXTURES[source_hash]
    container = GbxContainer.read(original)
    prefix, body = container.prefix, container.body
    # Retain both matching native authors; only ID text in the existing Id
    # dictionary entry changes. All subsequent dictionary indices stay intact.
    for section, data in (("header", prefix), ("body", body)):
        if data.count(AUTHOR) != 1:
            raise ValueError(f"{section} author differs from the pinned native identity")
    id_bytes = fixture["id"].encode("ascii")
    name_bytes = fixture["name"].encode("ascii")
    edits = []
    prefix, edit = patch_string(prefix, SOURCE_ID, id_bytes, "header identity")
    edits.append(edit)
    body, edit = patch_string(body, SOURCE_ID, id_bytes, "body identity")
    edits.append(edit)
    prefix, edit = patch_string(prefix, SOURCE_NAME, name_bytes, "header display name")
    edits.append(edit)
    body, edit = patch_string(body, SOURCE_NAME, name_bytes, "body display name")
    edits.append(edit)
    # Identity has the same first-entry lookback token before its length word.
    for data in (prefix, body):
        at = data.index(id_bytes)
        if struct.unpack_from("<I", data, at - 8)[0] != 0x40000000:
            raise ValueError("native identity lookback-table slot changed")
    if prefix.replace(id_bytes, SOURCE_ID).replace(name_bytes, SOURCE_NAME) != container.prefix:
        raise ValueError("non-metadata header byte changed")
    if body.replace(id_bytes, SOURCE_ID).replace(name_bytes, SOURCE_NAME) != container.body:
        raise ValueError("non-metadata native body byte changed")
    result = GbxContainer(prefix, body, container.compression,
                          container.node_count, container.references).write(body)
    filename = fixture["id"].rsplit("\\", 1)[1]
    output = output_folder / filename
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {"generator": "Tests/FxStandalone/identity.py",
                "source": str(source.resolve()), "sourceSha256": source_hash,
                "output": str(output.resolve()), "bytes": len(result), "sha256": sha(result),
                "stageAt": "Items/" + fixture["id"].replace("\\", "/"),
                "headerId": fixture["id"], "bodyId": fixture["id"],
                "headerAuthor": AUTHOR.decode(), "bodyAuthor": AUTHOR.decode(),
                "displayName": fixture["name"], "IsFogEmitter": fixture["IsFogEmitter"],
                "metadataEdits": edits, "lookbackTableSlotsPreserved": True,
                "allFxMeshAndPlacementBytesUnchanged": True,
                "sourceExactlyRecoverable": True,
                "nativeVerdict": "pending identity-specific cold preload/placement"}
    output.with_suffix(".identity-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output-folder", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output_folder), indent=2))
