"""Read native CPU HMS instance timing through the existing read-only bridge.

This does not capture a GPU draw or assert that its uploaded constants equal
the retained CPU state. No camera/map/plugin actions, writes or injection.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import time

import sample_paired_clocks as guarded


def packed(word):
    period_code = word & 2047
    phase_code = (word >> 11) & 4095
    a = guarded.f32(bytes.fromhex("e4247d3b"))
    b = guarded.f32(bytes.fromhex("01088039"))
    return {"word": word, "hex": f"0x{word:08X}", "periodCode": period_code,
            "periodSeconds": 2**(period_code*a-2) if period_code < 2047 else None,
            "timeDisabled": period_code == 2047, "phaseCode": phase_code,
            "phase01": phase_code*b, "paletteOrSlice": (word >> 23) & 255,
            "nativeHighBit": bool(word & 0x80000000)}


def snapshot(bridge, roots):
    mgr = roots["hmsInstances"]
    guarded.require(bridge.ptr(roots["zone"], 0x248) == mgr, "Zone instance manager changed")
    mode = guarded.dword(bridge.read(mgr, 0x30, 4))
    guarded.require(mode in (0, 1), "Native manager storage mode is outside the two code-derived layouts")
    model_desc = bridge.descriptor(mgr, 0x60, 32)
    instances = bridge.descriptor(mgr, 0xC0, 128)
    model_indices = bridge.descriptor(mgr, 0xE0, 128)
    descriptors = {"models": model_desc, "instances": instances, "modelIndices": model_indices}
    descriptor_offsets = {"models": (0x60, 32), "instances": (0xC0, 128), "modelIndices": (0xE0, 128)}
    guarded.require(instances["count"] == model_indices["count"], "Instance and model-index counts differ")
    model_bytes = bridge.read(model_desc["data"], 0, model_desc["count"]*0x78) if model_desc["count"] else b""
    instance_bytes = bridge.read(instances["data"], 0, instances["count"]*32) if instances["count"] else b""
    instance_span = bridge.last_span
    indices = bridge.read(model_indices["data"], 0, model_indices["count"]*4) if model_indices["count"] else b""
    mapped, staged_bytes, staged_indices = b"", b"", b""
    staged_span = None
    if mode == 1:
        for name, offset in (("stableIdMap", 0x1D8), ("stagedInstances", 0x1E8), ("stagedModelIndices", 0x208)):
            descriptors[name] = bridge.descriptor(mgr, offset, 128)
            descriptor_offsets[name] = (offset, 128)
        stable_ids, staged, staged_models = (descriptors[n] for n in ("stableIdMap", "stagedInstances", "stagedModelIndices"))
        guarded.require(staged["count"] == staged_models["count"], "Staged instance and model-index counts differ")
        mapped = bridge.read(stable_ids["data"], 0, stable_ids["count"]*4) if stable_ids["count"] else b""
        staged_bytes = bridge.read(staged["data"], 0, staged["count"]*32) if staged["count"] else b""
        staged_span = bridge.last_span
        staged_indices = bridge.read(staged_models["data"], 0, staged_models["count"]*4) if staged_models["count"] else b""
    models = []
    for index in range(model_desc["count"]):
        row = model_bytes[index*0x78:(index+1)*0x78]
        source = guarded.pointer(guarded.qword(row), nullable=True)
        render = guarded.pointer(guarded.qword(row, 0x10), nullable=True)
        models.append({"index": index, "sourceSolid2": source, "renderSolid2": render,
                       "referenceCount": guarded.dword(row, 0x58), "hex": row.hex()})
    records = []
    for index in range(descriptors["stableIdMap"]["count"] if mode == 1 else instances["count"]):
        mapping = guarded.dword(mapped, index*4) if mode == 1 else index
        storage_index = mapping & 0x7FFFFFFF
        if mode == 1 and storage_index == 0x7FFFFFFF:
            records.append({"index": index, "activeModel": False, "deadStableId": True, "mapping": mapping})
            continue
        from_staged = mode == 1 and bool(mapping & 0x80000000)
        source_descriptor = descriptors["stagedInstances"] if from_staged else instances
        source_bytes = staged_bytes if from_staged else instance_bytes
        source_indices = staged_indices if from_staged else indices
        guarded.require(storage_index < source_descriptor["count"], "Stable instance ID maps beyond its native storage table")
        row = source_bytes[storage_index*32:(storage_index+1)*32]
        raw_slot = guarded.dword(source_indices, storage_index*4)
        slot = raw_slot & 0xFF7FFFFF if mode == 1 else raw_slot
        guarded.require(slot == 0xFFFFFFFF or slot < len(models), "Instance model slot exceeds native model table")
        active = slot != 0xFFFFFFFF and models[slot]["sourceSolid2"] != 0
        record = {"index": index, "modelIndex": slot, "rawModelIndex": raw_slot,
                  "storage": "staged1E8" if from_staged else "mainC0", "storageIndex": storage_index,
                  "mapping": mapping, "activeModel": active, "hex": row.hex(),
                  "packedTiming": packed(guarded.dword(row, 28))}
        if active:
            record["sourceSolid2"] = models[slot]["sourceSolid2"]
            record["quaternionRawOrder"] = [guarded.f32(row, i*4) for i in range(4)]
            record["translation"] = [guarded.f32(row, 16+i*4) for i in range(3)]
        records.append(record)
    guarded.require(bridge.ptr(roots["zone"], 0x248) == mgr, "Zone instance manager changed after snapshot")
    guarded.require(guarded.dword(bridge.read(mgr, 0x30, 4)) == mode, "Native instance storage mode changed during snapshot")
    for name, (offset, maximum) in descriptor_offsets.items():
        guarded.require(bridge.descriptor(mgr, offset, maximum) == descriptors[name], "Native instance buffers changed during snapshot")
    if mode == 1 and descriptors["stableIdMap"]["count"]:
        guarded.require(bridge.read(descriptors["stableIdMap"]["data"], 0, len(mapped)) == mapped,
                        "Stable native instance mapping changed during snapshot")
    return {"mode": mode, "models": models, "instances": records, "instanceReadSpan": instance_span,
            "stagedInstanceReadSpan": staged_span, "descriptors": descriptors,
            "limits": "Sequential CPU reads; not a captured GPU draw. Model pointers are code-derived native source links, not file identities."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--expect-map", required=True)
    args = parser.parse_args()
    directory = args.output.resolve()
    guarded.require(not directory.exists(), "Choose an unused evidence directory")
    directory.mkdir(parents=True)
    guarded.dump(directory/"scope.json", {"scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
                                         "startedUtc": guarded.utc(), "scope": __doc__})
    bridge = guarded.Bridge(directory, 120)
    try:
        disk = guarded.DiskPE()
        roots = guarded.discover(bridge, disk, args.expect_map)
        # Exact pinned native caller obtains this manager at CHmsZone+0x248.
        # The allocator stores a 32-byte quaternion/translation/packed-word
        # record at manager+C0 and its model-slot link at manager+E0.
        for rva, size in ((0x260EA0, 0x105), (0x6A4080, 0x135), (0x262E20, 0x323), (0x2623B0, 0x23D),
                          (0x264DC0, 0x49), (0x263380, 0x176)):
            guarded.require(bridge.read(roots["base"]+rva, 0, size) == disk.read(rva, size),
                            f"Pinned instance packer/storage guard differs at RVA {rva:#x}")
        roots["hmsInstances"] = guarded.one_component({"components": roots["components"]}, "NHmsMgrInstDyna_SMgr", 0x2F079000)
        guarded.require(bridge.ptr(roots["zone"], 0x248) == roots["hmsInstances"],
                        "Semantic HMS instance component does not match native zone+248 allocator route")
        guarded.dump(directory/"roots.json", roots)
        guarded.identity(bridge, roots)
        result = snapshot(bridge, roots)
        guarded.dump(directory/"snapshot.json", result)
        guarded.identity(bridge, roots)
        guarded.dump(directory/"result.json", {"success": True, "requests": bridge.calls, "map": roots["map"],
                                              "activeInstances": sum(r["activeModel"] for r in result["instances"]),
                                              "limits": result["limits"]})
        active = [r for r in result["instances"] if r["activeModel"]]
        print(json.dumps({"success": True, "requests": bridge.calls, "instanceCount": len(result["instances"]),
                          "activeInstances": len(active), "distinctPeriodCodes": sorted({r["packedTiming"]["periodCode"] for r in active}),
                          "nonstaticInstancesFirst16": [{k: r[k] for k in ("index", "modelIndex", "packedTiming")} for r in active if not r["packedTiming"]["timeDisabled"]][:16]}, indent=2))
    except Exception as exc:
        guarded.dump(directory/"failure.json", {"success": False, "utc": guarded.utc(), "error": str(exc), "requests": bridge.calls})
        raise
    finally:
        bridge.trace.close()


if __name__ == "__main__":
    main()
