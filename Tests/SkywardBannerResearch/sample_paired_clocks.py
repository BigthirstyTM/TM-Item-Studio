"""Read-only native clock observations through the existing localhost DEV bridge.

The operator owns the loaded fixture. This script never changes maps, plugins,
camera, files in the game tree, or process memory. It does not inject code or
open a process handle. All live reads are existing DevSafeRead requests.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import socket
import struct
import sys
import time


EXE = Path(r"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Trackmania\Trackmania.exe")
EXE_HASH = "3FC7D8CDA542BEDA131C44306B123F4004D07D7E22F512B46B762AFC29F6EDDA"
ALLOW = {"GetMode", "GetMapInfo", "DevGetPointers", "DevSceneComponents", "GetTypeInfo", "DevSafeRead"}
ZONE_ID = 0x06004000
ZONE_VT_RVA = 0x1B652A0
ZONE_TICK_RVA = 0x1F2F70
SEQUENCE_VT_RVA = 0x1D0B8A0
SEQUENCE_APPLY_RVA = 0x13FF6B0
KIN_VIS_ID = 0x30099000


class Refusal(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise Refusal(message)


def utc():
    return datetime.now(timezone.utc).isoformat()


def integer(value):
    require(isinstance(value, (int, str)) and not isinstance(value, bool), "Expected exact integer/hex string")
    return int(value, 0) if isinstance(value, str) else value


def pointer(value, nullable=False):
    result = integer(value)
    if nullable and result == 0:
        return 0
    require(0x10000 <= result < 0x0000800000000000 and result % 8 == 0,
            f"Invalid/unexpected aligned data pointer {result:#x}")
    return result


def qword(data, offset=0):
    return struct.unpack_from("<Q", data, offset)[0]


def dword(data, offset=0):
    return struct.unpack_from("<I", data, offset)[0]


def f32(data, offset=0):
    result = struct.unpack_from("<f", data, offset)[0]
    require(math.isfinite(result), f"Nonfinite f32 at buffer offset {offset:#x}")
    return result


def dump(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


class DiskPE:
    """Tiny section reader; no process access and no third-party packages."""

    def __init__(self):
        self.data = EXE.read_bytes()
        self.sha = hashlib.sha256(self.data).hexdigest().upper()
        require(self.sha == EXE_HASH, "Executable disk hash differs from pinned research build")
        pe = dword(self.data, 0x3C)
        require(self.data[pe:pe + 4] == b"PE\0\0", "Invalid disk PE")
        count, opt_size = struct.unpack_from("<H12xH", self.data, pe + 6)
        opt = pe + 24
        require(struct.unpack_from("<H", self.data, opt)[0] == 0x20B, "Expected PE32+")
        require(qword(self.data, opt + 24) == 0x140000000, "Unexpected preferred PE base")
        self.image_size = dword(self.data, opt + 56)
        self.sections = []
        for index in range(count):
            offset = opt + opt_size + 40 * index
            virtual_size, rva, raw_size, raw = struct.unpack_from("<IIII", self.data, offset + 8)
            self.sections.append((rva, max(virtual_size, raw_size), raw, raw_size))

    def read(self, rva, size):
        for start, extent, raw, raw_size in self.sections:
            if start <= rva and rva + size <= start + extent:
                require(rva - start + size <= raw_size, "Requested disk bytes are not backed by section data")
                return self.data[raw + rva - start:raw + rva - start + size]
        raise Refusal(f"RVA {rva:#x} is outside disk PE sections")


class Bridge:
    def __init__(self, directory, deadline):
        self.deadline = time.monotonic() + deadline
        self.trace = (directory / "requests.jsonl").open("x", encoding="utf-8")
        self.calls = 0
        self.last_span = None

    def log(self, value):
        self.trace.write(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n")
        self.trace.flush()

    def call(self, name, inputs=None, optional=False):
        require(name in ALLOW, "Tool is outside the read-only allowlist")
        self.calls += 1
        require(self.calls <= 650, "Read-only request budget exhausted")
        left = self.deadline - time.monotonic()
        require(left > 0, "Total read-only observation deadline exhausted")
        request = {"route": "call", "tool": name, "input": inputs or {}}
        start = time.monotonic()
        record = {"sequence": self.calls, "utcStart": utc(), "monotonicStart": start, "request": request}
        self.log({**record, "event": "request"})
        try:
            with socket.create_connection(("127.0.0.1", 30006), timeout=min(8, left)) as connection:
                connection.settimeout(min(8, left))
                connection.sendall((json.dumps(request, separators=(",", ":")) + "\n").encode("utf-8"))
                connection.shutdown(socket.SHUT_WR)
                buffer = bytearray()
                while b"\n" not in buffer:
                    chunk = connection.recv(65536)
                    if not chunk:
                        break
                    buffer.extend(chunk)
                    require(len(buffer) <= 4 * 1024 * 1024, "Response exceeded read-only byte budget")
                response = json.loads(buffer.decode("utf-8").strip())
            end = time.monotonic()
            self.last_span = {"monotonicStart": start, "monotonicEnd": end, "seconds": end - start,
                              "sequence": self.calls}
            self.log({**record, "event": "response", "utcEnd": utc(), "monotonicEnd": end, "response": response})
            require(response.get("ok") is True, f"Bridge rejected {name}: {response.get('error')}")
            result = response.get("data", {}).get("result", {})
            if optional and result.get("success") is not True:
                return {"unavailable": result}
            require(result.get("success") is True, f"Tool rejected {name}: {result}")
            output = result.get("output")
            require(isinstance(output, dict), f"Expected object output from {name}")
            require(not output.get("error") and not output.get("readError"), f"Read refused by {name}: {output}")
            return output
        except Exception as error:
            self.log({**record, "event": "failure", "utcEnd": utc(), "reason": str(error)})
            raise

    def read(self, ptr, offset, size):
        require(isinstance(ptr, int) and 0x10000 <= ptr < 0x0000800000000000, "Invalid read root")
        require(0 <= offset <= 0x8000 and 1 <= size <= 4096, "Read exceeds bounded field/block limits")
        output = self.call("DevSafeRead", {"ptr": hex(ptr), "offset": offset, "kind": "bytes", "len": size})
        require(output.get("probe", {}).get("readable") is True, "DevSafeRead probe did not report readable")
        require(integer(output.get("addr")) == ptr + offset, "DevSafeRead address mismatch")
        block = output.get("dump", {})
        require(block.get("readBytes") == size and "truncatedAt" not in block, "Incomplete DevSafeRead block")
        values = block.get("bytes")
        require(isinstance(values, list) and len(values) == size, "Unexpected DevSafeRead byte array")
        return bytes(values)

    def ptr(self, ptr, offset=0, nullable=False):
        return pointer(qword(self.read(ptr, offset, 8)), nullable)

    def descriptor(self, root, offset, maximum):
        block = self.read(root, offset, 16)
        data, count, capacity = qword(block), dword(block, 8), dword(block, 12)
        require(count <= maximum, f"Buffer count {count} exceeds fixture bound {maximum}")
        require(count <= capacity <= 1048576, "Implausible buffer size/capacity")
        pointer(data, nullable=count == 0)
        return {"data": data, "count": count, "capacity": capacity}


def reflected_field(bridge, class_name, member, optional=False):
    query = {"className": class_name, "member": member} if class_name else {"from": "app", "member": member}
    value = bridge.call("GetTypeInfo", query, optional=optional)
    if "unavailable" in value:
        return None, value
    members = value.get("members", [])
    found = [item for item in members if item.get("name") == member and item.get("isField") is True]
    if optional and not found:
        return None, value
    require(len(found) == 1, f"Reflection did not return exactly one {class_name or 'app'}.{member} field")
    offset = integer(found[0]["offset"])
    require(0 <= offset < 0x8000 and offset != 0xFFFF, "Invalid reflected member offset")
    return offset, value


def one_component(components, name, type_id):
    found = [entry for entry in components["components"] if entry.get("name") == name]
    require(len(found) == 1, f"Expected exactly one semantic {name} component")
    entry = found[0]
    require(integer(entry["typeId"]) == type_id and entry.get("probe", {}).get("readable") is True,
            f"Wrong class ID/unreadable semantic component {name}")
    return pointer(entry["ptr"])


def map_identity(value):
    keys = ("name", "fullFileName", "nbItems", "nbBlocks")
    require(all(key in value for key in keys), "Map identity fields unavailable")
    require(value["name"].startswith("TMIS "), "Operator must load an owned TMIS fixture")
    require(value["nbItems"] <= 4, "Fixture has unexpected item count")
    return {key: value[key] for key in keys}


def identity(bridge, roots):
    require(bridge.call("GetMode").get("mode") == roots["mode"], "Owned editor/test context changed during observation")
    require(map_identity(bridge.call("GetMapInfo")) == roots["map"], "Loaded map identity changed")
    require(bridge.ptr(roots["app"], roots["offsets"]["GameScene"]) == roots["scene"], "GameScene pointer changed")
    require(bridge.ptr(roots["scene"], roots["mgrListOff"]) == roots["mgrList"], "Scene component list changed")
    require(bridge.ptr(roots["zone"]) == roots["base"] + ZONE_VT_RVA, "CHmsZone class/vtable changed")
    require(bridge.ptr(roots["zone"], 0x18) == roots["clockManager"], "Ordinary clock manager changed")
    require(bridge.ptr(roots["zone"], 0xD8) == roots["zoneTimeState"], "CHmsZone time-state pointer changed")
    require(bridge.ptr(roots["clockManager"], 8) == roots["ordinaryOwner"], "Ordinary clock owner changed")
    if roots.get("dyna"):
        require(bridge.ptr(roots["scene"], roots["offsets"]["ScenePhy"]) == roots["physics"], "ScenePhy pointer changed")
        require(bridge.ptr(roots["physics"], roots["offsets"]["Dyna"]) == roots["dyna"], "Dyna pointer changed")
        if roots.get("dynaMethodRoute"):
            require(dword(bridge.read(roots["base"] + 0x2053B84, 0, 4)) == roots["dynaMethodRoute"]["slot"],
                    "Native Dyna component slot changed")
            require(bridge.ptr(roots["dyna"]) == roots["dynaOwner"], "Native Dyna owner changed")


def resolve_dyna_method(bridge, disk, roots):
    """Pinned native getter chain, not reinterpretation of a reflection method offset."""
    reflected = roots["reflection"].get("IScenePhy.Dyna", {})
    method = [item for item in reflected.get("members", []) if item.get("name") == "Dyna"]
    if not (reflected.get("idHex", "").lower() == "0x30005000" and len(method) == 1
            and method[0].get("isField") is False and integer(method[0]["offset"]) == 0xFFFF):
        return
    # The native caller obtains the very manager passed to the offset setter
    # and kinematic updater through 6A5B40. The shared accessor is an exact
    # [scene + 0x10 + slot*8] load. No function is called by this script.
    for rva, size in [(0x6A5B40, 0x14), (0x7BA440, 7), (0x6C26A0, 8),
                      (0x6A5B00, 0x14), (0x6C4380, 7), (0x7BAAD0, 0x25)]:
        require(bridge.read(roots["base"] + rva, 0, size) == disk.read(rva, size),
                f"Pinned native Dyna getter/factory guard differs at RVA {rva:#x}")
    slot = dword(bridge.read(roots["base"] + 0x2053B84, 0, 4))
    collision_slot = dword(bridge.read(roots["base"] + 0x202DDBC, 0, 4))
    require(slot < 128 and collision_slot < 128 and slot != collision_slot,
            "Native physical component slot IDs exceed bounds or collide")
    descriptor_table = bridge.ptr(roots["base"] + 0x1FAAD30)
    require(bridge.ptr(descriptor_table, slot * 0x68) == roots["base"] + 0x7BAAD0,
            "Native Dyna slot registry factory does not match pinned NSceneDyna factory")
    offset = 0x10 + slot * 8
    dyna = bridge.ptr(roots["physics"], offset)
    collision = bridge.ptr(roots["physics"], 0x10 + collision_slot * 8)
    # The verified native factory invokes 6A5B00 on this scene and stores that
    # component at dyna+0. This is a stronger owner check than a guessed vtable.
    require(bridge.ptr(dyna) == collision, "NSceneDyna native factory owner relationship differs")
    roots["dyna"] = dyna
    roots["offsets"]["Dyna"] = offset
    roots["physicsClass"] = "IScenePhy"
    roots["dynaMethodRoute"] = {"slot": slot, "slotIdGlobalRva": "0x2053B84",
                                "collisionSlot": collision_slot, "collisionSlotIdGlobalRva": "0x202DDBC",
                                "descriptorTable": descriptor_table, "factoryRva": "0x7BAAD0",
                                "offsetProvenance": "Pinned native method getter plus live registry factory/owner checks; not reflection field offset"}


def discover(bridge, disk, expected_map):
    mode = bridge.call("GetMode").get("mode")
    require(mode in ("MapEditor", "MapEditor_Test"), "Load the owned editor fixture or its MapEditor_Test context before running")
    info = bridge.call("GetMapInfo")
    require(not expected_map or info.get("name") == expected_map, "The loaded map is not --expect-map")
    roots = {"mode": mode, "map": map_identity(info), "offsets": {}, "reflection": {}}
    nods = bridge.call("DevGetPointers")
    components = bridge.call("DevSceneComponents", {"maxSlots": 2})
    roots["base"] = pointer(nods["base"])
    require(pointer(components["base"]) == roots["base"], "Module bases disagree")
    roots["mgrListOff"] = integer(components["mgrListOff"])
    roots["mgrList"] = pointer(components["mgrList"]["ptr"])
    roots["components"] = components["components"]
    app = [entry for entry in nods["nods"] if entry.get("label") == "app" and not entry.get("isNull")]
    require(len(app) == 1, "No unique semantic app root")
    roots["app"] = pointer(app[0]["ptr"])

    # Only relocation-free instructions/data are compared byte-for-byte.
    guards = [(0x1F3F40, 0x39), (0x1F3044, 0x55), (0x1F3CD0, 0x20), (0x7BD0D0, 7),
              (0x801E63, 7), (SEQUENCE_APPLY_RVA, 0x19), (0x1D1ED94, 4)]
    for rva, size in guards:
        require(bridge.read(roots["base"] + rva, 0, size) == disk.read(rva, size),
                f"Live pinned-build guard differs at RVA {rva:#x}")
    require(bridge.ptr(roots["base"] + ZONE_VT_RVA, 0xF0) == roots["base"] + ZONE_TICK_RVA,
            "CHmsZone native tick vtable slot differs")
    require(bridge.ptr(roots["base"] + SEQUENCE_VT_RVA, 0xF8) == roots["base"] + SEQUENCE_APPLY_RVA,
            "Subvisual sequence native apply slot differs")

    for class_name, member in [(None, "GameScene"), ("ISceneVis", "ScenePhy")]:
        offset, reflection = reflected_field(bridge, class_name, member)
        roots["offsets"][member] = offset
        roots["reflection"][member] = reflection
    roots["scene"] = bridge.ptr(roots["app"], roots["offsets"]["GameScene"])
    require(bridge.ptr(roots["scene"], roots["mgrListOff"]) == roots["mgrList"], "Reflected scene/list mismatch")
    roots["zone"] = one_component(components, "CHmsZone", ZONE_ID)
    require(bridge.ptr(roots["zone"]) == roots["base"] + ZONE_VT_RVA, "Unexpected CHmsZone live vtable")
    roots["clockManager"] = bridge.ptr(roots["zone"], 0x18)
    roots["ordinaryOwner"] = bridge.ptr(roots["clockManager"], 8)
    roots["zoneTimeState"] = bridge.ptr(roots["zone"], 0xD8)
    roots["ordinaryOwnerIsZoneTimeState"] = roots["ordinaryOwner"] == roots["zoneTimeState"]
    roots["physics"] = bridge.ptr(roots["scene"], roots["offsets"]["ScenePhy"])
    roots["ordinaryOwnerEquals"] = {name: roots["ordinaryOwner"] == roots[name]
                                    for name in ("app", "scene", "zone", "clockManager", "physics")}
    # Reflection names are queried; no memory is followed through a guessed offset.
    for class_name in ("IScenePhys", "IScenePhy"):
        offset, reflection = reflected_field(bridge, class_name, "Dyna", optional=True)
        roots["reflection"][class_name + ".Dyna"] = reflection
        if offset is not None:
            require("Dyna" not in roots["offsets"], "Ambiguous reflected physics Dyna fields")
            roots["offsets"]["Dyna"] = offset
            roots["physicsClass"] = class_name
    if "Dyna" not in roots["offsets"]:
        resolve_dyna_method(bridge, disk, roots)
    if "Dyna" in roots["offsets"]:
        roots["dyna"] = bridge.ptr(roots["physics"], roots["offsets"]["Dyna"])
        # Raw NSceneDyna_SMgr begins with an owner pointer, not a CMwNod vtable.
        roots["dynaOwner"] = bridge.ptr(roots["dyna"])
        roots["dynaOwnerEquals"] = {name: roots["dynaOwner"] == roots[name]
                                   for name in ("app", "scene", "zone", "physics", "ordinaryOwner")}
        for member in ("Items", "KinematicConstraints", "KinematicSharedSignals"):
            offset, reflection = reflected_field(bridge, "NSceneDyna_SMgr", member, optional=True)
            roots["reflection"]["NSceneDyna_SMgr." + member] = reflection
            if offset is not None:
                roots["offsets"][member] = offset
    kin = [entry for entry in components["components"] if entry.get("name") == "NSceneKinematicVis_SMgr"]
    if kin:
        roots["kinematicVis"] = one_component(components, "NSceneKinematicVis_SMgr", KIN_VIS_ID)
    identity(bridge, roots)
    return roots


def ordinary_snapshot(bridge, roots):
    header = bridge.read(roots["clockManager"], 0x390, 16)
    groups, count = qword(header), dword(header, 8)
    require(count <= 8, "Ordinary clock group count exceeds fixture bound")
    pointer(groups, nullable=count == 0)
    pointers = bridge.read(groups, 0, count * 8) if count else b""
    result = []
    total = 0
    for group_index in range(count):
        descriptor = pointer(qword(pointers, group_index * 8))
        group = bridge.descriptor(descriptor, 0, 32)
        total += group["count"]
        require(total <= 32, "Ordinary FuncTree total exceeds fixture bound")
        entries = bridge.read(group["data"], 0, group["count"] * 0x18) if group["count"] else b""
        for index in range(group["count"]):
            tree, function, state = struct.unpack_from("<QQQ", entries, index * 0x18)
            pointer(tree)
            pointer(function)
            details = bridge.read(function, 0, 0x30)
            vtable = qword(details)
            record = {"group": group_index, "index": index, "descriptor": descriptor, "tree": tree,
                      "function": function, "stateRaw": hex(state), "functionVtable": hex(vtable),
                      "functionVtableRva": hex(vtable - roots["base"]),
                      "autoCreateMotion": dword(details, 0x18), "periodSeconds": f32(details, 0x28),
                      "phase": f32(details, 0x2C)}
            # Only the exact native subvisual class has its additional fields interpreted.
            if vtable == roots["base"] + SEQUENCE_VT_RVA:
                require(bridge.ptr(tree, 0xC0) == function, "Registered tree/function relationship differs")
                require(record["periodSeconds"] > 0, "Subvisual sequence period must be positive")
                sequence_details = bridge.read(function, 0x38, 0x14)
                packed = dword(bridge.read(tree, 0xE8, 4))
                record.update(sequence=True, sequenceTableRaw=hex(qword(sequence_details)),
                              looping=dword(sequence_details, 8), frameStart=dword(sequence_details, 12),
                              frameEnd=dword(sequence_details, 16), packedTween=hex(packed),
                              frame1=packed & 0xFFF, frame2=(packed >> 12) & 0xFFF,
                              tweenByte=packed >> 24, blend=(packed >> 24) / 255)
                record["packedReadSpan"] = dict(bridge.last_span)
            result.append(record)
        require(bridge.read(descriptor, 0, 16) == struct.pack("<QII", group["data"], group["count"], group["capacity"]),
                "Ordinary registration buffer changed during traversal")
    require(bridge.read(roots["clockManager"], 0x390, 16) == header, "Ordinary group list changed during traversal")
    return result


def kinematic_snapshot(bridge, roots):
    if "kinematicVis" not in roots:
        return {"unavailable": "No semantic NSceneKinematicVis_SMgr component"}
    descriptor = bridge.descriptor(roots["kinematicVis"], 8, 8)
    count = descriptor["count"]
    block = bridge.read(descriptor["data"], 0, count * 0x58) if count else b""
    span = dict(bridge.last_span)
    records = []
    for index in range(count):
        entry = block[index * 0x58:(index + 1) * 0x58]
        signal = pointer(qword(entry))
        shared = bridge.read(signal, 0, 0x40)
        shared_span = dict(bridge.last_span)
        records.append({"index": index, "signal": signal, "model": pointer(qword(shared)),
                        "phase": f32(shared, 8), "signalBlockHex": shared.hex(),
                        "sharedReadSpan": shared_span,
                        "sharedMatrix3x3RawOrder": [f32(shared, 0xC + i * 4) for i in range(9)],
                        "sharedTranslation": [f32(shared, 0x30 + i * 4) for i in range(3)],
                        "constraintBlockHex": entry.hex(), "constraintReadSpan": span,
                        "matrix3x3RawOrder": [f32(entry, 0x28 + i * 4) for i in range(9)],
                        "position": [f32(entry, 0x4C + i * 4) for i in range(3)],
                        "hmsClassId": hex(dword(entry, 0x18)), "hmsInstance": entry[0x1C]})
    require(bridge.descriptor(roots["kinematicVis"], 8, 8) == descriptor, "Kinematic visual list changed")
    return {"descriptor": descriptor, "records": records}


def clocks(bridge, roots):
    # The verified CHmsZone tick writes origin/incoming/elapsed at +1A4/8/C.
    # An origin is associated with the ordinary consumer only on exact equality
    # of that native state pointer with manager+8. No semantic type is guessed.
    zone_state = bridge.read(roots["zoneTimeState"], 0x1A4, 16)
    zone_span = dict(bridge.last_span)
    zone_clock = {"originMs": dword(zone_state), "incomingMs": dword(zone_state, 4),
                  "elapsedMs": dword(zone_state, 8), "tickArgumentFloat": f32(zone_state, 12),
                  "originFormulaMatches": dword(zone_state, 8) == ((dword(zone_state, 4) - dword(zone_state)) & 0xFFFFFFFF),
                  "readSpan": zone_span}
    if roots["ordinaryOwnerIsZoneTimeState"]:
        ordinary, ordinary_span = zone_clock["elapsedMs"], zone_span
    else:
        ordinary = dword(bridge.read(roots["ordinaryOwner"], 0x1AC, 4))
        ordinary_span = dict(bridge.last_span)
    # ScenePhy-12 is the visual scene clock inspected by E++ KinematicsMainTab.
    # It is recorded as an observation, not asserted to be the argument passed
    # to 0x1407BD0E0 on this frame.
    scene_clock = dword(bridge.read(roots["scene"], roots["offsets"]["ScenePhy"] - 12, 4))
    scene_span = dict(bridge.last_span)
    result = {"ordinaryOwnerTimeMs": ordinary, "ordinaryReadSpan": ordinary_span,
              "zoneClock": zone_clock, "ordinaryOwnerIsZoneTimeState": roots["ordinaryOwnerIsZoneTimeState"],
              "visualSceneTimeMs": scene_clock, "visualSceneReadSpan": scene_span}
    if roots.get("dyna"):
        result["kinematicOffsetMs"] = dword(bridge.read(roots["dyna"], 0x7A38, 4))
        result["kinematicOffsetReadSpan"] = dict(bridge.last_span)
    return result


def sample(bridge, roots, index):
    identity(bridge, roots)
    result = {"index": index, "utc": utc(), "monotonicStart": time.monotonic(), "before": clocks(bridge, roots)}
    result["ordinaryFunctions"] = ordinary_snapshot(bridge, roots)
    result["kinematicVisuals"] = kinematic_snapshot(bridge, roots)
    result["after"] = clocks(bridge, roots)
    identity(bridge, roots)
    result["monotonicEnd"] = time.monotonic()
    result["spanSeconds"] = result["monotonicEnd"] - result["monotonicStart"]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="New append-only evidence directory")
    parser.add_argument("--discover", action="store_true", help="Resolve semantic roots/build guards only; do not traverse registrations")
    parser.add_argument("--expect-map", default="", help="Require this exact owned map display name")
    parser.add_argument("--samples", type=int, default=3)
    parser.add_argument("--interval", type=float, default=2)
    parser.add_argument("--deadline", type=float, default=90)
    args = parser.parse_args()
    require(1 <= args.samples <= 6 and 0 <= args.interval <= 8 and 15 <= args.deadline <= 120, "CLI bounds exceeded")
    destination = args.output.resolve()
    require(not destination.exists(), "Preserve previous evidence; choose an unused output directory")
    destination.mkdir(parents=True)
    bridge = None
    try:
        disk = DiskPE()
        dump(destination / "scope.json", {
            "scope": "Read-only observations via existing localhost semantic DEV bridge. No staging, game mutations, writes/injection or process handles.",
            "executable": str(EXE), "executableSha256": disk.sha,
            "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper(),
            "allowlist": sorted(ALLOW), "arguments": vars(args) | {"output": str(destination)},
            "limits": "Sequential RPCs are not atomic paired samples. All timing spans are retained. Raw manager owner identities and the physical update time argument are not assumed."})
        bridge = Bridge(destination, args.deadline)
        roots = discover(bridge, disk, args.expect_map)
        dump(destination / "roots.json", roots)
        if not args.discover:
            for index in range(args.samples):
                if index:
                    require(time.monotonic() + args.interval < bridge.deadline, "Insufficient time for next sample")
                    time.sleep(args.interval)
                observation = sample(bridge, roots, index)
                dump(destination / f"sample-{index:02}.json", observation)
        summary = {"success": True, "discoveryOnly": args.discover, "requests": bridge.calls,
                   "map": roots["map"], "physicalRootResolved": "dyna" in roots,
                   "limits": "Does not establish attachment, clock synchronization or embedded/player-plugin-free behavior. Interpret measured state with matching video and fixture bytes."}
        dump(destination / "result.json", summary)
        print(json.dumps(summary, indent=2))
        return 0
    except Exception as error:
        failure = {"success": False, "utc": utc(), "reason": str(error), "exceptionType": type(error).__name__,
                   "requests": bridge.calls if bridge else 0, "scope": "Stopped on first failure; no write/mutation fallback."}
        dump(destination / "failure.json", failure)
        print(json.dumps(failure, indent=2), file=sys.stderr)
        return 2
    finally:
        if bridge:
            bridge.trace.close()


if __name__ == "__main__":
    sys.exit(main())
