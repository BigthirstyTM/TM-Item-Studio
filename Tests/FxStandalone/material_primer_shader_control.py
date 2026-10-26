"""Minimal reference-table-only control for the existing material primer.

Remove the extra GameData prefix from its one stock parent-material FID. All
header bytes, body bytes, bitmap pixels, shared references and native node order
stay exact. This does not establish native path resolution or rendering.
"""
from pathlib import Path
import argparse
import json
import struct

from gbx_container import GbxContainer
from audit_material_primer import audit, CANDIDATE_SHA, SOURCE_SHA, digest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "artifacts/fx-clones/fx-material-upload-control-20261025/TMIS_Sparkler_MaterialPrimer_20261025.Item.Gbx"
ORIGINAL = ROOT / "artifacts/fx-clones/fx-header-null-control-20261025/original-bitmap/TMIS_Standalone_SparklerFlare_20261025.Item.Gbx"
OLD = rb"GameData\Techno3\Media\Material\Tech3 Block TAddModCV.Material.gbx"
NEW = rb"Techno3\Media\Material\Tech3 Block TAddModCV.Material.gbx"
NATIVE = Path(r"C:\Users\PC\OpenplanetNext\Extract\GameData\Stadium\Media\Material_BlockCustom\CustomModAddSelfIllum.Material.Gbx")


def generate(output: Path):
    raw = SOURCE.read_bytes(); source = GbxContainer.read(raw)
    if digest(raw) != CANDIDATE_SHA or digest(ORIGINAL.read_bytes()) != SOURCE_SHA:
        raise ValueError("requires the exact independently audited source primer")
    independent = audit(ORIGINAL, SOURCE)
    ref = source.references[0]
    if ref["path"] != OLD.decode() or ref["ancestorLevel"] != 0 or ref["folderIndex"] != 0 or ref["useFile"]:
        raise ValueError("existing shader reference differs")
    native = GbxContainer.read(NATIVE.read_bytes())
    native_parent = [r for r in native.references if r["path"] == NEW.decode() and not r["useFile"]]
    if len(native_parent) != 1:
        raise ValueError("proposed game-root parent spelling lacks its native precedent")
    offset = ref["start"] + 4
    before = struct.pack("<I", len(OLD)) + OLD
    after = struct.pack("<I", len(NEW)) + NEW
    if source.prefix[offset:offset + len(before)] != before:
        raise ValueError("bounded reference-table string differs")
    prefix = source.prefix[:offset] + after + source.prefix[offset + len(before):]
    result = GbxContainer(prefix, source.body, source.compression, source.node_count, ()).write(source.body)
    reopened = GbxContainer.read(result)
    if reopened.body != source.body or reopened.node_count != 33 or len(reopened.references) != 1:
        raise ValueError("opaque graph or reference count changed")
    actual = reopened.references[0]
    if actual["path"] != NEW.decode() or any(actual[k] != ref[k] for k in ("flags", "nodeIndex", "useFile", "folderIndex", "ancestorLevel")):
        raise ValueError("shader dependency fields changed outside the path text")
    restored_prefix = prefix[:offset] + before + prefix[offset + len(after):]
    if restored_prefix != source.prefix:
        raise ValueError("exact source reference table not restorable")
    header_end = 17 + struct.unpack_from("<I", source.prefix, 13)[0]
    if reopened.prefix[:header_end] != source.prefix[:header_end]:
        raise ValueError("collector header changed")
    if output.exists(): raise FileExistsError("preserve existing evidence: " + str(output))
    output.parent.mkdir(parents=True, exist_ok=True); output.write_bytes(result)
    report = {
        "generator": "Tests/FxStandalone/material_primer_shader_control.py",
        "source": str(SOURCE), "sourceSha256": digest(raw), "output": str(output.resolve()),
        "outputSha256": digest(result), "bytes": len(result),
        "stageAt": "Items/TMIS_FX_SequentialProof/" + output.name,
        "oldParentShaderPath": OLD.decode(), "newParentShaderPath": NEW.decode(),
        "nativePrecedent": str(NATIVE), "nativePrecedentReference": native_parent[0],
        "headerExactlyPreserved": True, "bodyExactlyPreserved": True,
        "nodeCount": 33, "firstUseIndices": independent["firstUseIndices"],
        "sharedBitmapNodeIndex": 14, "sampler": "SelfIllum", "shaderRefNodeIndex": 29,
        "parentReference": actual, "allSourceBytesExactlyRestorable": True,
        "scope": "Only the one external parent-material path loses GameData\\; native ancestor-zero versus native source ancestor-three is deliberately unchanged.",
        "nativeVerdict": "Unstaged offline control. Native preload, bitmap GPU upload, visible particles, save/reopen and standalone behavior are unverified."
    }
    output.with_suffix(".shader-path-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(); print(json.dumps(generate(args.output), indent=2))
