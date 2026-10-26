"""Read-only, pinned on-disk PE cross-references. Never opens a process.

Produces reproducible, bounded evidence for ordinary vertex-tween time tracing.
Byte-pattern candidates are not automatically treated as decoded instructions.
"""
import hashlib
import importlib.util
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "pinned_pe", ROOT / "artifacts/flag-selfcontained-20261022/serialization-audit/audit_pe.py")
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
out = Path(sys.argv[1]).resolve()
assert not out.exists(), "Preserve previous evidence; choose an unused directory"
exe_bytes = helper.EXE.read_bytes()
assert hashlib.sha256(exe_bytes).hexdigest().upper() == helper.EXPECTED_EXE
pe = helper.pefile.PE(data=exe_bytes)
base = pe.OPTIONAL_HEADER.ImageBase
cs = helper.capstone.Cs(helper.capstone.CS_ARCH_X86, helper.capstone.CS_MODE_64)
cs.skipdata = True

def owner(va):
    e = next((x for x in pe.DIRECTORY_ENTRY_EXCEPTION
              if x.struct.BeginAddress <= va-base < x.struct.EndAddress), None)
    if e is None:
        return None
    fragments = [[base+e.struct.BeginAddress, base+e.struct.EndAddress]]
    while e.unwindinfo.UNW_FLAG_CHAININFO:
        e = e.unwindinfo._chained_entry
        fragments.append([base+e.struct.BeginAddress, base+e.struct.EndAddress])
    return {"root": hex(fragments[-1][0]), "ranges": [[hex(a),hex(b)] for a,b in fragments]}

targets = {0x1413FF6B0, 0x140180A50, 0x1405730E0, 0x140801E20,
           0x1401F3F40, 0x1401F3CD0, 0x1407BD0D0, 0x1407BD0E0}
targets.update(int(x,0) for x in sys.argv[2:])
calls = {hex(t):[] for t in targets}
jumps = {hex(t):[] for t in targets}
pointer_refs = {hex(t):[] for t in targets}
offset_candidates = []
for section in pe.sections:
    data = section.get_data()
    sva = base+section.VirtualAddress
    for target in targets:
        pattern = struct.pack("<Q",target)
        pos = 0
        while True:
            p = data.find(pattern,pos)
            if p < 0:
                break
            pointer_refs[hex(target)].append(hex(sva+p))
            pos = p+1
    if not section.Characteristics & 0x20000000:
        continue
    for opcode, table in [(b"\xe8",calls),(b"\xe9",jumps)]:
        pos = 0
        while True:
            p = data.find(opcode,pos)
            if p<0 or p+5>len(data):
                break
            va = sva+p
            dest = va+5+struct.unpack_from("<i",data,p+1)[0]
            if dest in targets:
                table[hex(dest)].append({"va":hex(va),"owner":owner(va)})
            pos=p+1
    pos=0
    while True:
        p=data.find(struct.pack("<I",0x7A38),pos)
        if p<0:
            break
        va=sva+p
        o=owner(va)
        offset_candidates.append({"immediateVA":hex(va),"owner":o})
        pos=p+1

out.mkdir(parents=True)
roots={}
def keep_owner(o):
    if not o:
        return
    if o["root"] not in roots:
        roots[o["root"]]={"root":o["root"],"ranges":[]}
    for pair in o["ranges"]:
        if pair not in roots[o["root"]]["ranges"]:
            roots[o["root"]]["ranges"].append(pair)

for rows in list(calls.values())+list(jumps.values()):
    for row in rows:
        keep_owner(row["owner"])
for row in offset_candidates:
    keep_owner(row["owner"])
for t in targets:
    keep_owner(owner(t))
