# Real Sparkler texture probes, 25 October 2026

The reference is the actual Sparkler item embedded in the supplied map,
`TestItem_Sparkler_BitmapRefs_v3.Item.Gbx`, SHA-256
`AE59D3DBA5FEBEB909E9D48C833A75CF1F7B06DCCA861A9924F6827DC32BF851`.
Its original particles were observed in the game with the two supplied local
bitmap/DDS dependencies. This is distinct from the earlier FogVolumetric mesh
probe, whose Diamond variant was incorrectly called Sparkler.

All generators are offline under [`Tests/FxStandalone`](../Tests/FxStandalone/README.md).
They decompress before editing and preserve opaque FX bytes. They do not use
literal ASCII offsets in compressed LZO input. Every generated candidate has a
manifest recording exact hashes and dependency semantics.

| Candidate | File bytes | Actual change | Native evidence |
|---|---:|---|---|
| `TMIS_Sparkler_BuiltinTextureProbe` | 2550 | GPU primary texture refs become `isTextureFid=true` with forward-slash builtin paths; external refs removed | Cold-session placement and native map save succeeded; no sparks observed. Saved item has null primary bitmap refs. |
| `TMIS_Sparkler_BuiltinBackslashProbe` | 2547 | Same native string branch using backslashes | Read-only native inspection also found null primary bitmap bindings before save. |
| `TMIS_Sparkler_EmbeddedPixelsProbe` | 343828 | Original bitmap nodes21/22 inline, full original image texels as FileGen Pixels RGBA32F nodes23/24, no external refs/FIDs | Native preload was in flight when the process exited. A placement operation also completed in that frame. Cause unproven; quarantined, no retry/promotion. |
| `TMIS_Sparkler_TinyNativeBitmapControl` | 2693 | Exact native1x1 `LightPosIRad` bitmap/image payload copied twice; only Image node indexes change | Process exited during preload with no concurrent placement. Quarantined; no deliverable claim. |

Builtin paths were confirmed present in the game's FID tree:

```
GameData\Stadium\Media\Texture\Sparkler_D.Texture.gbx
GameData\Stadium\Media\Texture\SparklerEnd_D.Texture.gbx
```

Presence in the FID tree does not prove the particle archive's string resolver
accepts a particular path syntax. The native saved forward-slash item is
6868 bytes (body6631, node count21, no external refs). At decompressed GPU chunk
offsets3868 and5742 it serializes version22, `isTextureFid=0`, `textureNodeRef=-1`
for both primary textures. Together with the visible absence of sparks this
supports a missing texture binding; direct read-only native GPU+0x70 inspection
can distinguish load-time resolution from save-time clearing.

The inline Pixels archive layout matches five native zero-reference bitmap
files in the game extraction. This validates the byte boundaries, not the
runtime compatibility of converted512x512 RGBA32F pixels with the original
Sparkler bitmap flags. The tiny exact-native control isolates archive/node
registration from image conversion, size and bitmap flags.

The tiny exact-native control also failed during preload, so image conversion,
512x512 dimensions and original Sparkler bitmap flags cannot be the sole cause.
The next bounded hypothesis is native node-index ordering: external indexes21
and22 were reserved by the original ref table, but after inlining/removing that
table they appear before previously internal indexes14 through20. An engine
reader may require sequential first-use indexes. A strict index-remap fixture
must prove all reference offsets and preserve the opaque graph before any new
native test. This is a hypothesis, not a confirmed crash cause.

The sequential-index tiny control also exited. A separate serialized-layout
defect is now confirmed from `CPlugBitmap.chunkl` and native bytes: standalone
bitmap chunk `0x09011036` writes its first ID as
`version1, U01NodeRef=-1, lookbackVersion3, U02Id=-1, U06NodeRef=-1`.
The enclosing item initialized its lookback context before this node. Copying
the standalone version3 word inline makes the engine interpret it as U02Id and
leaves a trailing `-1` where the next chunk ID belongs. The corrected helper
removes exactly that4-byte version word under a full chunk036/037 signature
guard. Both ID and node refs are null, so the bitmap introduces no new string
table entries or extra reference relocations. FileGen archive version6 is a
different field and remains intact.

The corrected full-pixel candidate is
`fx-sequential-bitmap-20261025/TMIS_Sparkler_SequentialPixelsLookbackFixed.Item.Gbx`
(344462 bytes, SHA-256
`FDA42E7DC13285D630F7564DB228F70C7DE1C46A27BE0951F55A8F5C0C81EA7B`).
It applies both fixes, retains the original bitmap flags/pixels, and records an
exact inverse of its nine NodeRef edits plus two inline node insertions.

