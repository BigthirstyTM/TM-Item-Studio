"""Independent read-only audit of the pinned shared-bitmap material control.

Does not import the generator, reserialize a graph, stage a file or call the game.
Native upload and particle rendering remain separate acceptance gates.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


SOURCE_SHA = "EA88213156B702F749454DCD0D1979DCF922AEF0720404E99B29AE54CE2F8C83"
CANDIDATE_SHA = "C2F2A22472DFDAC2D1ABCCCD90CA2938755C6AE585D46E6460216611B1F57340"
CLASSES = (
    0x09145000, 0x09159000, 0x090BB000, 0x0901E000, 0x09056000,
    0x090FD000, 0x0900C000, 0x0915C000, 0x090B3000, 0x090B2000,
    0x090B5000, 0x090C5000, 0x090C6000, 0x09011000, 0x0902F000,
    0x090B3000, 0x090B2000, 0x090B5000, 0x090C5000, 0x090C6000,
    0x09011000, 0x0902F000, 0x09159000, 0x090BB000, 0x0901E000,
    0x09056000, 0x09079000, 0x0903A000, None, 0x0900C000,
    0x2E020000, 0x09187000,
)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def unique(data: bytes, needle: bytes) -> int:
    at = data.find(needle)
    if at < 0 or data.find(needle, at + 1) >= 0:
        raise ValueError(f"expected one bounded structural signature {needle.hex()}")
    return at


def audit(source: Path, candidate: Path) -> dict:
    source_raw, raw = source.read_bytes(), candidate.read_bytes()
    if digest(source_raw) != SOURCE_SHA or digest(raw) != CANDIDATE_SHA:
        raise ValueError("only the independently pinned control is in scope")
    original, control = GbxContainer.read(source_raw), GbxContainer.read(raw)
    if original.node_count != 25 or original.references or control.node_count != 33:
        raise ValueError("unexpected source/control graph counts")
    header_size = struct.unpack_from("<I", original.prefix, 13)[0]
    if control.prefix[:17 + header_size] != original.prefix[:17 + header_size]:
        raise ValueError("source native-null header changed")
    references = control.references
    if len(references) != 1 or references[0]["nodeIndex"] != 29 or references[0]["path"] != r"GameData\Techno3\Media\Material\Tech3 Block TAddModCV.Material.gbx":
        raise ValueError("unexpected shader dependency or external node index")
    at, size, count_at = 8395626, 2393, 376
    if len(control.body) != len(original.body) + size:
        raise ValueError("unexpected body length change")
    if struct.unpack_from("<I", original.body, count_at)[0] != 2 or struct.unpack_from("<I", control.body, count_at)[0] != 3:
        raise ValueError("prefab entity count differs")
    recovered = bytearray(control.body[:at] + control.body[at + size:])
    struct.pack_into("<I", recovered, count_at, 2)
    for offset, before, after, class_id in (
        (8395650, 23, 31, 0x2E020000), (8395772, 24, 32, 0x09187000),
    ):
        if struct.unpack_from("<II", recovered, offset) != (after, class_id):
            raise ValueError("placement definition relocation differs")
        struct.pack_into("<I", recovered, offset, before)
    if bytes(recovered) != original.body:
        raise ValueError("complete original body did not restore exactly")
    definitions = []
    for index, class_id in enumerate(CLASSES, 1):
        if class_id is None:
            continue
        offset = unique(control.body, struct.pack("<II", index, class_id))
        definitions.append({"index": index, "classId": f"0x{class_id:08X}", "bodyOffset": offset})
    # A native external slot has its first use in the material parent field,
    # rather than an inline class definition in the body.
    shader_chunk = unique(control.body[at:at + size], struct.pack("<III", 0x09079015, 7, 29)) + at
    first_uses = definitions + [{"index": 29, "external": True, "bodyOffset": shader_chunk + 8}]
    first_uses.sort(key=lambda row: row["bodyOffset"])
    if [row["index"] for row in first_uses] != list(range(1, 33)):
        raise ValueError("recorded structural first-use sequence differs")
    # The full material texture header includes the Id definition, sampler
    # name, texture U01, and reference14; it is not an arbitrary integer match.
    sampler = struct.pack("<IIII", 0x0903A013, 0, 1, 0x40000000)
    sampler += struct.pack("<I", 9) + b"SelfIllum" + struct.pack("<II", 0, 14)
    sampler_at = unique(control.body[at:at + size], sampler) + at
    texture_ref = sampler_at + len(sampler) - 4
    if texture_ref != 8397155:
        raise ValueError("shared texture reference offset differs")
    if any(row["index"] == 14 and row["bodyOffset"] >= at for row in definitions):
        raise ValueError("primer unexpectedly defines another bitmap14")
    # Source FX is node8 and ends before its original 36-byte owner record.
    graph_at = unique(original.body, struct.pack("<III", 8, 0x0915C000, 0x0915C000))
    graph_end = at - 36
    if control.body[graph_at:graph_end] != original.body[graph_at:graph_end]:
        raise ValueError("FX archive or original pixels changed")
    piece = control.body[at:at + size]
    # This pinned donor introduces SelfIllum after the complete FX graph and
    # contains no version3 flag1 lookback aliases into a donor dictionary.
    # Do not scan arbitrary flag2 patterns: unaligned native float/chunk bytes
    # can have the same bits. The donor's actual Id field is guarded above.
    donor_aliases = []
    for offset in range(len(piece) - 3):
        token = struct.unpack_from("<I", piece, offset)[0]
        if 0x40000001 <= token <= 0x40000030:
            donor_aliases.append({"offset": offset, "token": hex(token)})
    if donor_aliases:
        raise ValueError("donor Id aliases require an explicit independent remap")
    source_tail = original.body[at:]
    if any(struct.pack("<I", token) in source_tail for token in range(0x40000000, 0x40000010)):
        raise ValueError("source tail has Id tokens requiring a dictionary audit")
    return {
        "auditor": "Tests/FxStandalone/audit_material_primer.py",
        "sourceSha256": digest(source_raw), "candidateSha256": digest(raw),
        "candidate": str(candidate.resolve()), "nativeNullHeaderExactlyPreserved": True,
        "completeSourceBodyExactlyRestored": True,
        "originalFxGraphIncludingBothImagesExactlyPreserved": True,
        "fxGraphBodyRange": [graph_at, graph_end],
        "prefabEntities": 3, "nodeCount": 33, "newEntityBodyRange": [at, at + size],
        "firstUseIndices": [row["index"] for row in first_uses],
        "definitions": definitions, "externalShaderFirstUseOffset": shader_chunk + 8,
        "sampler": "SelfIllum", "sharedBitmapNodeIndex": 14,
        "sharedBitmapReferenceBodyOffset": texture_ref,
        "primerDonorFlag1LookbackAliasTokens": donor_aliases,
        "sourceTailFlag1IdTokens000000Through00000F": 0,
        "externalReferences": references,
        "verdict": "PASS offline structure/binding; native preload, GPU upload, rendering, save/reopen still required",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = audit(args.source, args.candidate)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
