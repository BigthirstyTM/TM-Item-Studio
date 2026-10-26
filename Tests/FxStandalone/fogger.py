"""Bounded real-Fogger bitmap-binding probe; no native or live-file actions.

The native Save-As Fogger has a real positive volumetric emission rate but a
null primary bitmap. Bind the stock FoggerSmoke bitmap through the engine's
documented FID-string branch. Preserve the entire remaining native FX graph.
This uses stock game assets and is not a self-contained pixel-embedded item.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import sys

from gbx_container import GbxContainer

NICE = Path(r"C:\Users\PC\Downloads\Blender Addons\blendermania-addon\blendermania-addon\NICE")
sys.path.insert(0, str(NICE))
from src.gbx_structs import body_chunks  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = ROOT / "artifacts/fx-clones/native-embedded-20261025/TMIS_FoggerEmbeddedNativeClone.Item.Gbx"
SOURCE_SHA256 = "DF0CD97CBF5EF7A0F106974D5D838EEB8DCDF9A93215CA8C7CEDBF0DA5585FDD"
SOURCE_ID = rb"TMIS_FX_NativeCopy\TMIS_FoggerEmbeddedNativeClone.Item.Gbx"
TARGET_ID = rb"TMIS_FX_MeshProofs\TMIS_FoggerBuiltinSmokeProbe01.Item.Gbx"
SPRITE_ID = rb"TMIS_FX_MeshProofs\TMIS_FoggerBuiltinSpriteProbe1.Item.Gbx"
DEFAULT_TEXTURE = "GameData/Stadium/Media/Texture/FoggerSmoke.Texture.gbx"


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def gpu_record(container):
    signature = struct.pack("<II", 0x090C6002, 22)
    if container.body.count(signature) != 1:
        raise ValueError("expected one Fogger GPU model chunk version 22")
    offset = container.body.index(signature)
    stream = io.BytesIO(container.body)
    stream.seek(offset + 4)
    typed = body_chunks[0x090C6002].parse_stream(
        stream, gbx_data={}, nodes=[None] * container.node_count,
        load_external_file=lambda _: None,
    )
    return offset, typed


def generate(source: Path, output: Path, texture: str, sprites: bool = False):
    data = source.read_bytes()
    if sha(data) != SOURCE_SHA256:
        raise ValueError("native Fogger source differs from the pinned Save-As fixture")
    container = GbxContainer.read(data)
    if container.references or container.node_count != 15:
        raise ValueError("expected the native 15-node zero-external-reference Fogger")
    target_id = SPRITE_ID if sprites else TARGET_ID
    if len(SOURCE_ID) != len(target_id) or data.count(SOURCE_ID) != 2:
        raise ValueError("native fixed-length header/body identities changed")
    offset, typed = gpu_record(container)
    if not typed.IsFogEmitter or typed.FogEmissionRate != 100.0:
        raise ValueError("source is not the positive-rate real volumetric Fogger")
    if typed.isTextureFid or typed.texture._index != -1:
        raise ValueError("source primary bitmap is not the expected null node")
    start = offset + 12
    old = struct.pack("<II", 0, 0xFFFFFFFF)
    if container.body[start:start + 8] != old:
        raise ValueError("primary bitmap branch does not match the isolated typed record")
    path = texture.encode("utf-8")
    replacement = struct.pack("<II", 1, len(path)) + path
    source_body = container.body
    flag_offset = None
    if sprites:
        # This unique, independently typed v22 tail is u24, IsFogEmitter,
        # FogEmissionRate and u27. Change the fog selector only; spawn/life,
        # texture, rate, physics, opacity and every curve stay unchanged.
        tail = struct.pack("<IIf3f", 1, 1, 100.0, 0.0, 0.0, 2.0)
        if source_body.count(tail) != 1:
            raise ValueError("typed positive Fogger tail is not unique")
        flag_offset = source_body.index(tail) + 4
        if not offset < flag_offset < offset + 192:
            raise ValueError("fog selector escaped the pinned GPU record")
        source_body = (source_body[:flag_offset] + struct.pack("<I", 0)
                       + source_body[flag_offset + 4:])
    body = source_body[:start] + replacement + source_body[start + 8:]
    # Same-width identities preserve the complete native header and all
    # lookback-string indexes. The texture splice is inside a non-skippable
    # chunk and is self-sized by the string length.
    prefix = container.prefix.replace(SOURCE_ID, target_id)
    body = body.replace(SOURCE_ID, target_id)
    patched = GbxContainer(prefix, container.body, container.compression,
                           container.node_count, container.references)
    result = patched.write(body)
    reopened = GbxContainer.read(result)
    new_offset, new_gpu = gpu_record(reopened)
    if new_offset != offset or not new_gpu.isTextureFid or new_gpu.texture != texture:
        raise ValueError("native bitmap FID string did not round-trip")
    if bool(new_gpu.IsFogEmitter) != (not sprites) or new_gpu.FogEmissionRate != 100.0:
        raise ValueError("positive emission settings or selected fog mode changed")
    restored_body = (body[:start] + old + body[start + len(replacement):]).replace(target_id, SOURCE_ID)
    if flag_offset is not None:
        restored_body = (restored_body[:flag_offset] + struct.pack("<I", 1)
                         + restored_body[flag_offset + 4:])
    if restored_body != container.body or prefix.replace(target_id, SOURCE_ID) != container.prefix:
        raise ValueError("unrelated native bytes changed")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    manifest = {
        "generator": "Tests/FxStandalone/fogger.py", "source": str(source.resolve()),
        "sourceSha256": sha(data), "output": str(output.resolve()),
        "outputSha256": sha(result), "outputBytes": len(result),
        "stageAt": "Items/" + target_id.decode().replace("\\", "/"),
        "bitmapFid": texture, "isTextureFid": True,
        "sourceBitmap": "null CPlugBitmap", "bitmapSourceBodyOffset": start,
        "isFogEmitter": bool(new_gpu.IsFogEmitter), "fogEmissionRate": new_gpu.FogEmissionRate,
        "modeProbe": "fog selector disabled to test GPU smoke sprites; native behavior unverified" if sprites else "original volumetric fog selector retained",
        "sourceFogSelectorOffset": flag_offset,
        "sourceNodeCount": container.node_count, "outputNodeCount": reopened.node_count,
        "externalReferenceTableCount": len(reopened.references),
        "actualDependencies": "requires immutable stock Trackmania FoggerSmoke bitmap; no user texture file",
        "opaqueGraphUnchangedOutsideTextureFieldIdentityAndOptionalFogSelector": True,
        "mesh": "original source mesh retained for isolated bitmap test",
        "nativeVerdict": "pending preload, native inventory registration, placement, visible smoke and save/reopen",
        "warning": "CPlugFileGen inline-pixel candidate crashed preload elsewhere; this candidate uses only stock FID resolution",
    }
    output.with_suffix(".manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--texture", default=DEFAULT_TEXTURE)
    parser.add_argument("--sprites", action="store_true", help="separate isolated probe: disable IsFogEmitter only")
    args = parser.parse_args()
    print(json.dumps(generate(args.source, args.output, args.texture, args.sprites), indent=2))
