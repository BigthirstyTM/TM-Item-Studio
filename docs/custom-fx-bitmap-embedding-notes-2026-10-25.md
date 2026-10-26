# Native bitmap pixels inside an FX item

Date: 2026-10-25. This investigation is offline; the root agent owns native
preload, placement, rendering, save and reopen checks.

## Result

There is a concrete native serialization route for a bitmap with its pixels
inside the enclosing GBX graph:

```text
CPlugParticleGpuModel
  -> inline CPlugBitmap (0x09011000)
     -> inline CPlugFileGen (0x0902F000), GenKind Pixels = 9
        -> raw pixel bytes
```

Actual game assets already use this graph, with zero external references.
The corrected custom Sparkler and Foggers now pass native preload, placement
and map save. The saved map's embedded items retain the actual decoded texture
pixels byte-for-byte and have zero external references. Visible particle
playback remains a separate, unresolved check; the editor control also showed
no particles in the latest root-agent test.

## Why copying the original wrapper is insufficient

The visually proven source Sparkler item is:

`Documents/Trackmania/Items/ChatGPT/PackedOnlyV2/TestItem_Sparkler_BitmapRefs_v3.Item.Gbx`

Its two external bitmap wrappers are `Sparkler_D.Texture.gbx` and
`SparklerEnd_D.Texture.gbx`. Each is 358 bytes and contains an external
`Image/<name>.dds` reference. Embedding those wrapper bytes alone leaves the
image dependency external. An empty item reference table therefore does not,
by itself, prove that the texture pixels are present.

The game-extracted Sparkler, SparklerEnd and FoggerSmoke DDS images are all
512 x 512 DXT5 textures. Pillow decodes them as RGBA with white RGB and varying
alpha. The helper preserves those decoded texels; it does not replace the
particle image with a solid rectangle or generated placeholder.

## Native examples checked

Paths below are relative to `C:/Users/PC/OpenplanetNext/Extract/GameData`.
All five files have zero external references and an inline Image node.

| Native wrapper | Parameters | Raw pixels |
| --- | --- | ---: |
| `LightMap/Media/Texture/SpritePosRadius.Texture.Gbx` | 4, 4, 1, 4, 8, 1 | 256 bytes |
| `LightMap/Media/Texture/ProbeBBoxInvHDiag.Texture.Gbx` | 4, 4, 1, 4, 7, 1 | 128 bytes |
| `Techno3/Media/Texture/LightPosIRad.Texture.Gbx` | 1, 1, 1, 4, 8, 1 | 16 bytes |
| `Techno3/Media/Texture/LightData16b.Texture.Gbx` | 2, 2, 2, 4, 7, 1 | 64 bytes |
| `Effects/Media/Texture/HeightField.Texture.Gbx` | 16, 16, 1, 1, 6, 1 | 512 bytes |

The parameters correspond to width, height, depth, component count, image
format and mip count. The first five values and payload lengths agree in all
examples. Each example uses one mip. Current `Openplanet.h` names image format
8 `RGBA32F`, 7 `RGBA16F`, and 6 `RGBA16`. The native class enum names GenKind 9
`Pixels`; the class exists in the current API metadata with size 248.

The final pixel byte is followed immediately by the original bitmap settings.
For these wrappers that is 28 bytes followed by bitmap chunk `0x09011032`.
The helper checks this boundary, rather than searching for a terminator inside
the pixel payload.

## Exact bounded archive used

Offsets are relative to the inline Image class ID, little endian:

| Offset | Bytes | Value |
| ---: | ---: | --- |
| 0 | 4 | Class `0x0902F000` |
| 4 | 4 | Version `0x80000006` |
| 8 | 4 | GenKind `9` (Pixels) |
| 12 | 4 | Integer parameter count `6` |
| 16 | 24 | `[width, height, 1, 4, 8, 1]` for the RGBA32F candidate |
| 40 | 20 | Five zero 32-bit fields, matching the empty native configuration |
| 60 | width x height x 16 | RGBA32F pixel bytes |

