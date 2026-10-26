# FX native save barrier

Date: 2026-10-25. File inspection and probe-source work only in this audit;
the root runtime agent owns native testing and screenshots.

The actual Sparkler resolves both original stock bitmaps through the exact
paths `Stadium\Media\Texture\Sparkler_D.Texture.gbx` and
`Stadium\Media\Texture\SparklerEnd_D.Texture.gbx`. The root runtime agent
observed actual orange sparks; evidence is
`artifacts/fx-clones/fx-resolved-visual-map-20261025/native-visible-anchor.json`
and `resolved-sparkler-visible-gameplay.png`. No additional user texture files
are needed. Native map embedding then reports an item-serialization error.
The working texture binding and the save failure are distinct results.

## Why the input string does not solve native save

The local E++ reverse-engineering records are:

- `research/2026-08-24-Foggers.md`: GPU chunk090C6002 v22, native writer at
  `0x14057aab0`; `isTextureFid` is seeded from **archive+0x18**, not a stored
  GPU-model property. A loaded string is resolved into the bitmap nod at
  GPU-model+0x70.
- `research/2026-08-24-GbxArchiveModes.md`: native item save mode10 and map
  embed mode8 leave archive+0x68=0, so FID-bearing children become external
  references.
- `research/2026-08-22-GbxFidRefSave.md`: the native writer subsequently
  rejects User/GameData cross-tree references. The flag permitting them is
  runtime archive/FID state, not an item archive field.
- `research/2026-08-24-FidQuestionSuffix.md`: `?N` is a process-local alias
  uniquifier. Reads strip it; writes resolve it to the original FID. It does
  not change the source tree or the cross-tree flag.

These are named function/layout observations for the 2026-08-24 research
build, not a claim that every address is unchanged in the current build. The
root's current native behavior independently matches the described save gate.

Changing the input chunk version cannot force a future map writer to preserve
the input string. Versions<=6 use a nod reference unconditionally; versions>=7
let the writer select its branch again. The secondary texture slots are also
always nod references. Replacing one texture slot with another would therefore
retain the save problem.

## Other file-only branches inspected

`CPlugBitmap` chunk09011030 archives Image as a typed `CPlugFileImg` nod
reference. It has no corresponding image-path string branch. An inline bitmap
with a GameData DDS child would still expose a cross-tree FID during save.

Chunk09011034 looked promising because GBX.NET labels one field
`ImageArrayFids` as a string array. Actual nonempty native archives disprove a
plain path-string interpretation: `Terrain_D.TextureArray.Gbx` contains count6
followed by indices3..8, exactly its six external DDS references. Native
`BitmapArrayElemName` fields select an array through another external reference.
The audit found1344 bitmap records, all version4; raw evidence is
`fx-image-generator-controls-20261025/native-bitmap-imagearrays.json`.

A separate scan of1351 native bitmap roots found no resource-index reference
table entries (`flags&4`), so it provides no resource-identity replacement for
the stock particle textures. Evidence is `native-bitmap-resource-refs.json`.
Do not confuse this on-disk reference-table flag with the runtime FID flag
discussed in the native cross-tree gate.

A scoped scan of79 Sparkler/Fogger/Smoke/Flare GBX filenames found no inline
primary bitmap rendering precedent. The observed GPU records use null or
external bitmap references. This scoped negative result does not prove that
all native assets lack inline bitmaps. Evidence is
`native-smoke-sparkler-gpu-precedents.json`.

No new item control is justified by these branches yet. Runtime flag changes,
code patches, transient aliases and guessed resource indices are not a
file-only persistent solution.

## Inline images and the next observation

The existing RGBA8/Plain inline controls already retain the same actual native
bitmap settings as the working DDS bitmap: usage0, sRGB1, filter3, mip mapping
requested, default color depth. Remaining differences are the image class,
format, channel type, origin, mip count and memory state. Their complete
read-only comparison is `bitmap-runtime-representation-comparison.json`.
These are correlated representation differences, not identified causes.

The optional A8 RenderInfo observation is null on both working vanilla and
blank inline controls. It does not discriminate rendering or GPU upload.
The v4 read-only probe additionally observes the descriptor pointer at
bitmap+0x178 and safe width/height reads at descriptor+0x28/+0x2C. Those offsets
come from `2026-08-24-ControlQuadBitmapBind.md` and
`2026-08-24-CalculatingShadowsDetect.md`, Trackmania.exe imagebase0x140000000.
A current448-byte class-size guard is necessary but does not by itself prove
current field offsets. Results label this old-build provenance explicitly.
The probe writes no game fields, retains the opt-in flag and reports bounded
dimensions; it does not claim to read image contents or verify a GPU upload.

Exact Main.as SHA-256:
`BB67B0336E3638997CDA0D7B272C8FE55FCE44A3A75E7147C74F36E2D8396187`.
LSP against the installed Openplanet1.29.14 typedb reports no diagnostics;
`probe-v4-lsp.json` preserves that result. This audit did not stage or execute
that source. The next meaningful native comparison is the same visible stock
anchor versus an actual blank inline anchor with the descriptor opt-in enabled.

A map exported offline with the original opaque item bytes can preserve the
input stock-path strings, but that is not proof that an ordinary subsequent
native map save will preserve embedding. A single separately installed item
may support a lower, file-required workflow after Save Anyway; that still
requires a cold native reopen/play test and does not establish map portability.
