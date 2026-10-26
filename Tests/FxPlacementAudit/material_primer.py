"""Append a textured native mesh entity sharing an existing inline FX bitmap.

Only an isolated file control. No native runtime action and no renderer claim.
The FX archive remains exact; placement NodeRefs move to preserve sequential
native first-use ordering after the new third prefab entity.
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "FxStandalone"))
from gbx_container import GbxContainer

SOURCE_SHA = "EA88213156B702F749454DCD0D1979DCF922AEF0720404E99B29AE54CE2F8C83"
DONOR_NODE_SLOTS = ((28,1,0x09159000), (40,2,0x090BB000),
                    (96,3,0x0901E000), (160,4,0x09056000),
                    (1416,5,0x09079000), (1436,6,0x0903A000),
                    (1561,7,0x09011000), (2043,9,0x0900C000))
REMAP = {1:23,2:24,3:25,4:26,5:27,6:28,7:14,8:29,9:30}
SHADER = rb"GameData\Techno3\Media\Material\Tech3 Block TAddModCV.Material.gbx"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def generate(source: Path, donor: Path, output: Path):
    original, donor_raw = source.read_bytes(), donor.read_bytes()
    if sha(original) != SOURCE_SHA:
        raise ValueError("source must be cold-placeable header-null Studio Sparkler")
    src, other = GbxContainer.read(original), GbxContainer.read(donor_raw)
    positions = json.loads(Path(str(donor)+".positions.json").read_text(encoding="utf-8"))
    if positions["sourceSha256"] != SOURCE_SHA or src.node_count != 25 or src.references:
        raise ValueError("source/donor boundary proof differs")
    if other.node_count != 10 or len(other.references) != 1 or other.references[0]["nodeIndex"] != 8 or other.references[0]["path"] != SHADER.decode():
        raise ValueError("unexpected donor graph or native shader dependency")
    piece = bytearray(other.body)
    for offset, old, class_id in DONOR_NODE_SLOTS:
        if struct.unpack_from("<II",piece,offset) != (old,class_id):
            raise ValueError("recorded donor definition changed")
        struct.pack_into("<I",piece,offset,REMAP[old])
    if struct.unpack_from("<II",piece,1822) != (0x09079015,7) or struct.unpack_from("<I",piece,1830)[0] != 8:
        raise ValueError("native material parent shader slot changed")
    struct.pack_into("<I",piece,1830,REMAP[8])
    # Empty bitmap is only a stand-in for a reference to the already defined
    # first FX CPlugBitmap, node14. It has no chunks or nested nodes.
    if piece[1565:1573] != struct.pack("<II",0x09011000,0xFACADE01):
        raise ValueError("placeholder bitmap definition differs")
    piece = piece[:1565] + piece[1573:]
    if struct.unpack_from("<I",piece,56)[0] != 3:
        raise ValueError("donor first lookback stream version differs")
    piece = piece[:56] + piece[60:]
    # Root prefab body header is not an entity record. There is no prefab
    # footer: its array ends immediately after the last 36-byte owner record.
    piece = bytes(piece[28:])
    at = positions["sourceNextEntityInsertOffset"]
    count_at = positions["sourcePrefabEntCountOffset"]
    if struct.unpack_from("<I",src.body,count_at)[0] != 2 or src.body[at-8:at] != struct.pack("<II",0xFFFFFFFF,0):
        raise ValueError("source two-owner prefab boundary differs")
    body = bytearray(src.body)
    placement_patches = []
    for old,new,class_id in ((23,31,0x2E020000),(24,32,0x09187000)):
        signature = struct.pack("<II",old,class_id)
        offset = body.find(signature,at,min(len(body),at+500))
        if offset < 0 or body.find(signature,offset+1,min(len(body),at+500)) >= 0:
            raise ValueError("unique placement definition missing")
        struct.pack_into("<I",body,offset,new)
        placement_patches.append({"sourceOffset":offset,"old":old,"new":new,"classId":f"0x{class_id:08X}"})
    struct.pack_into("<I",body,count_at,3)
    body[at:at] = piece
    recovered = bytearray(body[:at] + body[at+len(piece):])
    struct.pack_into("<I",recovered,count_at,2)
    for patch in placement_patches:
        struct.pack_into("<I",recovered,patch["sourceOffset"],patch["old"])
    if bytes(recovered) != src.body:
        raise ValueError("complete original body not exactly restorable")
    header_size = struct.unpack_from("<I",src.prefix,13)[0]
    prefix = src.prefix[:17+header_size] + struct.pack("<IIII",33,1,0,0)
    prefix += struct.pack("<II",1,len(SHADER)) + SHADER + struct.pack("<III",29,0,0)
    result = GbxContainer(prefix,bytes(body),src.compression,33,()).write(bytes(body))
    reopened = GbxContainer.read(result)
    if reopened.node_count != 33 or len(reopened.references) != 1:
        raise ValueError("rewritten container/reference table differs")
    graph_at = positions["sourceFxGraphOffset"]-4
    graph_bytes = positions["sourceFxGraphBytes"]
    if body[graph_at:graph_at+graph_bytes] != src.body[graph_at:graph_at+graph_bytes]:
        raise ValueError("FX graph unexpectedly changed")
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_bytes(result)
    report = {"generator":"Tests/FxPlacementAudit/material_primer.py",
              "source":str(source.resolve()),"sourceSha256":sha(original),
              "donor":str(donor.resolve()),"donorSha256":sha(donor_raw),
              "output":str(output.resolve()),"bytes":len(result),"sha256":sha(result),
              "stageAt":"Items/TMIS_FX_SequentialProof/"+output.name,
              "newPrefabEntityIndex":2,"primerPosition":[5,0,0],
              "sampler":"SelfIllum","sharedBitmapNodeIndex":14,
              "nativeShaderDependency":SHADER.decode(),
              "shaderRefNodeIndex":29,"nodeCount":33,
              "thirdEntityBodyOffset":at,"thirdEntityBytes":len(piece),
              "placementReferencePatches":placement_patches,
              "firstUseNodeOrderSequential":list(range(1,33)),
              "originalFxGraphBytesExactlyUnchanged":True,
              "completeOriginalBodyExactlyRestorable":True,
              "headerAndBodyIdsRetainNativeNull":True,
              "nativeVerdict":"pending isolated cold preload/place/render; file-only material upload hypothesis"}
    output.with_suffix(".primer-manifest.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source",type=Path,required=True)
    parser.add_argument("--donor",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    print(json.dumps(generate(args.source,args.donor,args.output),indent=2))
