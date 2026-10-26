"""Inline a DDS-backed particle bitmap as native CPlugFileGen Pixels.

This is a bounded writer for the pinned, uncompressed native bitmap wrappers.
It substitutes the external Image and removes the standalone lookback version
word from the first bitmap ID field, because the enclosing item already owns
an initialized lookback-string context. It returns class ID plus body.
Runtime loading and particle rendering must be verified separately.
"""

from __future__ import annotations

from array import array
from io import BytesIO
from pathlib import Path
import struct
import sys

from PIL import Image

from gbx_container import GbxContainer


BITMAP_CLASS = 0x09011000
FILEGEN_CLASS = 0x0902F000
BITMAP_IMAGE_CHUNK = 0x09011030
NEXT_BITMAP_CHUNK = 0x09011032
FILEGEN_VERSION = 0x80000006
PIXELS_KIND = 9
RGBA32F = 8


def bitmap_body_for_parent_context(body: bytes) -> bytes:
    """Remove only the verified first-ID version in a standalone bitmap body.

    Chunk036 v1 contains U01NodeRef(null), U02Id(null), U06NodeRef(null).
    A standalone file writes lookback version3 before U02Id. An inline node
    shares the enclosing item's context and must omit that version word.
    Both ID and nod refs are null, so there are no string dictionary entries
    or additional node indexes to relocate in this exact configuration.
    """
    signature = struct.pack("<6I", 0x09011036, 1, 0xFFFFFFFF, 3,
                            0xFFFFFFFF, 0xFFFFFFFF)
    if body.count(signature) != 1:
        raise ValueError("bitmap lacks the verified standalone chunk036 first-ID layout")
    chunk = body.index(signature)
    if body[chunk + len(signature):chunk + len(signature) + 4] != struct.pack("<I", 0x09011037):
        raise ValueError("bitmap chunk036 first-ID signature does not end at chunk037")
    version_offset = chunk + 12
    result = body[:version_offset] + body[version_offset + 4:]
    if result[:version_offset] + struct.pack("<I", 3) + result[version_offset:] != body:
        raise ValueError("bitmap lookback version removal changed other bytes")
    return result


def filegen_pixels_rgba32f(dds_bytes: bytes) -> tuple[bytes, dict]:
    """Build class ID + native Pixels archive, using decoded original texels."""
    with Image.open(BytesIO(dds_bytes)) as source:
        if source.format != "DDS":
            raise ValueError("expected a DDS particle image")
        rgba = source.convert("RGBA")
        width, height = rgba.size
        if not 0 < width <= 4096 or not 0 < height <= 4096:
            raise ValueError("DDS dimensions exceed the bounded particle scope")
        floats = array("f", (component / 255.0 for component in rgba.tobytes()))
    if floats.itemsize != 4:
        raise ValueError("host float array is not IEEE float32")
    if sys.byteorder != "little":
        floats.byteswap()
    pixels = floats.tobytes()
    if len(pixels) != width * height * 4 * 4:
        raise ValueError("incorrect RGBA32F pixel payload length")
    # Native examples have six integer parameters and five zero fields between
    # them and pixel data. The complete extra v6-field semantics are unknown;
    # preserving this observed empty configuration avoids inventing a schema.
    archive = struct.pack("<4I6I5I", FILEGEN_CLASS, FILEGEN_VERSION,
                          PIXELS_KIND, 6, width, height, 1, 4, RGBA32F, 1,
                          0, 0, 0, 0, 0) + pixels
    return archive, {"width": width, "height": height, "depth": 1,
                     "components": 4, "format": "RGBA32F", "mipCount": 1,
                     "pixelBytes": len(pixels), "imageArchiveBytes": len(archive)}


