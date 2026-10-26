# Real Sparkler texture-isolation probes

These offline probes preserve the actual, visually proven Sparkler graph from
the supplied map. They do not use FogVolumetric as a substitute for Sparkler.
Native preload, visible particles and native map save/reopen are separate gates.

`gbx_container.py` reads binary GBX6 headers/reference tables, decompresses LZO
bodies before any edits, and retains the original compression mode. It refuses
trailing compressed data and out-of-bounds sizes. A write is decompressed again
to verify the exact prefix and body.

`create_builtin_sparkler.py` replaces only the two GPU-model primary bitmap
references with the native `isTextureFid=true` branch. Its paths are the actual
native FIDs confirmed by the running game:

```
GameData/Stadium/Media/Texture/Sparkler_D.Texture.gbx
GameData/Stadium/Media/Texture/SparklerEnd_D.Texture.gbx
```

The unused external references are removed; all other original decompressed
bytes are exactly restorable. This candidate uses builtin game textures.

`create_embedded_sparkler.py` instead inlines both original `CPlugBitmap` wrappers
and all their original DDS texels. Its `CPlugFileGen Pixels` kind9/version6 layout
comes from five actual zero-reference native bitmap files. `inline_bitmap.py`
preserves bitmap settings and encodes decoded texels as native RGBA32F. It omits
the standalone lookback version word before chunk036's null ID because the
parent item already owns the initialized string context. It adds no new ID
entries, so later opaque lookback indexes retain their original meaning.
Recorded node indexes are reassigned in strict first-use order using
`reindex.py`, independently of the string-context correction.
The result has 25 nodes, no external references and no builtin texture FID
strings. FX settings and mesh remain unchanged.

```powershell
python Tests/FxStandalone/create_builtin_sparkler.py `
  --output artifacts/fx-clones/fx-standalone-20261025/TMIS_Sparkler_BuiltinTextureProbe.Item.Gbx

python Tests/FxStandalone/create_embedded_sparkler.py `
  --output artifacts/fx-clones/fx-standalone-20261025/TMIS_Sparkler_EmbeddedPixelsProbe.Item.Gbx `
  --validate-native-examples C:/Users/PC/OpenplanetNext/Extract
