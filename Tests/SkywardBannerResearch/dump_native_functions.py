"""Dump complete pinned disk PE unwind-fragment groups for named functions.

Read-only executable-file inspection. Preferred VAs only. Never touches a live
process, the bridge or installed game data. Leaf byte ranges require --range.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "pinned_pe", ROOT / "artifacts/flag-selfcontained-20261022/serialization-audit/audit_pe.py")
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path)
parser.add_argument("functions", nargs="*")
parser.add_argument("--range", action="append", default=[], help="Explicit bounded START:END preferred VAs")
args = parser.parse_args()
assert not args.output.exists(), "Choose an unused evidence directory"
data = helper.EXE.read_bytes()
assert hashlib.sha256(data).hexdigest().upper() == helper.EXPECTED_EXE
pe = helper.pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
cs = helper.capstone.Cs(helper.capstone.CS_ARCH_X86, helper.capstone.CS_MODE_64)
cs.skipdata = True
groups = {}
for entry in pe.DIRECTORY_ENTRY_EXCEPTION:
    root = entry
    seen = set()
    while root.unwindinfo.UNW_FLAG_CHAININFO:
        assert root.struct.BeginAddress not in seen, "Unexpected unwind cycle"
        seen.add(root.struct.BeginAddress)
        root = root.unwindinfo._chained_entry
    groups.setdefault(base+root.struct.BeginAddress, []).append(
        (base+entry.struct.BeginAddress, base+entry.struct.EndAddress))
ranges = {}
for text in args.functions:
    target = int(text, 0)
    matches = [root for root, parts in groups.items() if any(a <= target < b for a, b in parts)]
    assert len(matches) == 1, f"No unique unwind function owner for {text}"
    root = matches[0]
    ranges[hex(root)] = sorted(set(groups[root]))
for text in args.range:
    a, b = (int(part, 0) for part in text.split(":"))
    assert 0 < b-a <= 0x8000, "Explicit range exceeds byte budget"
    ranges[f"range-{a:#x}"] = [(a, b)]
args.output.mkdir(parents=True)
files = []
for name, parts in ranges.items():
    assert sum(b-a for a, b in parts) <= 0x40000, "Function exceeds total evidence byte budget"
    lines = ["Pinned disk-only PE inspection; complete unwind fragment group or explicit bounded range.",
             "Executable SHA256: "+helper.EXPECTED_EXE]
    for a, b in parts:
        assert b-a <= 0x10000, "Function fragment exceeds evidence byte budget"
        lines.append(f"RANGE {a:#x} {b:#x}")
        lines.extend(f"{i.address:x} {i.mnemonic} {i.op_str}"
                     for i in cs.disasm(pe.get_data(a-base, b-a), a))
    path = args.output/(name+".asm.txt")
    path.write_text("\n".join(lines)+"\n", encoding="utf-8")
    files.append({"file": path.name, "ranges": [[hex(a), hex(b)] for a, b in parts],
                  "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper()})
manifest = {"scope": "Read-only pinned executable-file inspection; preferred VAs, not live addresses.",
            "exeSha256": helper.EXPECTED_EXE,
            "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
            "files": files}
(args.output/"manifest.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
print(json.dumps({"files": files}, indent=2))
