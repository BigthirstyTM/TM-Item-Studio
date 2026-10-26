"""Read-only corpus search for native GPU-model bitmap FID strings."""

from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


CLASSES = {0x090C6000, 0x090B2000, 0x090B3000, 0x0915C000, 0x09145000, 0x2E002000}
MARKER = struct.pack("<I", 0x090C6002)


def scan(roots: list[Path]) -> dict:
    stats = Counter()
    values = []
    errors = []
    seen = set()
    for root in roots:
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if not path.is_file() or path.suffix.lower() != ".gbx":
                continue
            resolved = str(path.resolve()).lower()
            if resolved in seen:
                continue
            seen.add(resolved)
            stats["gbxFiles"] += 1
            try:
                with path.open("rb") as stream:
                    header = stream.read(17)
                if len(header) != 17 or header[:7] != b"GBX\x06\x00BU":
                    continue
                class_id = struct.unpack_from("<I", header, 9)[0]
                if class_id not in CLASSES:
                    continue
                stats["relevantClassFiles"] += 1
                if path.stat().st_size > 32 * 1024 * 1024:
                    stats["largeSkipped"] += 1
                    continue
                container = GbxContainer.read(path.read_bytes())
                body = container.body
                start = 0
                while True:
                    offset = body.find(MARKER, start)
                    if offset < 0:
                        break
                    start = offset + 4
                    if offset + 20 > len(body):
                        continue
                    version, _, is_fid, length = struct.unpack_from("<4I", body, offset + 4)
                    if not 7 <= version <= 64 or is_fid not in (0, 1):
                        continue
                    stats["gpuModelChunkCandidates"] += 1
                    if not is_fid:
                        stats["nodeRefTextureBranches"] += 1
                        continue
                    stats["stringTextureBranches"] += 1
                    if length == 0:
                        stats["emptyStringBranches"] += 1
                        continue
                    if length > 4096 or offset + 20 + length > len(body):
                        stats["invalidStringLengths"] += 1
                        continue
                    raw = body[offset + 20:offset + 20 + length]
                    try:
                        value = raw.decode("utf-8")
                    except UnicodeDecodeError:
                        stats["invalidUtf8Strings"] += 1
                        continue
                    if any(ord(char) < 32 for char in value):
                        stats["nonPrintableStrings"] += 1
                        continue
                    values.append({"path": str(path.resolve()), "rootClass": f"0x{class_id:08X}",
                                   "bodyOffset": offset, "chunkVersion": version,
                                   "fidString": value, "externalReferenceCount": len(container.references)})
            except Exception as error:
                stats["readErrors"] += 1
                if len(errors) < 40:
                    errors.append({"path": str(path), "error": str(error)})
    return {"statistics": dict(stats), "nonemptyFidStrings": values, "firstReadErrors": errors}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, action="append", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = scan(args.root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
