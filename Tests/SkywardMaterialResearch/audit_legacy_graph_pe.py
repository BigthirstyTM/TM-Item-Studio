"""Pinned executable file audit of retained legacy hosts; never opens a process."""
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
OUTPUT = REPO / "artifacts/skyward-banner-20261023/material-candidates-legacy-graph-audit-v2"
RANGES = {
    "native-modern-item-vismodel-slot": (0x140AB5A1F, 0x140AB5A64),
    "native-legacy-tree-function-slot": (0x1403F0E0E, 0x1403F0E5E),
    "native-legacy-tree-function-setter": (0x1403F3B70, 0x1403F3C20),
}


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def main():
    data = EXE.read_bytes()
    assert sha(data) == PIN
    pe = pefile.PE(data=data)
    base = pe.OPTIONAL_HEADER.ImageBase
    assert base == 0x140000000
    index = 0x11 - 6
    dispatch = base + struct.unpack("<I", pe.get_data(0x3F1230 + index * 4, 4))[0]
    assert dispatch == RANGES["native-legacy-tree-function-slot"][0]
    cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    rows = []
    for name, (start, end) in RANGES.items():
        target = OUTPUT / (name + ".asm.txt")
        assert not target.exists(), "Preserve earlier evidence"
        text = f"Read-only on-disk PE, preferred VAs. Executable SHA256 {PIN}.\n"
        text += f"RANGE {start:#x} {end:#x}\n"
        text += "".join(f"{i.address:x} {i.mnemonic} {i.op_str}\n" for i in cs.disasm(pe.get_data(start-base, end-start), start))
        target.write_text(text, encoding="utf-8")
        rows.append({"file": target.name, "range": [hex(start),hex(end)], "sha256":sha(target.read_bytes())})
    manifest = {"scope":"Pinned executable file-only read; no process or game calls.","executableSha256":PIN,"treeFunctionChunk":"0x0904F011","treeFunctionChunkDispatch":hex(dispatch),"outputs":rows}
    target = OUTPUT / "native-audit.json"
    assert not target.exists()
    target.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(manifest,indent=2))


if __name__ == "__main__":
    main()