files=[]
for key,o in roots.items():
    lines=["Pinned on-disk PE inspection; preferred VAs, not live addresses.",
           "Executable SHA256: "+helper.EXPECTED_EXE]
    for a,b in o["ranges"]:
        a,b=int(a,0),int(b,0)
        # Refuse unbounded giant functions in this first reference pass.
        if b-a>0x10000:
            lines.append(f"OMITTED large root fragment {a:#x} {b:#x}")
            continue
        lines.append(f"RANGE {a:#x} {b:#x}")
        lines += [f"{i.address:x} {i.mnemonic} {i.op_str}"
                  for i in cs.disasm(pe.get_data(a-base,b-a),a)]
    path=out/(key+".asm.txt")
    path.write_text("\n".join(lines)+"\n",encoding="utf-8")
    files.append({"file":path.name,"sha256":hashlib.sha256(path.read_bytes()).hexdigest().upper()})
bounded={
    "ordinary-cloth-time":(0x1401F3F40,0x1401F4030),
    "ordinary-cloth-registration":(0x1401F3CD0,0x1401F3E7D),
    "ordinary-cloth-tick-tail":(0x1401F3265,0x1401F3287),
    "ordinary-cloth-origin-writer":(0x1401F3044,0x1401F3099),
    "hms-zone-manager-constructor":(0x1401F0E39,0x1401F0E59),
    "hms-zone-class-id":(0x1401E2070,0x1401E207C),
    "physical-dyna-method-getter":(0x1406A5B40,0x1406A5B54),
    "physical-dyna-slot-id-getter":(0x1407BA440,0x1407BA447),
    "scene-component-slot-accessor":(0x1406C26A0,0x1406C26A8),
    "physical-collision-method-getter":(0x1406A5B00,0x1406A5B14),
    "physical-collision-slot-id-getter":(0x1406C4380,0x1406C4387),
    "physical-dyna-factory-owner-binding":(0x1407BAAD0,0x1407BAB14),
    "kinematic-offset-getter":(0x1407B9E40,0x1407B9E4F),
    "kinematic-offset-setter":(0x1407BD0D0,0x1407BD0D7),
    "kinematic-clock-consumer":(0x140801E20,0x140801F50),
    "kinematic-clock-source":(0x1407BD0E0,0x1407BD15E),
    "kinematic-game-source-selection":(0x1412EACF6,0x1412EAD3E),
    "kinematic-game-clock-input":(0x1412EB035,0x1412EB058),
    "native-frac-helper":(0x1418F6A04,0x1418F6AA8),
    "subvisual-native-constructor":(0x1413FF4B0,0x1413FF4DF),
}
for name,(a,b) in bounded.items():
    lines=["Pinned on-disk PE inspection; preferred VAs, not live addresses.",
           "Executable SHA256: "+helper.EXPECTED_EXE,f"RANGE {a:#x} {b:#x}"]
    lines += [f"{i.address:x} {i.mnemonic} {i.op_str}" for i in cs.disasm(pe.get_data(a-base,b-a),a)]
    path=out/(name+".asm.txt")
    path.write_text("\n".join(lines)+"\n",encoding="utf-8")
    files.append({"file":path.name,"sha256":hashlib.sha256(path.read_bytes()).hexdigest().upper(),
                  "range":[hex(a),hex(b)]})
result={"scope":"Static disk-file bytes only; no process, game, MCP, staging or fixtures.",
        "exeSha256":helper.EXPECTED_EXE,
        "scriptSha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
        "directCallCandidates":calls,"directJumpCandidates":jumps,"absolutePointerReferences":pointer_refs,
        "offset7A38Candidates":offset_candidates,"files":files,
        "ordinaryTickTimeMultiplier":{"va":"0x141d1ed94","float32":struct.unpack("<f",pe.get_data(0x141D1ED94-base,4))[0]},
        "subvisualApplyVirtualSlot":{"vtableVA":"0x141d0b8a0","slotOffset":"0xf8","targetVA":hex(struct.unpack("<Q",pe.get_data(0x141D0B998-base,8))[0])},
        "limits":"E8/E9 byte candidates and literal 7A38 displacement matches require manual instruction validation; no exhaustive indirect graph claim. Owner dumps include discovered unwind-chain fragments, not an assertion of complete function bodies; named bounded ranges pin the reviewed excerpts."}
(out/"manifest.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
print(json.dumps({k:v for k,v in result.items() if k!="files"},indent=2))