Both corrected fixtures now passed native preload with the game remaining
alive. Read-only inspection reports `bitmapNull=false` and empty `bitmapFid`
for both emitters, proving native inline bitmap bindings rather than fallback
game texture FIDs. The original local expressions remain `float3(0)` and
`float3(0,0,10)`. Evidence:

- [`tiny-native-preload-success.json`](../artifacts/fx-clones/fx-sequential-bitmap-20261025/tiny-native-preload-success.json)
- [`fullpixels-native-preload-success.json`](../artifacts/fx-clones/fx-sequential-bitmap-20261025/fullpixels-native-preload-success.json)

This supports the corrected bitmap string-context layout and full RGBA32F
image archive at the native-load level.

The first native map save also succeeded without an external-file embedding
dialog. The map is
`C:\Users\PC\Documents\Trackmania\Maps\MCP\TMIS_StandaloneFX_InlinePixels_20261025.Map.Gbx`
(569647 bytes). It contains the edited Sparkler and both Volume/Sprite Fogger
controls. A read-only extraction using `Tests/MapInspect` found three embedded
items with zero external references. The Sparkler's two images and each
Fogger's one image retain every original decoded DDS texel exactly: each is
512×512×1 RGBA32F, one mip, 4194304 pixel bytes. Both bitmap ID chunks in the
Sparkler, and the single chunk in each Fogger, retain the corrected initialized
parent lookback context. Evidence:

- [`saved-map-embedded-audit.json`](../artifacts/fx-clones/fx-sequential-bitmap-20261025/saved-map-embedded-audit.json)
- [`saved-map-emitter-audit.json`](../artifacts/fx-clones/fx-sequential-bitmap-20261025/saved-map-emitter-audit.json)

The saved Sparkler also retains its edited item-space emission point at
`(0,3.5,0)` and the second emitter ten metres higher. The original rotated FX
owner is unchanged. Both Foggers retain identity owners and their original
zero offset. This establishes native save retention of the inline pixels and
edited expression; it does not establish particle rendering. The first editor
screenshots did not show sparks or smoke. Visible particles after placement,
play and reopening remain acceptance gates.

A read-only corpus scan inspected45752 GBX files (20136 relevant root classes)
and found195 plausible GPU-model chunks. Of95 string branches,78 were empty;
all17 nonempty strings came from these newly generated probes. There is no
nonempty vanilla string-path precedent in the scanned extraction. The scan
evidence is `artifacts/fx-clones/fx-standalone-20261025/native-texture-fid-corpus.json`.

The next acceptance gates are actual visible particles on the custom meshes,
native emitter-point visibility and native map reopening with the particles
still visible. None of these candidates is currently a proven standalone
custom Sparkler deliverable solely from the evidence above.

An additional bounded configuration test now isolates the wrapper settings.
The original DDS bitmap chunk030 U01 triple is
`(0x002C0000,0x10004E12,0x08003692)` and Flags2 is `0x48000800`. Native float
Pixels wrappers use `(0x0026000E,0x10000E00,0x08000092)` and `0x08000800`.
The low byte14 agrees with the ColorFloat enum; the complete packed meaning
and extra Flags2 bit remain unconfirmed. Four controls change the low byte,
only Flags2, only the complete U01 triple, or both native configuration
records. All image, FX, mesh and identity bytes remain exactly restorable.
Controls and audits are under `fx-float-bitmap-controls-20261025`.

The exact tiny native bitmap has transparent zero pixels. A separate
`TMIS_Sparkler_WhiteTinyNativeConfig.Item.Gbx` changes only those two 1×1 texels
to opaque white while retaining the native float wrapper. This avoids using a
deliberately invisible parser control as particle-rendering evidence. Native
rendering results for these controls are pending.

The six-case native FID-string resolver matrix found a concrete accepted path:
`Stadium\Media\Texture\Sparkler_D.Texture.gbx`, with the matching
`SparklerEnd_D.Texture.gbx` path. Only the `StadiumBackslash` candidate binds
both stock bitmap nodes; bare filename, `Texture/`, `Media/Texture/`,
`Stadium/Media/Texture/` and `Media\Texture\` do not. The earlier full
`GameData` forward/backslash paths also did not bind. Exact native reports and
the matrix summary are under
`artifacts/fx-clones/fx-fid-path-matrix-20261025/`. Binding is a separate gate
from visible particles and native save/reopen persistence.

The equivalent positive-rate Fogger control uses
`Stadium\Media\Texture\FoggerSmoke.Texture.gbx`. Its original
`IsFogEmitter=true`, rate100, mesh, expressions and identity remain intact.
A separate full-path-identity control changes the existing header/body Id
definition text to its own stage path, retaining the original definition slots
and author. Both controls and their exact inverse/independent metadata audits
are under `fx-resolved-fogger-20261025`. These use the standard game's bitmap
and image assets and require no extra user texture files. Their exact native
rendering and persistence verdicts must be recorded separately.
