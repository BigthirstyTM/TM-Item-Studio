"""Find pinned disk PE candidate stores to one exact native displacement.

Candidate windows may begin within another instruction. Full native-function
decoding is required before accepting an assignment or an object identity.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct

root = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("pe_helper", root/"artifacts/flag-selfcontained-20261022/serialization-audit/audit_pe.py")
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path)
parser.add_argument("offset", type=lambda x: int(x, 0))
args = parser.parse_args()
assert not args.output.exists(), "Preserve previous evidence"
raw = h.EXE.read_bytes()
assert hashlib.sha256(raw).hexdigest().upper() == h.EXPECTED_EXE
pe = h.pefile.PE(data=raw)
base = pe.OPTIONAL_HEADER.ImageBase
cs = h.capstone.Cs(h.capstone.CS_ARCH_X86, h.capstone.CS_MODE_64)
cs.detail = True


def owner(va):
    entry = next((e for e in pe.DIRECTORY_ENTRY_EXCEPTION if e.struct.BeginAddress <= va-base < e.struct.EndAddress), None)
    if entry is None:
        return None
    while entry.unwindinfo.UNW_FLAG_CHAININFO:
        entry = entry.unwindinfo._chained_entry
    return hex(base+entry.struct.BeginAddress)


found = {}
for section in pe.sections:
    if not section.Characteristics & 0x20000000:
        continue
    data = section.get_data()
    section_va = base+section.VirtualAddress
    start = 0
    needle = struct.pack("<i", args.offset)
    while (pos := data.find(needle, start)) >= 0:
        start = pos+1
        for delta in range(2, 6):
            at = pos-delta
            if at < 0:
                continue
            insn = next(cs.disasm(data[at:at+15], section_va+at), None)
            if not insn or not insn.mnemonic.startswith("mov") or not insn.operands:
                continue
            operand = insn.operands[0]
            if operand.type != h.capstone.x86.X86_OP_MEM or operand.mem.disp != args.offset:
                continue
            if insn.reg_name(operand.mem.base) in ("rsp", "rbp", "rip"):
                continue
            found[insn.address] = {"address": hex(insn.address), "instruction": insn.mnemonic+" "+insn.op_str,
                                   "owner": owner(insn.address), "bytes": insn.bytes.hex()}
args.output.mkdir(parents=True)
result = {"scope": __doc__, "offset": hex(args.offset), "exeSha256": h.EXPECTED_EXE,
          "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
          "candidates": [found[k] for k in sorted(found)]}
(args.output/"candidates.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
print(json.dumps(result, indent=2))
