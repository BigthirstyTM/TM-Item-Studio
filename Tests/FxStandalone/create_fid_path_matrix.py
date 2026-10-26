"""Generate six bounded native bitmap-FID string resolver controls offline.

The two already tested full GameData forward/backslash forms are excluded.
No files are staged and no engine operation is performed.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import struct

from create_builtin_sparkler import DEFAULT_SOURCE, generate, sha
from gbx_container import GbxContainer


CASES = (
    ("Bare", ""),
    ("Texture", "Texture"),
    ("MediaForward", "Media/Texture"),
    ("StadiumForward", "Stadium/Media/Texture"),
    ("MediaBackslash", r"Media\Texture"),
    ("StadiumBackslash", r"Stadium\Media\Texture"),
)


def create(source: Path, output: Path) -> dict:
    source_raw = source.read_bytes()
    original = GbxContainer.read(source_raw)
    rows = []
    for label, root in CASES:
        path = output / ("TMIS_Sparkler_Fid" + label + "_20261025.Item.Gbx")
        manifest = generate(source, path, root)
        candidate = GbxContainer.read(path.read_bytes())
        body = candidate.body
        # Verify both branch records and undo each low-to-high splice. After
        # undoing an earlier edit the next original source offset is valid.
        for patch in manifest["patches"]:
            at = patch["sourceBodyOffset"]
            replacement = bytes.fromhex(patch["newHex"])
            branch, length = struct.unpack_from("<II", replacement)
            expected = patch["bitmapFidPath"].encode("utf-8")
            if branch != 1 or length != len(expected) or replacement[8:] != expected:
                raise ValueError("unexpected FID branch encoding")
            if body[at:at + len(replacement)] != replacement:
                raise ValueError("candidate does not contain its exact audited FID branch")
            body = body[:at] + bytes.fromhex(patch["oldHex"]) + body[at + len(replacement):]
        if body != original.body or candidate.references or candidate.node_count != 23:
            raise ValueError("matrix candidate is not exactly invertible to original source")
        if label == "Bare" and any("/" in p["bitmapFidPath"] or "\\" in p["bitmapFidPath"] for p in manifest["patches"]):
            raise ValueError("bare filename candidate gained an unintended path separator")
        rows.append({"case": label, "textureRoot": root,
                     "file": str(path.resolve()), "bytes": path.stat().st_size,
                     "sha256": manifest["outputSha256"],
                     "stageAt": "Items/TMIS_FX_SequentialProof/" + path.name,
                     "texturePaths": [p["bitmapFidPath"] for p in manifest["patches"]],
                     "externalReferences": 0, "nodeCount": 23,
                     "completeOriginalBodyExactlyRestored": True,
                     "nativeVerdict": "pending isolated FID preload and read-only bitmap binding inspection"})
    report = {"generator": "Tests/FxStandalone/create_fid_path_matrix.py",
              "source": str(source.resolve()), "sourceSha256": sha(source_raw),
              "cases": rows, "excludedPreviouslyTested": ["GameData/Stadium/Media/Texture", r"GameData\Stadium\Media\Texture"],
              "evidenceScope": "Offline inverse/branch audit only; no native resolver success or visible particle claim"}
    (output / "fid-path-matrix.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(create(args.source, args.output), indent=2))
