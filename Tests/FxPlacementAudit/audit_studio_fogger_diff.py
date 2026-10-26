"""Pinned offline source/Studio binary bisection; never stages or loads items."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from gbx_container import GbxContainer

SOURCE_HASH = "8493C5D71BD141DDAC594D150864AB13D2CAED42DF480DE8C4D682EE40251269"
EXPORT_HASH = "DA6C92A686303BC7D2C502E8BA05D736546FA0E303535CD08314777298B6C211"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def string_field(data, offset):
    length = struct.unpack_from("<I", data, offset)[0]
    if length > 4096 or offset + 4 + length > len(data):
        raise ValueError("invalid bounded string field")
    text = data[offset + 4:offset + 4 + length].decode("utf-8")
    return {"offset": offset, "text": text, "bytes": length}, offset + 4 + length


def metadata(body):
    pos = 0

    def word(expected=None):
        nonlocal pos
        value = struct.unpack_from("<I", body, pos)[0]
        pos += 4
        if expected is not None and value != expected:
            raise ValueError(f"unexpected metadata integer {value:x}")
        return value

    word(0x2E001009)
    _, pos = string_field(body, pos)
    word(0); word(3); word(0xFFFFFFFF); word(0x2E00100B); word(0x40000000)
    ident, pos = string_field(body, pos)
    collection = word(); word(0x40000000)
    author, pos = string_field(body, pos)
    word(0x2E00100C)
    name, pos = string_field(body, pos)
    word(0x2E00100D)
    description, pos = string_field(body, pos)
    word(0x2E001010)
    return {"id": ident, "author": author, "name": name, "description": description,
            "collection": collection, "metadataEnd": pos - 4, "definitionCount": 2}


def encode(text):
    raw = text.encode("utf-8")
    return struct.pack("<I", len(raw)) + raw


def splice(data, patches, reverse=False):
    chunks = []; cursor = 0; delta = 0
    for patch in sorted(patches, key=lambda patch: patch["offset"]):
        old, new = encode(patch["before"]), encode(patch["after"])
        offset = patch["offset"] + (delta if reverse else 0)
        expected, replacement = (new, old) if reverse else (old, new)
        if offset < cursor or data[offset:offset + len(expected)] != expected:
            raise ValueError("splice does not match pinned source field")
        chunks.extend((data[cursor:offset], replacement))
        cursor = offset + len(expected)
        delta += len(new) - len(old)
    chunks.append(data[cursor:])
    return b"".join(chunks)


def chunks(prefix):
    header_size, count = struct.unpack_from("<II", prefix, 13)
    pos = 21 + count * 8
    result = []
    for index in range(count):
        ident, size = struct.unpack_from("<II", prefix, 21 + index * 8)
        payload = prefix[pos:pos + (size & 0x7FFFFFFF)]
        result.append({"id": ident, "size": size, "offset": pos, "data": payload})
        pos += len(payload)
    if pos != 17 + header_size:
        raise ValueError("header chunk sizes do not consume the declared header")
    return result


def local_field(body):
    # Pinned single-emitter Fogger: only the local offset follows exactly the
    # model FACADE and null JointName. Validate the complete emitter tail.
    marker = bytes.fromhex("01decafaffffffff")
    candidates = []
    start = 0
    while (at := body.find(marker, start)) >= 0:
        start = at + 1
        try:
            field, pos = string_field(body, at + 8)
            if not field["text"].startswith("float3("):
                continue
            for _ in range(9):
                _, pos = string_field(body, pos)
            if struct.unpack_from("<I", body, pos)[0] not in (0, 1):
                continue
            pos += 4
            for _ in range(2):
                _, pos = string_field(body, pos)
            if body[pos:pos + 20] != struct.pack("<5I", 0xFFFFFFFF, 0xFFFFFFFF, 0, 55, 0xFACADE01):
                continue
            candidates.append(field)
        except (ValueError, UnicodeDecodeError, struct.error):
            pass
    if len(candidates) != 1:
        raise ValueError("not the unique complete pinned Fogger emitter tail")
    return candidates[0]


def audit(source, exported, output_folder):
    source_bytes, export_bytes = source.read_bytes(), exported.read_bytes()
    if sha(source_bytes) != SOURCE_HASH or sha(export_bytes) != EXPORT_HASH:
        raise ValueError("this investigation requires the exact pinned source and crashed Studio export")
    first, second = GbxContainer.read(source_bytes), GbxContainer.read(export_bytes)
    if first.node_count != second.node_count or first.references != second.references or first.compression != second.compression:
        raise ValueError("unexpected node/reference/compression change")
    before, after = metadata(first.body), metadata(second.body)
    if before["author"]["text"] != after["author"]["text"] or before["collection"] != after["collection"]:
        raise ValueError("unexpected author/collection change")
    patches = [{"field": key, "offset": before[key]["offset"], "before": before[key]["text"],
                "after": after[key]["text"]} for key in ("id", "name", "description")]
    source_local, export_local = local_field(first.body), local_field(second.body)
    point_patch = {"field": "LocalOffsetExpr", "offset": source_local["offset"],
                   "before": source_local["text"], "after": export_local["text"]}
    all_patches = patches + [point_patch]
    if splice(first.body, all_patches) != second.body or splice(second.body, all_patches, reverse=True) != first.body:
        raise ValueError("unexplained body difference outside the four source fields")

    header_before, header_after = chunks(first.prefix), chunks(second.prefix)
    if [chunk["id"] for chunk in header_before] != [chunk["id"] for chunk in header_after]:
        raise ValueError("header chunk set/order changed")
    if any(a["data"] != b["data"] for a, b in zip(header_before[1:], header_after[1:])):
        raise ValueError("non-collector header chunk changed")
    collector = header_before[0]
    if collector["id"] != 0x2E001003:
        raise ValueError("collector is not the first header chunk")
    old_payload = collector["data"]
    header_patches = []
    for key in ("id", "name"):
        encoded = encode(before[key]["text"])
        if old_payload.count(encoded) != 1:
            raise ValueError("header source metadata string is not unique")
        header_patches.append({"field": key, "offset": old_payload.index(encoded),
                               "before": before[key]["text"], "after": after[key]["text"]})
    new_payload = splice(old_payload, header_patches)
    if new_payload != header_after[0]["data"]:
        raise ValueError("unexplained collector-header payload change")
    expected_prefix = bytearray(first.prefix[:collector["offset"]] + new_payload
                                + first.prefix[collector["offset"] + len(old_payload):])
    delta = len(new_payload) - len(old_payload)
    struct.pack_into("<I", expected_prefix, 13, struct.unpack_from("<I", first.prefix, 13)[0] + delta)
    struct.pack_into("<I", expected_prefix, 25, collector["size"] + delta)
    if bytes(expected_prefix) != second.prefix:
        raise ValueError("unexplained container/header/reference bytes changed")

    output_folder.mkdir(parents=True, exist_ok=True)
    controls = []
    variants = [
        ("Fogger_Source_PointOnly.Item.Gbx", first.prefix, splice(first.body, [point_patch]),
         "Exact known-safe source except LocalOffsetExpr float3(0) -> float3(0,3.5,0); original ID/name/author/header retained."),
        ("Fogger_Studio_MetadataOnly.Item.Gbx", second.prefix, splice(first.body, patches),
         "Exact crashed Studio export except native point restored to original float3(0); all Studio metadata retained."),
    ]
    for filename, prefix, body, purpose in variants:
        data = GbxContainer(prefix, body, first.compression, first.node_count, first.references).write(body)
        destination = output_folder / filename
        destination.write_bytes(data)
        controls.append({"file": str(destination.resolve()), "bytes": len(data), "sha256": sha(data),
                         "purpose": purpose, "nativeStatus": "NOT staged or tested", "localOffset": local_field(body)["text"],
                         "identity": {key: metadata(body)[key]["text"] for key in ("id", "author", "name", "description")}})

    report = {"source": str(source.resolve()), "sourceSha256": sha(source_bytes),
              "crashedExport": str(exported.resolve()), "crashedExportSha256": sha(export_bytes),
              "nodeCount": first.node_count, "externalReferences": list(first.references), "compression": first.compression,
              "sourceBodyBytes": len(first.body), "exportBodyBytes": len(second.body),
              "sourcePrefixBytes": len(first.prefix), "exportPrefixBytes": len(second.prefix),
              "bodyFields": all_patches, "bodyEntirelyExplainedByFourStringSplices": True,
              "bodyReverseRestoresSourceExactly": True, "headerEntirelyExplainedByIdNameAndTwoSizeWords": True,
              "allOtherHeaderChunksAndRefTableExact": True, "originalLookbackDefinitionCount": 2,
              "outputLookbackDefinitionCount": 2, "noScientificNotationInFoggerOffset": "e" not in export_local["text"].lower(),
              "conclusion": "No unexplained structural byte change found. These two orthogonal offline controls separate the shared origin change from metadata/ID policy. Native results must be recorded independently; do not stage both simultaneously under duplicate source identities.",
              "controls": controls}
    (output_folder / "audit.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--exported", type=Path, required=True)
    parser.add_argument("--output-folder", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(audit(args.source, args.exported, args.output_folder), indent=2))
