"""Bounded decoded search of pinned PE draw flags; disk only, no process access."""
import bisect
import hashlib
import json
from pathlib import Path
import struct
import sys

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "artifacts/flag-vanilla-steps-20261021/gpu-tools/python-libs"))
import capstone
import pefile

EXE = Path("C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/Trackmania.exe")
PIN = "3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA"
OUTPUT = REPO / "artifacts/skyward-banner-20261023/material-candidates-dyna-render-audit-v1"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    data = EXE.read_bytes()
    assert sha(data) == PIN
    pe = pefile.PE(data=data)
    base = pe.OPTIONAL_HEADER.ImageBase
    assert base == 0x140000000
    section = next(s for s in pe.sections if s.Name.rstrip(b"\0") == b".text")
    text = section.get_data()
    entries = sorted(pe.DIRECTORY_ENTRY_EXCEPTION, key=lambda e:e.struct.BeginAddress)
    starts = [e.struct.BeginAddress for e in entries]
    candidates = set()
    for pattern in (struct.pack("<I",0x20000000),struct.pack("<I",0xDFFFFFFF)):
        position = 0
        while True:
            position = text.find(pattern, position)
            if position < 0:
                break
            rva = section.VirtualAddress + position
            index = bisect.bisect_right(starts,rva)-1
            if index >= 0 and rva < entries[index].struct.EndAddress:
                candidates.add(index)
            position += 1
    cs = capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
    cs.detail = True
    matches = []
    for index in sorted(candidates):
        entry = entries[index]
        start,end = entry.struct.BeginAddress,entry.struct.EndAddress
        for i in cs.disasm(pe.get_data(start,end-start),base+start):
            immediate = [o.imm&0xFFFFFFFF for o in i.operands if o.type==capstone.CS_OP_IMM]
            memory = [o for o in i.operands if o.type==capstone.CS_OP_MEM]
            if any(v in (0x20000000,0xDFFFFFFF,29) for v in immediate) and any(o.mem.disp in (0x28,0x1D8,0x1D4) for o in memory):
                matches.append({"fragmentStart":hex(base+start),"fragmentEnd":hex(base+end),"va":hex(i.address),"instruction":i.mnemonic+" "+i.op_str})
    # Exact .pdata chain association, including all fragments of the relevant root.
    def root(entry):
        while entry.unwindinfo.UNW_FLAG_CHAININFO:
            entry=entry.unwindinfo._chained_entry
        return base+entry.struct.BeginAddress
    selected = []
    for entry in entries:
        if root(entry) in (0x140A48C70,0x140A8A1A0,0x140A8A210):
            selected.append((base+entry.struct.BeginAddress,base+entry.struct.EndAddress))
    assert not OUTPUT.exists(),"Preserve earlier evidence"
    OUTPUT.mkdir(parents=True)
    body = f"Pinned on-disk PE only; preferred VAs. Executable SHA256 {PIN}.\n"
    for start,end in selected:
        assert end-start<0x10000
        body+=f"RANGE {start:#x} {end:#x}\n"
        body+="".join(f"{i.address:x} {i.mnemonic} {i.op_str}\n" for i in cs.disasm(pe.get_data(start-base,end-start),start))
    (OUTPUT/"instanced-draw-flag-writer-and-consumers.asm.txt").write_text(body,encoding="utf-8")
    result={
        "scope":"Pinned PE file search; candidates decoded from .pdata boundaries. Not an exhaustive dataflow/write or live draw proof.",
        "executableSha256":PIN,"candidateUnwindFragments":len(candidates),"matches":matches,
        "retainedRanges":[[hex(a),hex(b)] for a,b in selected],
        "disassemblySha256":sha(body.encode()),
    }
    (OUTPUT/"draw-flag-audit.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,indent=2))


if __name__=="__main__":
    main()
