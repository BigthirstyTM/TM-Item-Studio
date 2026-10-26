"""Pinned disk-only search for native instance timing packing.

No process handle, bridge, game action or mutable game file is used. Byte
matches are candidate discovery only; output includes bounded decoded owners.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "pinned_pe", ROOT / "artifacts/flag-selfcontained-20261022/serialization-audit/audit_pe.py")
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
out = Path(sys.argv[1]).resolve()
assert not out.exists(), "Choose an unused evidence directory"
data = helper.EXE.read_bytes()
assert hashlib.sha256(data).hexdigest().upper() == helper.EXPECTED_EXE
pe = helper.pefile.PE(data=data)
base = pe.OPTIONAL_HEADER.ImageBase
cs = helper.capstone.Cs(helper.capstone.CS_ARCH_X86, helper.capstone.CS_MODE_64)
cs.skipdata = True


def owner(va):
    entry = next((e for e in pe.DIRECTORY_ENTRY_EXCEPTION
                  if e.struct.BeginAddress <= va-base < e.struct.EndAddress), None)
    if entry is None:
        return None
    parts = [[base+entry.struct.BeginAddress, base+entry.struct.EndAddress]]
    while entry.unwindinfo.UNW_FLAG_CHAININFO:
        entry = entry.unwindinfo._chained_entry
        parts.append([base+entry.struct.BeginAddress, base+entry.struct.EndAddress])
    return {"root": hex(parts[-1][0]), "ranges": [[hex(a), hex(b)] for a, b in parts]}


A = struct.unpack("<f", struct.pack("<I", 0x3B7D24E4))[0]
B = struct.unpack("<f", struct.pack("<I", 0x39800801))[0]
values = {"shaderA": A, "inverseShaderA": 1/A, "twiceInverseShaderA": 2/A,
          "phaseMaximum": 4095.0, "periodSentinel": 2047.0,
          "phaseQuantum": B, "maximumSeconds": 60.0,
          "log2_240": math.log2(240), "log_240": math.log(240),
          "inverseLog_240_times2047": 2047/math.log(240)}
float_locations = {}
mask_candidates = []
roots = {}


def keep(item):
    if item:
        roots[item["root"]] = item


for section in pe.sections:
    raw = section.get_data()
    va = base+section.VirtualAddress
    for name, number in values.items():
        pattern = struct.pack("<f", number)
        start = 0
        while (position := raw.find(pattern, start)) >= 0:
            float_locations[va+position] = {"name": name, "float32": struct.unpack("<f", pattern)[0]}
            start = position+1
    if not section.Characteristics & 0x20000000:
        continue
    for mask in (0x7FF, 0xFFF, 0x7FFFFF):
        start = 0
        while (position := raw.find(struct.pack("<I", mask), start)) >= 0:
            item = owner(va+position)
            row = {"mask": hex(mask), "immediateVA": hex(va+position), "owner": item}
            mask_candidates.append(row)
            keep(item)
            start = position+1

references = []
for section in pe.sections:
    if not section.Characteristics & 0x20000000:
        continue
    raw = section.get_data()
    va = base+section.VirtualAddress
    # Locate legacy SSE RIP-relative scalar loads/arithmetic. Decode candidate
    # boundaries with Capstone before recording a reference.
    for prefix in (b"\xf3\x0f", b"\x0f", b"\xf2\x0f"):
        start = 0
        while (position := raw.find(prefix, start)) >= 0:
            start = position+1
            p = position+len(prefix)
            if p+6 > len(raw) or raw[p] not in (0x10, 0x28, 0x2F, 0x51, 0x58, 0x59, 0x5C, 0x5D, 0x5E, 0x5F):
                continue
            modrm = raw[p+1]
            if modrm & 0xC7 != 0x05:
                continue
            target = va+p+6+struct.unpack_from("<i", raw, p+2)[0]
            if target not in float_locations:
                continue
            instruction = next(cs.disasm(raw[position:position+15], va+position), None)
            if instruction is None or instruction.address+instruction.size != va+p+6:
                continue
            item = owner(instruction.address)
            references.append({"va": hex(instruction.address), "instruction": instruction.mnemonic+" "+instruction.op_str,
                               "target": hex(target), **float_locations[target], "owner": item})
            keep(item)

out.mkdir(parents=True)
files = []
for name, item in roots.items():
    lines = ["Pinned disk-only PE candidate inspection; preferred VAs.", "Executable SHA256: "+helper.EXPECTED_EXE]
    for begin, end in item["ranges"]:
        a, b = int(begin, 0), int(end, 0)
        if b-a > 0x8000:
            lines.append(f"OMITTED large fragment {a:#x} {b:#x}")
            continue
        lines.append(f"RANGE {a:#x} {b:#x}")
        lines.extend(f"{i.address:x} {i.mnemonic} {i.op_str}"
                     for i in cs.disasm(pe.get_data(a-base, b-a), a))
    path = out/(name+".asm.txt")
    path.write_text("\n".join(lines)+"\n", encoding="utf-8")
    files.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper()})
manifest = {"scope": "Pinned disk-only candidate search; decoded instructions still require semantic interpretation.",
            "exeSha256": helper.EXPECTED_EXE, "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
            "constants": [{"va": hex(va), **value} for va, value in float_locations.items()],
            "constantReferences": references, "maskByteCandidates": mask_candidates, "files": files}
(out/"manifest.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
print(json.dumps({"constants": len(float_locations), "references": references,
                  "maskCandidates": len(mask_candidates), "ownerFiles": len(files)}, indent=2))
