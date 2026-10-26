"""Isolated full-path Fogger identity control from the actual Studio export.

Replaces existing nonempty header/body ID definitions in their original slots.
All graph/mesh/emission/pixel bytes remain exact. No live staging is performed.
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
from gbx_container import GbxContainer

PINNED = "DA6C92A686303BC7D2C502E8BA05D736546FA0E303535CD08314777298B6C211"
OLD_ID = b"TMIS_Standalone_VolumeFogger_20261025"
NEW_ID = rb"TMIS_FX_SequentialProof\TMIS_Standalone_VolumeFogger_FullPath_20261025.Item.Gbx"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def replace_definition(data, offset, old, new):
    if struct.unpack_from("<II", data, offset) != (0x40000000, len(old)):
        raise ValueError("expected existing nonempty native ID definition")
    text = offset + 8
    if data[text:text + len(old)] != old:
        raise ValueError("unexpected native ID text")
    return data[:offset + 4] + struct.pack("<I", len(new)) + new + data[text + len(old):]


def generate(source, output):
    original = source.read_bytes()
    if sha(original) != PINNED:
        raise ValueError("source must be actual original-bitmap Studio Fogger export")
    container = GbxContainer.read(original)
    prefix = container.prefix
    header_size, count = struct.unpack_from("<II", prefix, 13)
    if not 1 <= count <= 32:
        raise ValueError("unsupported header count")
    first_id, first_size = struct.unpack_from("<II", prefix, 21)
    payload = 21 + count * 8
    if first_id != 0x2E001003 or struct.unpack_from("<I", prefix, payload)[0] != 3:
        raise ValueError("unsupported collector header")
    header_tag = payload + 4
    body_chunk = container.body.index(struct.pack("<I", 0x2E00100B), 0, 350)
    body_tag = body_chunk + 4
    delta = len(NEW_ID) - len(OLD_ID)
    new_prefix = bytearray(replace_definition(prefix, header_tag, OLD_ID, NEW_ID))
    struct.pack_into("<I", new_prefix, 13, header_size + delta)
    struct.pack_into("<I", new_prefix, 25, (first_size & 0x80000000) | ((first_size & 0x7FFFFFFF) + delta))
    body = replace_definition(container.body, body_tag, OLD_ID, NEW_ID)
    # Reverse both exact splices, including the two header-size changes.
    restored_prefix = bytearray(replace_definition(bytes(new_prefix), header_tag, NEW_ID, OLD_ID))
    struct.pack_into("<I", restored_prefix, 13, header_size)
    struct.pack_into("<I", restored_prefix, 25, first_size)
    if bytes(restored_prefix) != prefix or replace_definition(body, body_tag, NEW_ID, OLD_ID) != container.body:
        raise ValueError("unexplained metadata/body changes")
    result = GbxContainer(bytes(new_prefix), body, container.compression, container.node_count, container.references).write(body)
    reopened = GbxContainer.read(result)
    if reopened.body != body or reopened.node_count != container.node_count or reopened.references != container.references:
        raise ValueError("rewritten native container differs")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    report = {"generator": "Tests/FxStandalone/full_path_ids_control.py",
              "source": str(source.resolve()), "sourceSha256": sha(original),
              "output": str(output.resolve()), "bytes": len(result), "sha256": sha(result),
              "oldHeaderAndBodyId": OLD_ID.decode(), "newHeaderAndBodyId": NEW_ID.decode(),
              "stageAt": "Items/" + NEW_ID.decode().replace("\\", "/"),
              "headerBodyIdentityDefinitionTag": "0x40000000",
              "originalLookbackDefinitionSlotsPreserved": True,
              "sourceExactlyRestorable": True,
              "allMeshPointFxPixelAndUnknownBodyBytesPreserved": True,
              "authorNameDescriptionUnchanged": True,
              "nativeVerdict": "pending isolated cold preload/placement; nonempty full-path policy control only"}
    output.with_suffix(".full-path-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output), indent=2))
