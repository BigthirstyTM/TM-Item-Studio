"""Bracket native HMS timing/rotation snapshots with guarded scene clocks.

Read-only existing bridge requests only. The operator owns map/game state.
This records native-selected shader clock candidates, not a GPU draw capture.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
import time

import sample_paired_clocks as guarded
import sample_hms_instance_timing as hms


def selected_state(bridge, roots):
    """Exact state-selection branches of native 0x1409536F0.

    The renderer's engine/renderContext identity is deliberately not inferred.
    We read the state its setter would select for this semantic CHmsZone.
    """
    special = bridge.ptr(roots["zone"], 0x460, nullable=True)
    if special:
        gbase = roots["base"]
        guarded.require(bridge.ptr(special) == gbase+0x1C19570,
                        "Special render-zone holder vtable differs from native constructor")
        guarded.require(bridge.ptr(special, 8) == roots["zone"],
                        "Special render-zone holder does not point back to the semantic zone")
    state = bridge.ptr(special, 0x128, nullable=True) if special else 0
    special_state = state
    if not state:
        state = bridge.ptr(roots["zone"], 0xD8)
    # Native 0x1409496C0..752 assigns special+128 to either zone+D8 or a
    # freshly allocated 0xA18-byte memcpy of it. It is a render-frame pointer,
    # not an owner identity that can be required to persist across RPCs.
    clock_bytes = bridge.read(state, 0x1A4, 16)
    milliseconds = guarded.dword(clock_bytes, 8)
    span = dict(bridge.last_span)
    guarded.require(bridge.ptr(roots["zone"], 0x460, nullable=True) == special,
                    "Zone special render-clock owner changed")
    later_special_state = bridge.ptr(special, 0x128, nullable=True) if special else 0
    guarded.require(bridge.ptr(roots["zone"], 0xD8) == roots["zoneTimeState"],
                    "Fallback zone clock changed")
    return {"zoneSpecialOwner": special, "zoneSpecialState": special_state,
            "selectedState": state, "selection": "special+128" if special_state else "zone+D8",
            "selectedTimeMs": milliseconds, "readSpan": span,
            "clockFields": {"originMs": guarded.dword(clock_bytes), "incomingMs": guarded.dword(clock_bytes, 4),
                            "elapsedMs": milliseconds, "tickArgumentFloat": guarded.f32(clock_bytes, 12),
                            "originFormulaMatches": milliseconds == ((guarded.dword(clock_bytes, 4)-guarded.dword(clock_bytes)) & 0xFFFFFFFF)},
            "laterSpecialState": later_special_state, "frameCopyPointerChanged": later_special_state != special_state,
            "equalsOrdinaryOwner": state == roots["ordinaryOwner"],
            "equalsZoneFallback": state == roots["zoneTimeState"],
            "limit": "Native setter state candidate for this zone. +128 is proven per-frame state copy and may change during RPCs. renderContext+838 not independently rooted."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--expect-map", required=True)
    parser.add_argument("--samples", type=int, default=3)
    parser.add_argument("--interval", type=float, default=2)
    parser.add_argument("--deadline", type=float, default=120)
    args = parser.parse_args()
    guarded.require(1 <= args.samples <= 3 and 0 <= args.interval <= 8 and 30 <= args.deadline <= 180,
                    "CLI bounds exceeded")
    directory = args.output.resolve()
    guarded.require(not directory.exists(), "Preserve previous evidence; choose an unused directory")
    directory.mkdir(parents=True)
    guarded.dump(directory/"scope.json", {"scope": __doc__, "arguments": vars(args) | {"output": str(directory)},
                                         "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
                                         "limits": "CPU state and sequential RPC spans, not exact GPU uploads. Solid2 links are retained without assigning file/mesh identities."})
    bridge = guarded.Bridge(directory, args.deadline)
    try:
        disk = guarded.DiskPE()
        roots = guarded.discover(bridge, disk, args.expect_map)
        for rva, size in ((0x260EA0, 0x105), (0x6A4080, 0x135), (0x262E20, 0x323),
                          (0x2623B0, 0x23D), (0x264DC0, 0x49), (0x263380, 0x176),
                          (0x9536F0, 0x127), (0xA9C080, 0x100),
                          (0x9496C0, 0x92), (0xA1DE60, 0x20)):
            guarded.require(bridge.read(roots["base"]+rva, 0, size) == disk.read(rva, size),
                            f"Pinned native clock/HMS guard differs at RVA {rva:#x}")
        roots["hmsInstances"] = guarded.one_component({"components": roots["components"]},
                                                        "NHmsMgrInstDyna_SMgr", 0x2F079000)
        guarded.require(bridge.ptr(roots["zone"], 0x248) == roots["hmsInstances"],
                        "Semantic HMS instance manager differs from native allocator route")
        guarded.dump(directory/"roots.json", roots)
        samples = []
        for index in range(args.samples):
            if index:
                guarded.require(time.monotonic()+args.interval < bridge.deadline, "Deadline before next sample")
                time.sleep(args.interval)
            guarded.identity(bridge, roots)
            start = time.monotonic()
            observation = {"index": index, "utc": guarded.utc(), "monotonicStart": start,
                           "before": guarded.clocks(bridge, roots),
                           "selectedShaderClockBefore": selected_state(bridge, roots)}
            observation["hms"] = hms.snapshot(bridge, roots)
            observation["kinematicVisuals"] = guarded.kinematic_snapshot(bridge, roots)
            observation["selectedShaderClockAfter"] = selected_state(bridge, roots)
            observation["after"] = guarded.clocks(bridge, roots)
            guarded.identity(bridge, roots)
            observation["monotonicEnd"] = time.monotonic()
            observation["spanSeconds"] = observation["monotonicEnd"]-start
            guarded.dump(directory/f"sample-{index:02}.json", observation)
            samples.append(observation)
        result = {"success": True, "requests": bridge.calls, "map": roots["map"], "samples": len(samples),
                  "limits": "Native CPU timing/rotation and selected zone state. No exact shader binding, attachment, mesh identity or portability claim."}
        guarded.dump(directory/"result.json", result)
        print(json.dumps(result, indent=2))
        return 0
    except Exception as error:
        guarded.dump(directory/"failure.json", {"success": False, "utc": guarded.utc(),
                                               "error": str(error), "requests": bridge.calls})
        raise
    finally:
        bridge.trace.close()


if __name__ == "__main__":
    sys.exit(main())