The five zero fields follow the parameter arrays. GBX.NET documents part of
them as empty Vec4 and float arrays, a string and function-key node array.
The complete extra version-6 semantics are not established here. The writer
retains the known empty configuration; it is not a general ImageGen editor.

`CPlugFileGen` overrides normal chunk serialization: there is no additional
`FACADE01` terminator between its raw pixels and the enclosing bitmap fields.
The enclosing bitmap keeps its own terminator.

## Implementation and verification

[`Tests/FxStandalone/inline_bitmap.py`](../Tests/FxStandalone/inline_bitmap.py)
provides:

```python
inline_bitmap(wrapper_bytes, dds_bytes, image_node_index)  # class + bitmap body
build_inline_bitmap(wrapper_path, dds_path, image_node_index)
validate_native_pixel_examples(extract_root)
```

The helper reads the wrapper with the bounded GBX container reader, requires
the original uncompressed two-node/one-DDS wrapper layout and Image chunk
`0x09011030` version 5, and substitutes only that Image node reference with a
FileGen node. Every surrounding bitmap configuration byte is preserved. The
enclosing item writer must remove the old external reference and allocate the
new image index in its node count.

Validation completed:

- The five native example payload boundaries and zero-ref tables pass.
- Both Sparkler textures and FoggerSmoke convert successfully from their real
  DDS files to inline RGBA32F image nodes.
- Each 512 x 512 conversion has 4,194,304 pixel bytes and produces a 4,194,646
  byte inline bitmap node. GBX body compression is the enclosing writer's job.

The final Sparkler builder reindexes the graph sequentially. It has 25 node
slots, bitmap indices 14 and 21 and their Image nodes 15 and 22. The original
Sparkler emitter models are retained. Fogger uses 17 slots, bitmap 13 and Image
14. Inline bitmap chunk 0x09011036 must inherit the enclosing lookback-string
context: the standalone bitmap's extra version-3 word is removed. Keeping that
word was an early malformed-candidate error.

The alternative GPU-model FID-string branch can point at built-in
`GameData/Stadium/Media/Texture/Sparkler_D.Texture.gbx` and `SparklerEnd_D.Texture.gbx`.
It needs no additional user files but relies on installed game assets. The
inline Pixels candidate is the stronger test for actual embedded image data.

## Source limitations

GBX.NET 2.4.4 `CPlugBitmap` describes Image as a node reference. Its
`CPlugFileGen` reader recognizes that Pixels has appended raw image data but
throws `NotImplementedException`; it also supports only versions through 5
and GenKind through 50. The current native corpus uses version 6 and kinds
51-53 as well. This task therefore uses a narrow native-byte writer rather
than claiming the generic library supports these images.

The first EmbeddedPixels candidate caused a native preload failure and process
exit. That historical file is not the corrected sequential/lookback fixture.
After the lookback fix, the root agent observed successful native preload,
placement and map save of the corrected full-pixel items.

The independent saved-file audits are
[`saved-map-embedded-audit.json`](../artifacts/fx-clones/fx-sequential-bitmap-20261025/saved-map-embedded-audit.json)
and [`saved-map-emitter-audit.json`](../artifacts/fx-clones/fx-sequential-bitmap-20261025/saved-map-emitter-audit.json).
They verify both Sparkler images and the FoggerSmoke image as 512 x 512
RGBA32F payloads with the original decoded texels, zero external references,
and preservation of Sparkler's edited physical item-space emission height
Y=3.5 after native map serialization.

The remaining gate is observed particle playback and clean native reopening
of the final delivered identities. Neither successful preload nor retained
image bytes proves visible sparks or smoke. `inline_bitmap.py` remains a
bounded research helper under Tests; Studio exports preserve the source image
bytes and apply only validated metadata and native emission-point splices.