def inline_bitmap(wrapper_bytes: bytes, dds_bytes: bytes,
                  image_node_index: int) -> bytes:
    """Return inline CPlugBitmap class+body; allocate Image index in the item."""
    if not 0 < image_node_index < 10_000_000:
        raise ValueError("Image node index must be a positive allocated index")
    if struct.unpack_from("<I", wrapper_bytes, 9)[0] != BITMAP_CLASS:
        raise ValueError("wrapper is not a CPlugBitmap")
    container = GbxContainer.read(wrapper_bytes)
    if container.compression != "U" or container.node_count != 2:
        raise ValueError("expected the native uncompressed two-node wrapper")
    if len(container.references) != 1:
        raise ValueError("expected exactly one external DDS image reference")
    reference = container.references[0]
    if reference["nodeIndex"] != 1 or not reference["path"].lower().endswith(".dds"):
        raise ValueError("unexpected bitmap image dependency")
    body = bitmap_body_for_parent_context(container.body)
    signature = struct.pack("<II", BITMAP_IMAGE_CHUNK, 5)
    if body.count(signature) != 1:
        raise ValueError("expected a unique native bitmap Image chunk version 5")
    chunk = body.index(signature)
    image_start = chunk + 8
    if struct.unpack_from("<I", body, image_start)[0] != 1:
        raise ValueError("Image chunk does not use the sole DDS reference")
    tail_start = image_start + 4
    if body[tail_start + 28:tail_start + 32] != struct.pack("<I", NEXT_BITMAP_CHUNK):
        raise ValueError("unexpected post-image bitmap layout; refusing opaque splice")
    if body[-4:] != struct.pack("<I", 0xFACADE01):
        raise ValueError("bitmap body lacks the native chunk terminator")
    image, _ = filegen_pixels_rgba32f(dds_bytes)
    patched = (body[:image_start] + struct.pack("<I", image_node_index)
               + image + body[tail_start:])
    # Configuration chunks, including packed format/filter flags, are retained;
    # only the standalone lookback version is omitted for the parent context.
    # The caller must remove the old external refs and allocate all inline node
    # indexes in strict first-use order in the enclosing item's node table.
    return struct.pack("<I", BITMAP_CLASS) + patched


def build_inline_bitmap(wrapper_path: Path | str, dds_path: Path | str,
                        image_node_index: int) -> bytes:
    return inline_bitmap(Path(wrapper_path).read_bytes(),
                         Path(dds_path).read_bytes(), image_node_index)


def validate_native_pixel_examples(extract_root: Path | str) -> list[dict]:
    """Confirm the byte-count rule against five actual, zero-ref game assets."""
    base = Path(extract_root) / "GameData"
    examples = (
        ("LightMap/Media/Texture/SpritePosRadius.Texture.Gbx", (4, 4, 1, 4, 8, 1)),
        ("LightMap/Media/Texture/ProbeBBoxInvHDiag.Texture.Gbx", (4, 4, 1, 4, 7, 1)),
        ("Techno3/Media/Texture/LightPosIRad.Texture.Gbx", (1, 1, 1, 4, 8, 1)),
        ("Techno3/Media/Texture/LightData16b.Texture.Gbx", (2, 2, 2, 4, 7, 1)),
        ("Effects/Media/Texture/HeightField.Texture.Gbx", (16, 16, 1, 1, 6, 1)),
    )
    evidence = []
    for relative, expected in examples:
        data = (base / relative).read_bytes()
        container = GbxContainer.read(data)
        if container.references:
            raise ValueError(f"native example has external refs: {relative}")
        body = container.body
        signature = struct.pack("<4I", FILEGEN_CLASS, FILEGEN_VERSION, PIXELS_KIND, 6)
        if body.count(signature) != 1:
            raise ValueError(f"unexpected native FileGen signature: {relative}")
        start = body.index(signature)
        parameters = struct.unpack_from("<6I", body, start + 16)
        if parameters != expected or body[start + 40:start + 60] != bytes(20):
            raise ValueError(f"native FileGen parameters changed: {relative}")
        width, height, depth, components, image_format, mip_count = parameters
        component_bytes = 4 if image_format == RGBA32F else 2
        pixel_bytes = width * height * depth * components * component_bytes
        end = start + 60 + pixel_bytes
        if body[end + 28:end + 32] != struct.pack("<I", NEXT_BITMAP_CHUNK):
            raise ValueError(f"native raw image boundary mismatch: {relative}")
        evidence.append({"path": str(base / relative), "fileBytes": len(data),
                         "parameters": list(parameters), "pixelBytes": pixel_bytes,
                         "externalReferenceCount": 0, "boundaryVerified": True})
    return evidence
