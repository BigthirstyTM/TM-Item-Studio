"""Independent source-recovery and typed Id audit of the two Fogger merges.

Does not import the generator, write GBX data, stage files or operate the game.
"""
from pathlib import Path
import io
import json
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests/FxStandalone"))
from gbx_container import GbxContainer
from fogger import sha, gpu_record
from src.gbx_structs import GbxLookbackString, body_chunks
from construct import Struct

FOLDER = ROOT / "artifacts/fx-clones/fx-fogger-visible-carrier-merge-20261025"
SPARKLER = ROOT / "artifacts/fx-clones/fx-fid-path-matrix-20261025/TMIS_Sparkler_FidStadiumBackslash_20261025.Item.Gbx"
FOGGER = ROOT / "artifacts/fx-clones/fx-safe-fogger-tower-fullpath-20261025/visibility/TMIS_CustomFoggerTower_VolumeVisible_20261025.Item.Gbx"
CASES = (("Baseline", "3E1DCB13D524FF8D60AA24D2895432C8732C5FEF27D60262D00A9BE9905C2007"),
         ("Bell", "4960E49D3D666F3FCEA8C6502481BA8E0D821F6EC73DBB2870F413C6D7ACB479"))


def audit(label, expected):
    original = SPARKLER.read_bytes(); donor_raw = FOGGER.read_bytes()
    if sha(original) != "3C1F8FA89642AD33E918BAD8AC323CA8CE0A3EB2A40204B767C8AC8541863B44" or sha(donor_raw) != "82E1540DF19C9E3B226375BA176A1B5A80DEDAA483D2A9E1B5FA851BB660A079":
        raise ValueError("independent pinned source differs")
    path = FOLDER / ("TMIS_Fogger_VisibleCarrier" + label + "_20261025.Item.Gbx")
    raw = path.read_bytes()
    if sha(raw) != expected: raise ValueError("independently pinned candidate differs")
    source = GbxContainer.read(original); fogger = GbxContainer.read(donor_raw); item = GbxContainer.read(raw)
    if item.node_count != 16 or item.references: raise ValueError("wrong merged graph counts")
    header_end = 17 + struct.unpack_from("<I", source.prefix, 13)[0]
    prefix = bytearray(item.prefix); struct.pack_into("<I", prefix, header_end, 23)
    if bytes(prefix) != source.prefix: raise ValueError("unknown header/reference-table changes")
    # Actual typed Id fields: root definition, child name, submodel's u10 and
    # emitter model name. Only the root introduces a new dictionary entry.
    ctx = {"lookbackstring_table": {1: "5X7FYPqoTyWnW8fpyvCv5Q"},
           "lookbackstring_index": 1, "lookbackstring_version": True}
    params = {"gbx_data": ctx, "nodes": [None] * 16, "load_external_file": lambda _: None}
    stream = io.BytesIO(item.body)
    stream.seek(2395 + 24); root = Struct("name" / GbxLookbackString).parse_stream(stream, **params).name
    stream.seek(2395 + 49); child = Struct("name" / GbxLookbackString).parse_stream(stream, **params).name
    stream.seek(2395 + 85); submodel = body_chunks[0x090B202D].parse_stream(stream, **params).u10
    stream.seek(2395 + 1706); emitter = body_chunks[0x090B3001].parse_stream(stream, **params).name
    if [str(root), str(child), str(submodel), str(emitter)] != ["Fogger16m"] * 4 or ctx["lookbackstring_index"] != 2:
        raise ValueError("actual native Id fields do not resolve to the imported root")
    imported = bytearray(item.body[2395:4373])
    for offset, new, class_id in ((0,8,0x0915C000),(53,9,0x090B3000),(73,10,0x090B2000),
                                  (137,11,0x090B5000),(1201,12,0x090C5000),(1301,13,0x090C6000)):
        if struct.unpack_from("<II", imported, offset) != (new, class_id): raise ValueError("imported NodeRef/class differs")
        struct.pack_into("<I", imported, offset, new - 1)
    for offset in (49, 121, 1706):
        if struct.unpack_from("<I", imported, offset)[0] != 0x40000002: raise ValueError("typed lookback alias differs")
        struct.pack_into("<I", imported, offset, 0x40000004)
    if imported != fogger.body[6347:8325]: raise ValueError("whole original Fogger graph and identity owner did not restore")
    tail = bytearray(item.body[4373:])
    for offset, new, old, class_id in ((24,14,19,0x2E020000),(146,15,20,0x09187000)):
        if struct.unpack_from("<II", tail, offset) != (new, class_id): raise ValueError("placement NodeRef differs")
        struct.pack_into("<I", tail, offset, old)
    if tail != source.body[6324:]: raise ValueError("unknown post-prefab changes")
    before = bytearray(item.body[:2395])
    if label == "Bell":
        # The measured native stream has 24 Float3 positions, at574..861.
        # Compare everything else independently rather than repeating its shape formula.
        before[574:862] = source.body[574:862]
    if before != source.body[:2395]: raise ValueError("unknown static-carrier/prefab/material/normal/UV/index/collision changes")
    recovered = bytes(before) + source.body[2395:6324] + bytes(tail)
    if recovered != source.body: raise ValueError("complete original carrier body not restored")
    _, gpu = gpu_record(item)
    if not gpu.IsFogEmitter or gpu.FogEmissionRate != 100.0 or gpu.texture != r"Stadium\Media\Texture\FoggerSmoke.Texture.gbx":
        raise ValueError("native fog settings or stock path differ")
    return {"candidate": str(path), "candidateSha256": sha(raw), "bytes": len(raw),
            "wholeCarrierSourceRecoveredExactly": True, "wholeFoggerGraphAndOwnerRecoveredExactly": True,
            "collectorHeaderAndReferenceTableExactExceptNodeCount": True,
            "nameFields": {"root": str(root), "child": str(child), "submodelU10": str(submodel), "emitterModel": str(emitter)},
            "nativeDictionaryDefinitions": 2, "particleSourceBytesUnchangedExceptSixNodeRefsAndThreeTypedIdAliases": True,
            "staticPrefixExactlyPreservedOutsideOptional24Positions": True,
            "scope": "PASS offline source recovery and independent NICE Id decoding. No native render/save/standalone claim."}


if __name__ == "__main__":
    report = {"auditor": "Tests/FxPlacementAudit/audit_visible_fogger_merge.py", "audits": [audit(*case) for case in CASES]}
    text = json.dumps(report, indent=2)
    (FOLDER / "independent-source-recovery-audit.json").write_text(text, encoding="utf-8")
    print(text)
