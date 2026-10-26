"""Audit retained native VertexTween VS timing; file reads only, no live device."""
import hashlib
import json
from pathlib import Path
import struct


REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "artifacts/flag-vanilla-steps-20261021/gpu-tools/shader-cache"
OUTPUT = REPO / "artifacts/skyward-banner-20261023/material-candidates-vertex-timing-v1"
NAME = "Warp_TDiffSpec_VertexTween_v.hlsl.GpuCache.Gbx.raw"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def float32(word):
    return struct.unpack("<f", struct.pack("<I", word))[0]


def main():
    raw = (SOURCE / NAME).read_bytes()
    retained = json.loads((SOURCE / "disassembly-manifest.json").read_text())
    rows = []
    common_timing = None
    for entry in (r for r in retained if r["file"] == NAME):
        offset, size = entry["offset"], entry["bytes"]
        bytecode = raw[offset:offset + size]
        assert bytecode[:4] == b"DXBC"
        assert struct.unpack_from("<I", bytecode, 24)[0] == size
        chunk_count = struct.unpack_from("<I", bytecode, 28)[0]
        chunks = struct.unpack_from("<" + "I" * chunk_count, bytecode, 32)
        constants = []
        for chunk_offset in chunks:
            tag = bytecode[chunk_offset:chunk_offset + 4]
            if tag not in (b"SHEX", b"SHDR"):
                continue
            chunk_size = struct.unpack_from("<I", bytecode, chunk_offset + 4)[0]
            words = struct.unpack_from("<" + "I" * (chunk_size // 4), bytecode, chunk_offset + 8)
            assert words[1] == len(words)
            instruction = 2
            while instruction < len(words):
                length = (words[instruction] >> 24) & 127
                assert length > 0
                tokens = words[instruction:instruction + length]
                for index, word in enumerate(tokens):
                    value = float32(word)
                    if .00386 < value < .00387 or .000244 < value < .000245:
                        assert index > 0 and tokens[index - 1] == 0x4001
                        constants.append({
                            "instructionTokenOffset": instruction,
                            "opcode": tokens[0] & 2047,
                            "literalWordIndex": index,
                            "literalBits": f"0x{word:08X}",
                            "literalFloat": value,
                            "rawFileLiteralByteOffset": offset + chunk_offset + 8 + (instruction + index) * 4,
                        })
                instruction += length
            assert instruction == len(words)
        assert [c["literalBits"] for c in constants] == ["0x3B7D24E4", "0x39800801", "0x39800801"]
        assert [c["opcode"] for c in constants] == [50, 56, 50]  # MAD, MUL, MAD
        assembly_path = SOURCE / entry["disassembly"]
        assembly_bytes = assembly_path.read_bytes()
        assembly = assembly_bytes.decode("utf-8").replace("\r\n", "\n")
        start = assembly.index("  and r0.x, r2.w, l(2047)\n")
        end = assembly.index("  mov r6.y, r4.z\n", start)
        timing = assembly[start:end]
        if common_timing is None:
            common_timing = timing
        assert timing == common_timing
        assert "ine r0.x, cb0[0].x, l(-1)\n" in assembly
        assert "  div r0.x, cb0[0].w, r0.x\n" in timing
        assert "  mul r0.x, r0.x, r2.w\n" in timing
        rows.append({
            "retainedVariant": len(rows), "rawOffset": offset,
            "bytecodeBytes": size, "bytecodeSha256": sha(bytecode),
            "assembly": str(assembly_path.relative_to(REPO)),
            "assemblySha256": sha(assembly_bytes),
            "normalizedTimingBlockSha256": sha(timing.encode()),
            "exactTimingConstants": constants,
        })
    assert len(rows) == 4
    OUTPUT.mkdir(parents=True, exist_ok=True)
    manifest = {
        "scope": "Retained native DXBC file-only audit; no live draw, GPU buffer or CPU-to-GPU upload was captured.",
        "source": str((SOURCE / NAME).relative_to(REPO)),
        "sourceSha256": sha(raw),
        "sourceBytes": len(raw),
        "periodCoefficient": {"bits": "0x3B7D24E4", "float": float32(0x3B7D24E4)},
        "phaseCoefficient": {"bits": "0x39800801", "float": float32(0x39800801)},
        "variants": rows,
    }
    (OUTPUT / "audit.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (OUTPUT / "common-timing.asm.txt").write_text(common_timing, encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
