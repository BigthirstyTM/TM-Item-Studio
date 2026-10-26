"""Generate embedded FX items with a native emitter offset and a custom mesh.

This is an offline probe. It uses the gbx-py schema from the NICE importer
because GBX.NET currently exposes CPlugFxSystem as an unknown class. Keep the
output under a dedicated artifact directory and run native tests separately.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path


NICE_ROOT = Path(
    r"C:\Users\PC\Downloads\Blender Addons\blendermania-addon\blendermania-addon\NICE"
)
DEFAULT_SOURCE = Path(
    r"C:\Users\PC\Documents\Trackmania\Items\ShadocAlex Items\FogVolumetric2\FogVolumetric1_FogEmissionRate_0.Item.Gbx"
)
DEFAULT_SHELL_ROOT = Path("artifacts/fx-clones/fx-mesh-shells")


def load_parser():
    if not NICE_ROOT.exists():
        raise RuntimeError(
            f"NICE schema not found at {NICE_ROOT}; install gbx-py or pass --nice-root."
        )
    sys.path.insert(0, str(NICE_ROOT))
    try:
        from src.parser import generate_file, parse_file
        from src.gbx_structs import loop_noderefs
    except Exception as error:  # pragma: no cover - environment diagnostic
        raise RuntimeError("Could not import gbx-py NICE parser; install construct and gbx-py") from error
    return parse_file, generate_file, loop_noderefs


def body_key(body, class_id):
    # Construct's containers preserve integer chunk IDs, while some generated
    # versions expose them as strings. Accept both without weakening checks.
    for key in (class_id, f"0x{class_id:08x}", f"0x{class_id:x}"):
        if key in body:
            return key
    raise KeyError(f"missing chunk 0x{class_id:08X}")


def find_prefab(item):
    item_body = item.body[body_key(item.body, 0x2E002019)]
    model = item_body["EntityModel"]
    if model.get("classId") != 0x09145000:
        raise ValueError(f"expected embedded CPlugPrefab, got {model.get('classId')!r}")
    prefab = model["body"]
    if prefab.get("EntsCount", 0) < 2 or len(prefab.get("Ents", [])) < 2:
        raise ValueError("FX carrier needs a static entity and an FX entity")
    return item_body, prefab


def find_fx(prefab):
    for index, entry in enumerate(prefab["Ents"]):
        model = entry.get("model")
        if not model:
            continue
        model_body = model.get("body")
        if not model_body:
            continue
        fx_key = next((key for key in model_body if key in (0x0915C000, "0x0915c000", "0x915c000")), None)
        if fx_key is not None:
            fx = model_body[fx_key]
            root = fx.get("rootNode", {})
            root_node = root.get("node", {})
            children = root_node.get("Children", [])
            if not children:
                raise ValueError("CPlugFxSystem has no root particle child")
            # NICE's adapter flattens a CPlugFxSystemNode into its selected
            # body, so a ParticleEmitter child has LocalOffsetExpr directly;
            # older gbx-py revisions retain a `{type,node}` wrapper.
            child = children[0]
            emitter = child.get("node", child)
            if "LocalOffsetExpr" not in emitter or "Model" not in emitter:
                raise ValueError("first FX child is not a ParticleEmitter")
            return index, fx, emitter
    raise ValueError("embedded CPlugFxSystem (0x0915C000) was not found")


def find_shell_mesh(shell, parse_file):
    body = shell.body[body_key(shell.body, 0x2E002019)]
    model = body["EntityModel"]
    if model.get("classId") != 0x09145000:
        raise ValueError("mesh shell has no embedded CPlugPrefab")
    prefab = model["body"]
    if not prefab.get("Ents"):
        raise ValueError("mesh shell prefab has no entities")
    model = prefab["Ents"][0]["model"]
    if model.get("classId") != 0x09159000:
        raise ValueError("mesh shell first entity is not CPlugStaticObjectModel")
    static_body = model["body"]
    mesh = static_body.get("Mesh")
    if not mesh or mesh.get("classId") != 0x090BB000:
        raise ValueError("mesh shell has no typed CPlugSolid2Model mesh")
    return mesh


def update_identity(document, item_id, display_name):
    header = document.header.data[0]
    header.meta.id = item_id
    header.meta.author = "TM_Item_Studio"
    header.fileName = item_id
    body = document.body
    body[body_key(body, 0x2E00100B)].author.author = "TM_Item_Studio"
    body[body_key(body, 0x2E00100C)].name = display_name
    body[body_key(body, 0x2E00100D)].description = (
        "Native embedded CPlugFxSystem carrier with editable emitter offset and custom mesh"
    )


def generate_one(parse_file, generate_file, loop_noderefs, source_path, shell_path, output_path, item_id, name, offset, fog_rate):
    document = parse_file(str(source_path), with_nodes=False, recursive=False)
    shell = parse_file(str(shell_path), with_nodes=False, recursive=False)
    # gbx-py normally discovers node count while building. Embedded FX graphs
    # contain repeated references to the same node index, including opaque
    # children that gbx-py cannot type. Seed the original node table explicitly
    # so replacing the mesh does not collapse the native node count/ref table.
    node_count = int(document.numNodes)
    nodes = [None] * node_count
    for node in loop_noderefs(document.body):
        index = getattr(node, "_index", -1)
        if 0 <= index < node_count and nodes[index] is None:
            nodes[index] = node
    document.nodes = nodes
    item_body, prefab = find_prefab(document)
    _, fx, emitter = find_fx(prefab)
    mesh = find_shell_mesh(shell, parse_file)
    static_model = prefab["Ents"][0]["model"]
    static_model["body"]["Mesh"]["body"] = mesh["body"]

    expression = f"float3({offset[0]:g},{offset[1]:g},{offset[2]:g})"
    emitter["LocalOffsetExpr"] = expression
    emitter["WorldOffsetExpr"] = "float3(0,0,0)"
    # The FogVolumetric fixture has one GPU fog submodel. Only touch this
    # field when requested; all other particle bytes remain source-exact.
    if fog_rate is not None:
        model = emitter["Model"]
        particle_body = model["body"]
        submodels = particle_body[body_key(particle_body, 0x090B3000)]["ParticleEmitterSubModels"]
        gpu = submodels[0]["body"][body_key(submodels[0]["body"], 0x090B203A)]
        gpu_model = gpu["particleGpuModel"]
        gpu_body = gpu_model["body"]
        gpu_chunk = gpu_body[body_key(gpu_body, 0x090C6002)]
        if "FogEmissionRate" not in gpu_chunk:
            raise ValueError("expected FogEmissionRate field in CPlugParticleGpuModel")
        gpu_chunk["FogEmissionRate"] = float(fog_rate)

    update_identity(document, item_id, name)
    output = generate_file(document, remove_external=False, reindex_nodes=False)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(output)

    reopened = parse_file(str(output_path), with_nodes=False, recursive=False)
    _, reopened_prefab = find_prefab(reopened)
    _, _, reopened_emitter = find_fx(reopened_prefab)
    if reopened_emitter["LocalOffsetExpr"] != expression:
        raise ValueError("native LocalOffsetExpr did not survive gbx-py round-trip")
    if reopened_prefab["Ents"][0]["model"]["body"]["Mesh"].get("classId") != 0x090BB000:
        raise ValueError("typed custom mesh did not survive gbx-py round-trip")

    return {
        "itemId": item_id,
        "name": name,
        "mesh": shell_path.name,
        "source": str(source_path.resolve()),
        "output": str(output_path.resolve()),
        "bytes": len(output),
        "sha256": hashlib.sha256(output).hexdigest().upper(),
        "nativeFields": {
            "fxClassId": "0x0915C000",
            "particleEmitterClassId": "0x2F0C4000",
            "localOffsetExpr": expression,
            "fogEmissionRate": fog_rate,
            "nativeOffsetWritten": True,
            "meshClassId": "0x090BB000",
        },
        "offlineRoundTrip": "gbx-py parse/build/reparse passed",
        "nativeRuntimeVerdict": "pending Fids preload and placement test",
    }


def main():
    global NICE_ROOT
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--shell-root", type=Path, default=DEFAULT_SHELL_ROOT)
    parser.add_argument("--nice-root", type=Path, default=NICE_ROOT)
    parser.add_argument("--offset", nargs=3, type=float, default=(0.0, 1.5, 0.0), metavar=("X", "Y", "Z"))
    parser.add_argument("--fog-rate", type=float, default=0.0)
    args = parser.parse_args()
    NICE_ROOT = args.nice_root
    parse_file, generate_file, loop_noderefs = load_parser()
    source = args.source.resolve()
    shell_root = args.shell_root.resolve()
    if not source.exists():
        raise SystemExit(f"source does not exist: {source}")
    rows = []
    # Both candidates derive from FogVolumetric. A diamond mesh does not turn
    # its smoke emitter into a Sparkler; real Sparkler probes live separately.
    specs = [("Pyramid", "Fogger", "TMIS_Fogger_NativePyramid"), ("Diamond", "Fogger", "TMIS_Fogger_NativeDiamond")]
    for shape, label, item_id in specs:
        shell = shell_root / f"TMIS_FX_{shape}Shell.Item.Gbx"
        if not shell.exists():
            raise SystemExit(f"mesh shell does not exist: {shell}")
        output = args.output / f"{item_id}.Item.Gbx"
        rows.append(generate_one(parse_file, generate_file, loop_noderefs, source, shell, output, item_id, f"TMIS {label} native {shape}", args.offset, args.fog_rate))
    manifest = {"schemaVersion": 1, "generator": "Tests/FxNativePatch/generate.py", "rows": rows}
    (args.output / "fx-native-patched-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()