```

The exact source SHA-256 is pinned. Both generators refuse another source/layout
instead of guessing at unknown particle bytes. Each output has a manifest with
source/output hashes, body sizes, changed offsets, node counts and dependency
semantics. No script launches or mutates Trackmania.

The real source has two particle emitters. In its original decompressed body,
their LocalOffsetExpr length words are at 4170 (`float3(0)`) and 6044
(`float3(0,0,10)`). Each follows the emitter model's `FACADE01` terminator and
null JointName. The complete tails are ten strings (local/world offset,
velocity, spawn modifier, scale, ambient, up, direction, opacity, water), one
boolean, then hue and lightness strings. The second emitter retains a10m local
offset relative to the first. Inserting texture nodes shifts later body offsets;
these numeric offsets are source-fixture evidence, not generic write locations.

The native source's `CPlugFxSystem` has version 1 and SystemNodesVersion 10, with
a Parallel root and two ParticleEmitter children. Native texture insertion and
mesh mutation do not justify claims about additional editable settings until
each field is independently observed in the game.

The first full embedded-pixels candidate was being preloaded when the native
process exited; a placement operation had also just finished in that frame.
The first tiny exact-native bitmap control and its sequential-index variant
also exited during preload. Those historical outputs incorrectly retained the
standalone chunk036 lookback version inside the already initialized item
context. This is a confirmed serialized-layout defect; a corrected native test
must still establish whether it explains the process exits. These historical
files are quarantined and must not be presented as working items.

`create_native_bitmap_control.py` builds a tiny
audit control from an exact native 1x1 bitmap, changing only its Image node
index. That control deliberately changes the particle appearance and is not a
deliverable Sparkler; use `reindex.py --parent-lookback-initialized` for the
corrected tiny fixture. The current full-pixel generator already applies both
the node and lookback fixes.

The corrected tiny and full-pixel candidates passed native preload: both GPU
bitmap slots are non-null, their FIDs are empty (inline), and their original
emitter expressions remain intact. Read-only game reports are under
`artifacts/fx-clones/fx-sequential-bitmap-20261025/` as
`tiny-native-preload-success.json` and `fullpixels-native-preload-success.json`.
Native visible particle output and save/reopen persistence are separate gates.

The first native map save retains all three inline-pixel items with zero
external references. `audit_saved_items.py` independently decodes the original
DDS textures and compares their pixel hashes with every native-saved FileGen
Pixels payload. All four images match exactly. The separate isolated
`Tests/FxEmitterTransform` diagnostic accepts `--inspect <item>...` and proves
the saved Sparkler's requested item-space Y=3.5 and original FX owner survived.
Reports are `saved-map-embedded-audit.json` and `saved-map-emitter-audit.json`
in the same artifact directory. Particle visibility remains unproven.

`create_float_bitmap_controls.py` creates four config-only controls from the
exact corrected full-pixel fixture. Five native Pixels wrappers use different
bitmap configuration from the DDS wrappers: chunk030 U01 is
`(0x0026000E,0x10000E00,0x08000092)` instead of
`(0x002C0000,0x10004E12,0x08003692)`; Flags2 is `0x08000800` instead of
`0x48000800`. The low byte14 agrees with the ColorFloat enum, but the complete
U01 bit layout and extra Flags2 bit's meaning remain unconfirmed. Controls
change the low byte, only Flags2, the complete U01 triple, or both native
configuration records. They preserve all image, FX, mesh and identity bytes;
their manifests prove exact inverse restoration. Outputs and pixel/emitter
audits are under `artifacts/fx-clones/fx-float-bitmap-controls-20261025`.
Native load/rendering results must be recorded separately.

The native tiny control's actual texel is transparent RGBA `(0,0,0,0)`, so it
can establish parsing but cannot establish visible particles.
`create_white_tiny_bitmap_control.py` changes only the two 1×1 texels to opaque
white `(1,1,1,1)` while retaining the original native float bitmap settings.
`TMIS_Sparkler_WhiteTinyNativeConfig.Item.Gbx` is a meaningful visibility
control; it deliberately changes the appearance and is not the final Sparkler.

`create_full_native_float_variant.py` applies only the complete observed native
float configuration records to SHA-pinned final Studio exports. It accepts the
expected image count1/2 and refuses external references. It leaves metadata,
identity, emitter expressions, mesh and pixels exactly unchanged. The two final
source replacements under `fx-final-nativefloat-20261025` retain the original
filenames/identities for an intentional cold-session replacement by the runtime
owner. They are test variants until native rendering is observed.

`create_fid_path_matrix.py` generates six small resolver controls from the
same SHA-pinned actual Sparkler: bare bitmap filename, `Texture/`,
`Media/Texture/`, `Stadium/Media/Texture/`, and backslash forms of the last two.
The previously tested full `GameData` forward/backslash forms are excluded.
Each changes only the two native GPU texture string branches and removes their
unused external-table entries. It records exact inverse restoration, unique
stage filenames and SHA-256 digests in `fid-path-matrix.json`. Empty root now
means a bare filename rather than an unintended leading slash. No generator
stages a file or invokes the game. Native preload and bitmap binding are the
next checks; successful parsing alone would not prove visible particles.

The read-only `TMISFxCustomPlacement` v3 probe also inspects at most eight
vanilla item variants, with one direct prefab per variant, to compare the
actual vanilla anchor against custom anchors. Its optional RenderInfo A8
observation is **not discriminating**: both the visibly working vanilla
Sparkler and blank inline controls report null in editor and gameplay. A null
value must not be reported as evidence of failed GPU upload.

The native matrix now proves that only the tested root
`Stadium\Media\Texture` resolves both actual Sparkler bitmaps. See
`fx-fid-path-matrix-20261025/native-matrix-summary.json` for the native verdict
for each exact control. This is binding evidence; rendering and persistence
are separate. `create_resolved_fogger.py` uses the same root for the stock
`FoggerSmoke.Texture.gbx` and retains the true Fogger's positive rate100 and
fog selector. `resolved_fogger_identity.py` additionally gives that exact
control its own full header/body item path by changing the text in its existing
Id definition slots, with an exact inverse. Neither helper stages files.
