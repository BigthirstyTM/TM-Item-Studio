"""Read-only byte proof for the first native save of the attached Sparkler.

Normalizes only independently recorded NodeRef/lookback slots and the native
collector ID insertion. It never rewrites or stages a live item/map.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "FxStandalone"))
from gbx_container import GbxContainer
from reindex import NODE_DEFINITIONS, validate_source

SAVED_SHA256 = "92D03AFE121A532507836AA724C01961B81519B083EB407BA1D0CE1D3ADFA315"
LOOKBACK_REFERENCES = (2446, 2518, 4074, 4392, 5948)
NATIVE_NODE_MAP = {**{n: n for n in range(1, 14)},
                   **{n: n + 1 for n in range(14, 19)},
                   19: 21, 20: 22, 21: 14, 22: 20}
ID = b"ChatGPT\\PackedOnlyV2\\TestItem_Sparkler_BitmapRefs_v3.Item.Gbx"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def audit(source: Path, saved: Path) -> dict:
    source_bytes, saved_bytes = source.read_bytes(), saved.read_bytes()
    original = validate_source(source_bytes)
    if sha(saved_bytes) != SAVED_SHA256:
        raise ValueError("not the pinned first native-saved embedded item")
    native = GbxContainer.read(saved_bytes)
    id_definition = struct.pack("<II", 0x40000000, len(ID)) + ID
    if original.body[29:33] != b"\xff" * 4 or native.body[29:29+len(id_definition)] != id_definition:
        raise ValueError("collector ID insertion differs")
    normalized = bytearray(native.body[:29] + b"\xff" * 4 + native.body[29+len(id_definition):])
    if len(normalized) != len(original.body):
        raise ValueError("unexpected native body size after ID normalization")
    patches = []
    for offset in LOOKBACK_REFERENCES:
        before = struct.unpack_from("<I", original.body, offset)[0]
        after = struct.unpack_from("<I", normalized, offset)[0]
        if (before, after) != (0x40000002, 0x40000003):
            raise ValueError("recorded shared-lookback reference differs")
        struct.pack_into("<I", normalized, offset, before)
        patches.append({"kind": "lookback-reference", "sourceOffset": offset, "old": before, "native": after})
    slots = [(p, n) for p, n, _ in NODE_DEFINITIONS] + [(3814, 21), (5688, 22)]
    for offset, old in slots:
        expected = NATIVE_NODE_MAP[old]
        if struct.unpack_from("<I", normalized, offset)[0] != expected:
            raise ValueError("recorded native NodeRef differs")
        struct.pack_into("<I", normalized, offset, old)
        if old != expected:
            patches.append({"kind": "node-reference", "sourceOffset": offset, "old": old, "native": expected})
    if bytes(normalized) != original.body:
        raise ValueError("unexplained bytes remain after bounded native normalization")
    return {"source": str(source.resolve()), "saved": str(saved.resolve()),
            "sourceSha256": sha(source_bytes), "savedSha256": sha(saved_bytes),
            "sourceBodyBytes": len(original.body), "nativeBodyBytes": len(native.body),
            "nativeCollectorId": ID.decode("ascii"),
            "addedLookbackDefinitionSlots": 1,
            "oldToNativeNodeIndices": NATIVE_NODE_MAP,
            "recordedReferencePatches": sorted(patches, key=lambda row: row["sourceOffset"]),
            "completeOriginalBodyRecoveredExactly": True,
            "allParticleModelsExpressionsOwnerDataAndUnknownBytesUnchanged": True,
            "nativeExternalReferences": native.references,
            "verdict": "No FX body setting or activation graph was lost; native save only changed collector identity and reference numbering. Resource packaging must be audited separately."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("saved", type=Path)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    report = audit(args.source, args.saved)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
