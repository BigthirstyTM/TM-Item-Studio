"""Bounded on-disk clock audit. Never reads a live process or controls the game."""
import hashlib
import importlib.util
import json
import sys
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / "artifacts/flag-selfcontained-20261022/serialization-audit/audit_pe.py"
spec = importlib.util.spec_from_file_location("pinned_pe", HELPER)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)

out = Path(sys.argv[1]).resolve()
assert not out.exists(), "Preserve previous evidence; use a new directory"
exe = helper.EXE
exe_hash = hashlib.sha256(exe.read_bytes()).hexdigest().upper()
assert exe_hash == helper.EXPECTED_EXE
pe = helper.pefile.PE(str(exe))
base = pe.OPTIONAL_HEADER.ImageBase
assert base == 0x140000000
cs = helper.capstone.Cs(helper.capstone.CS_ARCH_X86, helper.capstone.CS_MODE_64)
cs.skipdata = True
ranges = {
    "constraint-evaluator": (0x1404A9270, 0x1404A9570),
    "subvisual-sequence-apply": (0x1413FF6B0, 0x1413FFB00),
    "kinematic-update": (0x140801E20, 0x140802C00),
    "native-axis-angle-quaternion": (0x1401936A0, 0x140193760),
    "native-quaternion-to-iso": (0x140183670, 0x140183880),
    "native-quaternion-to-matrix": (0x1401886D0, 0x140188900),
}
out.mkdir(parents=True)
files = []
for name, (start, end) in ranges.items():
    lines = ["Read-only pinned on-disk PE; preferred VAs, never live addresses.",
             f"Executable SHA256: {exe_hash}", f"RANGE {start:#x} {end:#x}"]
    for ins in cs.disasm(pe.get_data(start-base, end-start), start):
        lines.append(f"{ins.address:x} {ins.mnemonic} {ins.op_str}")
    target = out / (name + ".asm.txt")
    target.write_text("\n".join(lines)+"\n", encoding="utf-8")
    files.append({"file": target.name, "sha256": hashlib.sha256(target.read_bytes()).hexdigest().upper(),
                  "range": [hex(start), hex(end)]})
manifest = {"scope": "Pinned executable disk-file inspection only. No game, live memory, fixture or staging calls.",
            "executable": {"path": str(exe), "sha256": exe_hash}, "files": files,
            "axisAngleHalfConstant": {"va": "0x141d1f1ac", "float32": struct.unpack("<f", pe.get_data(0x141D1F1AC-base,4))[0]},
            "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper()}
(out / "manifest.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
print(json.dumps(manifest, indent=2))
