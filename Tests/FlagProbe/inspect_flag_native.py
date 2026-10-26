"""Read-only PE inspection for the fixed Trackmania research build.

Uses optional, already archived Capstone/pefile tools. Addresses are preferred
image addresses; this never opens or modifies the live game process.
"""
import argparse
import bisect
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'artifacts/flag-vanilla-steps-20261021/gpu-tools/python-libs'))
import capstone
import pefile

EXE=Path('C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Trackmania/Trackmania.exe')


def main():
    p=argparse.ArgumentParser()
    p.add_argument('output',type=Path)
    p.add_argument('--range',nargs=2,action='append',default=[])
    p.add_argument('--calls',action='append',default=[])
    p.add_argument('--quick-calls',action='append',default=[],
        help='Candidate direct E8/E9 references; confirm surrounding instructions separately')
    p.add_argument('--rip',action='append',default=[])
    args=p.parse_args()
    assert not args.output.exists(),'Preserve prior evidence'
    pe=pefile.PE(str(EXE))
    base=pe.OPTIONAL_HEADER.ImageBase
    cs=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
    cs.detail=True
    cs.skipdata=True
    lines=[]
    for start,end in args.range:
        start,end=int(start,0),int(end,0)
        assert base<=start<end<=base+pe.OPTIONAL_HEADER.SizeOfImage
        lines.append(f'RANGE {start:#x} {end:#x}')
        for i in cs.disasm(pe.get_data(start-base,end-start),start):
            lines.append(f'{i.address:x} {i.mnemonic} {i.op_str}')
    targets={int(a,0) for a in args.calls}
    rips={int(a,0) for a in args.rip}
    quick={int(a,0) for a in args.quick_calls}
    if quick:
        functions=[(base+x.struct.BeginAddress,base+x.struct.EndAddress) for x in pe.DIRECTORY_ENTRY_EXCEPTION]
        starts=[x[0] for x in functions]
        for section in pe.sections:
            if not section.Characteristics & 0x20000000:continue
            data=section.get_data()
            for opcode in (b'\xe8',b'\xe9'):
                pos=-1
                while True:
                    pos=data.find(opcode,pos+1)
                    if pos<0:break
                    if pos+5>len(data):continue
                    address=base+section.VirtualAddress+pos
                    target=address+5+struct.unpack_from('<i',data,pos+1)[0]
                    if target not in quick:continue
                    f=functions[max(0,bisect.bisect_right(starts,address)-1)]
                    lines.append(f'CANDIDATE {address:#x} target {target:#x}; fragment {f[0]:#x} {f[1]:#x}')
    if targets or rips:
        functions=[(base+x.struct.BeginAddress,base+x.struct.EndAddress) for x in pe.DIRECTORY_ENTRY_EXCEPTION]
        starts=[x[0] for x in functions]
        for section in pe.sections:
            if not section.Characteristics & 0x20000000:continue
            for i in cs.disasm(section.get_data(),base+section.VirtualAddress):
                if i.id==0:continue
                hit=bool(i.mnemonic in ('call','jmp') and i.operands and i.operands[0].type==capstone.x86.X86_OP_IMM and i.operands[0].imm in targets)
                hit|=any(o.type==capstone.x86.X86_OP_MEM and o.mem.base==capstone.x86.X86_REG_RIP and i.address+i.size+o.mem.disp in rips for o in i.operands)
                if hit:
                    f=functions[max(0,bisect.bisect_right(starts,i.address)-1)]
                    lines.append(f'XREF {i.address:#x} {i.mnemonic} {i.op_str}; fragment {f[0]:#x} {f[1]:#x}')
    args.output.write_text('\n'.join(lines)+'\n')
    print('\n'.join(lines))


if __name__=='__main__':main()
