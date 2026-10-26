"""Resolve the current owned anchor's reflected prefab/mesh links to HMS models.

Existing read-only bridge only. No preload, mutation, camera or plugin actions.
Exact node classes are checked by inspecting pinned native virtual class getters.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

import sample_paired_clocks as g
import sample_hms_instance_timing as hms


def node_class(bridge, disk, base, nod, expected=None):
    """Native classifier 0x140598590 calls nod vtable+18 with u32 outptr.

    Accept only the exact simple `mov [rdx],imm32; mov rax,rdx; ret` getter.
    Do not invoke a vfunc or infer a class merely from readable object memory.
    """
    vt = bridge.ptr(nod)
    g.require(0 <= vt-base < disk.image_size-0x20, "Node vtable outside pinned module")
    method = bridge.ptr(vt, 0x18)
    preferred = g.qword(disk.read(vt-base+0x18, 8))
    g.require(method == base+preferred-0x140000000, "Virtual class getter differs from pinned vtable")
    code = bridge.read(method, 0, 10)
    g.require(code == disk.read(method-base, 10), "Virtual class getter bytes differ from pinned executable")
    helper_record = None
    if code[:2] == bytes.fromhex("c702") and code[6:] == bytes.fromhex("488bc2c3"):
        type_id = g.dword(code, 2)
    else:
        # NPlugItem_SVariantList's observed getter is a native wrapper. Complete
        # pinned disassembly 0x140629DD0 moves the same outputptr RDX into RCX,
        # calls a constant-ID leaf, restores RAX=RDX and returns. Accept only
        # this exact instruction sequence and the exact constant-ID leaf shape.
        code = bridge.read(method, 0, 20)
        g.require(code == disk.read(method-base, 20), "Class wrapper differs from pinned executable")
        g.require(code[:8] == bytes.fromhex("4883ec28488bcae8") and
                  code[12:] == bytes.fromhex("488bc24883c428c3"),
                  "Node class getter is outside the two inspected native shapes")
        helper = method+12+struct.unpack_from("<i", code, 8)[0]
        g.require(0 <= helper-base < disk.image_size-10, "Class-ID helper outside pinned module")
        helper_code = bridge.read(helper, 0, 10)
        g.require(helper_code == disk.read(helper-base, 10), "Class-ID helper differs from pinned executable")
        g.require(helper_code[:2] == bytes.fromhex("c701") and helper_code[6:] == bytes.fromhex("488bc1c3"),
                  "Class-ID helper is outside inspected constant store/return shape")
        type_id = g.dword(helper_code, 2)
        helper_record = {"address": helper, "code": helper_code.hex()}
    g.require(expected is None or type_id == expected,
              f"Node class {type_id:#x} differs from required {expected:#x}")
    return {"ptr": nod, "classId": type_id, "classIdHex": hex(type_id), "vtable": vt,
            "classGetter": method, "getterCode": code.hex(), "constantIdHelper": helper_record}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--expect-map", required=True)
    parser.add_argument("--anchor", type=int, default=1)
    parser.add_argument("--entities", type=int, default=3)
    args = parser.parse_args()
    g.require(0 <= args.anchor <= 3 and args.entities in (2, 3), "Fixture bounds exceeded")
    directory = args.output.resolve()
    g.require(not directory.exists(), "Preserve previous evidence; choose an unused directory")
    directory.mkdir(parents=True)
    g.dump(directory/"scope.json", {"scope": __doc__, "arguments": vars(args) | {"output": str(directory)},
                                  "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest().upper()})
    bridge = g.Bridge(directory, 150)
    try:
        disk = g.DiskPE()
        roots = g.discover(bridge, disk, args.expect_map)
        for rva, size in ((0x598590, 0x31), (0x4374BF, 0xF), (0x43E080, 0x9B),
                          (0xA9C080, 0x161), (0x264DC0, 0x49), (0x263380, 0x176)):
            g.require(bridge.read(roots["base"]+rva, 0, size) == disk.read(rva, size),
                      f"Native prefab/visual/HMS code guard differs at RVA {rva:#x}")
        fields = {}
        reflection = {}
        for class_name, member in (("CGameCtnAnchoredObject", "ItemModel"), ("CGameItemModel", "EntityModel"),
                                   ("NPlugItem_SVariantList", "Variants"), ("NPlugItem_SVariant", "EntityModel"),
                                   ("CPlugPrefab", "Ents"), ("NPlugPrefab_SEntRef", "Model"),
                                   ("CPlugDynaObjectModel", "Mesh")):
            key = class_name+"."+member
            fields[key], reflection[key] = g.reflected_field(bridge, class_name, member)
        variant_stride = reflection["NPlugItem_SVariant.EntityModel"]["size"]
        ent_stride = reflection["NPlugPrefab_SEntRef.Model"]["size"]
        g.require(variant_stride == 0x28 and ent_stride == 0x50, "Reflected prefab strides differ")
        anchors = bridge.call("DevGetPointers", {"listAnchoredObjects": True, "anchoredObjectsLimit": 4})
        g.require(anchors["anchoredObjectsCount"] == roots["map"]["nbItems"] <= 4,
                  "Owned map anchor inventory count differs")
        anchor_entry = anchors["anchoredObjects"][args.anchor]
        g.require(anchor_entry["label"] == f"anchoredObjects[{args.anchor}]", "Anchor order differs")
        anchor = g.pointer(anchor_entry["ptr"])
        nodes = [node_class(bridge, disk, roots["base"], anchor, 0x03101000)]
        item = bridge.ptr(anchor, fields["CGameCtnAnchoredObject.ItemModel"])
        nodes.append(node_class(bridge, disk, roots["base"], item, 0x2E002000))
        variant_list = bridge.ptr(item, fields["CGameItemModel.EntityModel"])
        nodes.append(node_class(bridge, disk, roots["base"], variant_list, 0x2F0BC000))
        variants = bridge.descriptor(variant_list, fields["NPlugItem_SVariantList.Variants"], 1)
        g.require(variants["count"] == 1, "Expected exactly one fixture variant")
        prefab = bridge.ptr(variants["data"], fields["NPlugItem_SVariant.EntityModel"])
        nodes.append(node_class(bridge, disk, roots["base"], prefab, 0x09145000))
        ents = bridge.descriptor(prefab, fields["CPlugPrefab.Ents"], args.entities)
        g.require(ents["count"] == args.entities, "Prefab entity count differs from fixture")
        raw_ents = bridge.read(ents["data"], 0, ents["count"]*ent_stride)
        g.dump(directory/"graph.json", {"nodes": nodes, "fields": fields, "reflection": reflection,
                                       "variantDescriptor": variants, "entityDescriptor": ents,
                                       "entityBytes": raw_ents.hex()})
        roots["hmsInstances"] = g.one_component({"components": roots["components"]}, "NHmsMgrInstDyna_SMgr", 0x2F079000)
        g.require(bridge.ptr(roots["zone"], 0x248) == roots["hmsInstances"], "Native/semantic HMS manager mismatch")
        snapshot = hms.snapshot(bridge, roots)
        rows = []
        for index in range(ents["count"]):
            model = g.pointer(g.qword(raw_ents, index*ent_stride+fields["NPlugPrefab_SEntRef.Model"]))
            identity = node_class(bridge, disk, roots["base"], model)
            row = {"entityIndex": index, "model": identity}
            if identity["classId"] == 0x09144000:
                mesh = bridge.ptr(model, fields["CPlugDynaObjectModel.Mesh"])
                row["mesh"] = node_class(bridge, disk, roots["base"], mesh, 0x090BB000)
                row["hmsModelLinks"] = [m for m in snapshot["models"] if m["sourceSolid2"] == mesh]
                g.require(len(row["hmsModelLinks"]) == 1, "Prefab mesh does not identify exactly one HMS source model")
                slot = row["hmsModelLinks"][0]["index"]
                row["hmsInstances"] = [r for r in snapshot["instances"] if r["activeModel"] and r["modelIndex"] == slot]
                # Native Solid2 writer passes mesh+A8 to a counted 8-byte visual
                # pointer array serializer requiring base class 0x09006000.
                visuals = bridge.descriptor(mesh, 0xA8, 32)
                table = bridge.read(visuals["data"], 0, visuals["count"]*8) if visuals["count"] else b""
                row["visualDescriptor"] = visuals
                row["visuals"] = []
                for visual_index in range(visuals["count"]):
                    visual = g.pointer(g.qword(table, visual_index*8))
                    visual_id = node_class(bridge, disk, roots["base"], visual)
                    # The exact native VertexTween uploader uses +110 table and
                    # +118 count directly. Preserve those fields, not guessed cFrame.
                    detail = bridge.read(visual, 0x110, 12)
                    count = g.dword(detail, 8)
                    g.require(count <= 128, "Visual subframe count exceeds fixture bounds")
                    subtable = g.pointer(g.qword(detail), nullable=count == 0)
                    flags = g.dword(bridge.read(visual, 0x24, 4))
                    row["visuals"].append({"index": visual_index, "node": visual_id, "nativeSubvisualCount": count,
                                           "nativeSubvisualTable": subtable, "flags": flags, "flagsHex": hex(flags),
                                           "structuredBit": bool(flags & 0x08000000)})
                g.require(bridge.descriptor(mesh, 0xA8, 32) == visuals, "Solid2 visual descriptor changed")
                g.require(bridge.ptr(model, fields["CPlugDynaObjectModel.Mesh"]) == mesh, "Dyna mesh changed")
            rows.append(row)
            g.dump(directory/f"entity-{index:02}.json", row)
        g.require(bridge.descriptor(prefab, fields["CPlugPrefab.Ents"], args.entities) == ents, "Prefab entity descriptor changed")
        g.require(bridge.read(ents["data"], 0, len(raw_ents)) == raw_ents, "Prefab entity records changed")
        g.require(bridge.ptr(anchor, fields["CGameCtnAnchoredObject.ItemModel"]) == item, "Anchor model changed")
        g.identity(bridge, roots)
        g.dump(directory/"roots.json", roots)
        result = {"success": True, "requests": bridge.calls, "map": roots["map"], "anchorIndex": args.anchor,
                  "graphNodes": nodes, "fields": fields, "reflection": reflection, "entities": rows,
                  "limits": "Exact current anchor/prefab CPU source links. Actual GPU draw branch/binding and renderer clock upload remain separate."}
        g.dump(directory/"result.json", result)
        print(json.dumps({"success": True, "requests": bridge.calls,
                          "entities": [{"entityIndex": r["entityIndex"], "class": r["model"]["classIdHex"],
                                        "hmsSlots": [m["index"] for m in r.get("hmsModelLinks", [])],
                                        "visualCounts": [v["nativeSubvisualCount"] for v in r.get("visuals", [])]} for r in rows]}, indent=2))
    except Exception as error:
        g.dump(directory/"failure.json", {"success": False, "utc": g.utc(), "error": str(error), "requests": bridge.calls})
        raise
    finally:
        bridge.trace.close()


if __name__ == "__main__":
    main()
