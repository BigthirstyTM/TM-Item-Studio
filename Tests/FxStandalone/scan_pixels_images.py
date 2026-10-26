"""Read-only native-corpus search for CPlugFileGen Pixels archive precedents."""
from collections import Counter
import argparse
import json
from pathlib import Path
import struct

from gbx_container import GbxContainer


def scan(root: Path) -> dict:
    statistics, formats = Counter(), Counter()
    images, errors = [], []
    marker = struct.pack("<3I", 0x0902F000, 0x80000006, 9)
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() != ".gbx":
            continue
        statistics["gbxFiles"] += 1
        try:
            with path.open("rb") as stream:
                header = stream.read(17)
            if len(header) != 17 or header[:7] != b"GBX\x06\x00BU":
                statistics["otherContainers"] += 1
                continue
            root_class = struct.unpack_from("<I", header, 9)[0]
            if root_class not in (0x09011000, 0x0902F000):
                continue
            statistics["bitmapOrFileGenRootFiles"] += 1
            if path.stat().st_size > 16 * 1024 * 1024:
                statistics["largeFilesSkipped"] += 1
                continue
            archive = GbxContainer.read(path.read_bytes())
            statistics["containersRead"] += 1
            body = archive.body
            # Root node archives omit the class marker that an inline node has.
            prefix = struct.pack("<I", root_class) if root_class == 0x0902F000 else b""
            searchable = prefix + body
            start = 0
            while (at := searchable.find(marker, start)) >= 0:
                start = at + len(marker)
                if at + 60 > len(searchable):
                    statistics["truncatedCandidates"] += 1
                    continue
                count = struct.unpack_from("<I", searchable, at + 12)[0]
                if count != 6:
                    statistics["otherParameterCount"] += 1
                    continue
                params = struct.unpack_from("<6I", searchable, at + 16)
                width, height, depth, components, image_format, mips = params
                if not 0 < width <= 16384 or not 0 < height <= 16384 or not 0 < depth <= 4096 or not 1 <= components <= 4 or not 0 <= image_format <= 18 or not 1 <= mips <= 16:
                    statistics["invalidParameters"] += 1
                    continue
                formats[(image_format, mips)] += 1
                images.append({"path": str(path.resolve()), "rootClass": f"0x{root_class:08X}",
                               "bodyOffset": at - len(prefix), "parameters": list(params),
                               "emptyExtraConfiguration": searchable[at + 40:at + 60] == bytes(20),
                               "externalReferences": len(archive.references)})
        except Exception as error:
            statistics["readErrors"] += 1
            if len(errors) < 12:
                errors.append({"path": str(path), "error": str(error)})
    return {"root": str(root.resolve()), "statistics": dict(statistics),
            "formatAndMipCounts": [{"format": key[0], "mips": key[1], "count": count}
                                   for key, count in sorted(formats.items())],
            "pixelArchives": images, "firstReadErrors": errors,
            "scope": "Bitmap/FileGen root files, observed version6 Pixels with6 integer parameters; read-only corpus evidence, not a general archive parser or native rendering proof"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = scan(args.root)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"statistics": report["statistics"], "formatAndMipCounts": report["formatAndMipCounts"]}, indent=2))
