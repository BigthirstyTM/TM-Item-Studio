# Flag items (TM2020): the measured settings, and what is still open

**Date:** 2026-10-15
**Evidence:** the game's own files under `C:\Users\PC\OpenplanetNext\Extract\GameData`
(`Stadium/Items/Flag16m.Item.Gbx`, `Stadium/Media/Prefab/Items/Flag/Flag16m.Prefab.Gbx`,
`Stadium/Media/Dyna/Flag/Flag.DynaObject.Gbx` and `Flag.Mesh.Gbx`,
`Stadium/Media/Material/ItemFlag.Material.Gbx` and `ItemFlagNoAnim.Material.Gbx`,
`Stadium/Media/PlaceParam/Flag.PlaceParam.Gbx`) and the item corpus under
`C:\Users\PC\Documents\Trackmania\Items` plus the 8 802 `.Item.Gbx` files profiled in
[docs/evidence/flag-item-profile.tsv](evidence/flag-item-profile.tsv).
**Reproduce:** `Tests/FlagProbe` (`dump`, `--tags`, `--placement`, `--profile`, `--describe`,
`--lod`, `--lodstats`, `--entries`, `--convert`) and `Tests/FlagConversion` (the conversion suite).

Nothing here is documented by Nadeo; every value below was read out of the files. There is no
public tooling or write-up for custom flags, so the vanilla flag *is* the specification.

## 1. What makes an item a flag

Two independent signatures were measured over the whole corpus; each occurs on exactly the four
vanilla flag items (Stadium/RedIsland × Flag16m/Flag8m) and nowhere else.

| Layer | Setting | Vanilla value |
| --- | --- | --- |
| Variant | `NPlugItem_SVariant.Tags["Type"]` | `Flag` |
| Variant | `NPlugItem_SVariant.Tags["MatModifier"]` | `Ice` |
| Variant list | `NPlugItem_SVariantList.Version` | `1` |
| Placement | `CGameItemPlacementParam.Flags` | `1` |
| Placement | `PivotSnapDistance` | `-1` |
| Placement | `CubeSize` | `0` |
| Placement | `FlyVStep`, `FlyVOffset` | `0`, `0` |
| Placement class | `Version` / `SizeGroup` | `10` / `0x0` |
| Placement class | `AlwaysUp` / `AlignToWorldDir` | `true` / `true` |
| Placement class | `AlignToInterior` | `false` |
| Placement class | `WorldDir` | `<0, 0, 1>` |
| Placement class | `PatchLayouts` | 8 entries (see below) |

`Type=Flag` is the classification the inventory uses; the placement class is what makes the item
stand upright and align to its world direction. `AlwaysUp=true` **with** `AlignToWorldDir=true`
occurs on the same four files and on nothing else in 8 802 items, so the pair is a reliable second
signature (and the one that tells an author "this is a flag", not merely "this is tagged as one").

The eight patch layouts (file order) are:

| # | ItemCount | ItemSpacing | NormedPos | OnlyOnGroups |
| --- | --- | --- | --- | --- |
| 0 | 0 | 12 | 0.5 | Border, StructureSupport, BorderInGround, BorderWaterSmall, BorderPlatform, BorderPlatformWater, BorderDecoWall, BorderTrackWall, BorderMultiSize |
| 1 | 0 | 16 | 0.5 | same nine groups |
| 2 | 0 | 12 | 0.9 | BorderOpen |
| 3 | 0 | 16 | 0.9 | BorderOpen |
| 4 | 0 | 12 | 0.915 | BorderWater |
| 5 | 0 | 16 | 0.915 | BorderWater |
| 6 | 0 | 12 | 0.875 | BorderOpenCurve1 |
| 7 | 0 | 16 | 0.875 | BorderOpenCurve1 |

All eight use `FillAlign=0`, `FillDir=1`, `U01=0`, `Altitude=0`, `U02=0`. The 12 m/16 m pairs are
the item's two sizes; the groups are where the editor offers to patch the flag onto the track.

## 2. What the composed item looks like

Everything below was measured on the two vanilla flags and is what a custom flag has to reproduce.

**Item body (`CGameItemModel`)**

- `ItemType = Ornament`, `WaypointType = None`, `PageName = Items`.
- Chunks present: `2E001003-v8`, `2E001004`, `2E001006`, `2E002000`, `2E002001-v0`, `2E001009`,
  `2E00100B`, `2E00100C`, `2E00100D`, `2E001010-v4`, `2E001011-v1`, `2E001012`, `2E002008`,
  `2E002009`, `2E00200C`, `2E002012`, `2E002015`, `2E002019-v15`, `2E00201A`, `2E00201C-v5`,
  `2E00201E-v7`, `2E00201F-v12`, `2E002020-v3`, `2E002025-v0`, `2E002026-v0`, `2E002027-v0`, plus
  the game-skin header chunk `090F4000-v8` whose node is a `CPlugGameSkin`.
- `2E001012` = `U01=0, U02=1, U03=2, U04=0`. `U03=2` is *not* flag-specific (867 other items use
  it), so it is not part of the flag signature.
- The entity model is a `NPlugItem_SVariantList` whose variant references the prefab file
  `Media\Prefab\Items\Flag\Flag16m.Prefab.Gbx`, and the placement parameter is an external
  `Media\PlaceParam\Flag.PlaceParam.Gbx` reference in the vanilla files. Both also work inline.

**Prefab (2 entries)**

| Entry | Model | Transform (Position / Rotation) |
| --- | --- | --- |
| 0 | inline `CPlugStaticObjectModel v3` with the pole mesh, `Shape` = a `CPlugSurface` | `<0, 0, 0>` / `<0, 0, 0, 1>` |
| 1 | the cloth; vanilla references `Dyna\Flag\Flag.DynaObject.Gbx` by file, the custom item carries the same object inline. `Params` = `NPlugDynaObjectModel_SInstanceParams` | `<0, 11.367074, 0.13515112>` / `<0, -0.70710677, 0, 0.70710677>` |

The cloth entry's parameters are `Version=2`, `PeriodSc=8`, `PeriodScMax=16`, `TextureId=0`,
`Phase01=0`, `Phase01Max=1`, `IsKinematic=false`, `CastStaticShadow=false`. `PeriodSc`/`PeriodScMax`
(8 s / 16 s) are the flag's wave period range and `Phase01`/`Phase01Max` the per-instance phase
range: they are what makes two placed flags wave out of step.

**Meshes**

| | Pole (`Ents[0]`) | Cloth (`Ents[1]`) |
| --- | --- | --- |
| `LodMaxDistAtFov90` | `[16, 64, 512]` | `[16, 64, 128, 512]` |
| Visuals | 5: 180, 180, 72, 72, 18 indices | 5: 726, 216, 54, 24, 6 indices |
| `LodMask` per visual | `1, 1, 2, 2, 4` | `1, 2, 4, 8, 16` |
| Materials | `Technics`, `TechnicsTrims` | `ItemFlag` (LOD0–3), `ItemFlagNoAnim` (far LOD) |
| Local bounds | 0.135 × 13.008 × 0.135 m | 2.265 m tall, 0.098 m thick, 1.965 m from the pole axis |

The cloth's two materials are the animated one (`ItemFlag.Material.Gbx`, whose shader is
`Techno3\Media\Material\Tech3_Warp_TDiffSpec_VertexTween.Material.gbx` — a vertex-tween "warp"
shader) and a non-animated one (`ItemFlagNoAnim.Material.Gbx`) for the far LOD. A mesh can carry
them either as external `Materials` references (vanilla) or as `CustomMaterials` records whose
`Link` names the game material (`ItemFlag`, `ItemFlagNoAnim`) — the custom item uses the latter and
still waves, so the link form is sufficient.

**A mesh with more LOD levels than LOD distances is normal.** In the corpus, 6 488 of 6 588
Nadeo solids have shaded-geometry masks that address more levels than `LodMaxDistAtFov90` has
entries — including both vanilla flag meshes. The flag cloth is therefore not anomalous, and the
LOD data should not be "fixed" by padding the distance list.

## 3. Converting an item into a flag

`Models/ItemFlagConversion.cs` applies the classification and the placement, and nothing else:

- `ItemFlagConversion.Inspect(item)` measures an item against the table above and reports the
  deviations; it never writes.
- `ItemFlagConversion.Convert(item)` wraps a single-model item in a `Type=Flag`/`MatModifier=Ice`
  variant (or tags the variants of an existing list in place), sets the flag's placement class with
  its eight patch layouts, and normalises the placement-parameter fields the vanilla flag sets.
  Pivots, geometry, materials, skins and metadata are left alone.
- `Models/ItemFlagExport.cs` runs the conversion on its own snapshot of the document, gives the
  result a distinct id (`<id>_Flag`) so the original stays usable, and refuses to hand back a file
  that cannot be reopened or that does not measure as a flag.

**Conditions and limitations**

- The conversion cannot invent the cloth. It changes classification and placement only; the mesh,
  the wave materials and the cloth entry's dyna parameters come from the item being converted.
- An item whose placement parameters live in an external file is refused, and so is one whose
  placement-class chunk `2E020005` is opaque. Everything that can refuse runs before the first
  write, so a rejected conversion leaves the bytes untouched.
- A newer variant-list version is refused rather than downgraded.
- An item without placement parameters is classified and reported, but keeps its own placement and
  the note says so.
- Verified by `Tests/FlagConversion` (68 checks: measured-shape assertions, save/reparse round
  trips, export snapshot isolation, refused-write byte equality) and registered in
  `Tests/run-all.sh`.

### The XertroV flag (a real repair)

`ztest-flag3.Item.Gbx` was tagged like a flag but carried the wrong shape. Measured deviations
before and after `Tests/FlagProbe --convert`:

| Field | Before | After |
| --- | --- | --- |
| `VariantList.Version` | `0` | `1` |
| `Placement.FlyVStep` | `1` | `0` |
| `PlacementClass.AlwaysUp` | `false` | `true` |
| `PlacementClass.AlignToWorldDir` | `false` | `true` |
| `PlacementClass.AlignToInterior` | `true` | `false` |
| `PlacementClass.PatchLayouts` | 9 entries | 8 entries |

Six deviations became zero, and the file reparses with `isFlag=True`. That is also the answer to
"why did the old custom flag not work": it was *classified* as a flag in the inventory, but its
placement class was an ordinary aligned one, so it did not stand upright and did not align as a
flag. (See [docs/evidence/flag-xertrov-repair.txt](evidence/flag-xertrov-repair.txt).)

## 4. Still open: the cloth disappears at range

After the repair the cloth waves and the pole stands, but in game the cloth mesh disappears
completely once the camera is far enough (the pole keeps rendering), and a flag placed far away
waves chaotically. Measurements taken so far:

- The cloth's geometry, `LodMaxDistAtFov90`, `LodMask` list, both wave materials (by link name) and
  the prefab entry transform are **identical to the vanilla flag's**, byte for byte. A mesh with
  more LOD levels than distances is the corpus norm (§2).
- The one remaining structural difference is *storage*: Nadeo keeps the cloth outside the item —
  `Ents[1].ModelFile` → `Dyna\Flag\Flag.DynaObject.Gbx` → `MeshFile` → `Flag.Mesh.Gbx` — while the
  custom item embeds both the dyna object and its mesh inline in the item, and therefore also keeps
  a visible second copy of that mesh in the item's own node tree.
- Note that no working item in the user's corpus references a sibling `.Mesh.Gbx` either: some
  files sit next to their item, but the items do not reference them (`AutoTerrainer_Test.Item.Gbx`
  has `MeshFile = null`). So "externalise the mesh" is an experiment, not an established fix.

`Tests/FlagProbe --convert` therefore gained three variants that isolate the storage question; they
were built from the original item and written to `02. Freebuff RE Items`:

| File | Arrangement | What it answers |
| --- | --- | --- |
| `XertrovFlagRepair.Item.Gbx` | unchanged: inline dyna object with inline cloth mesh | the current behaviour (cloth vanishes at range, cloth off the pole) |
| `XertrovFlag_nomesh.Item.Gbx` | dyna entry kept, its `Mesh` removed | is the inline cloth mesh itself what the item renderer mishandles? (cloth should be invisible) |
| `XertrovFlag_static.Item.Gbx` | cloth moved onto a plain `CPlugStaticObjectModel` entry, no dyna body | does the wave survive without a dyna instance (is the waving the vertex-tween material)? |
| `XertrovFlag_external.Item.Gbx` + `XertrovFlag_external.Mesh.Gbx` | Nadeo's arrangement: dyna `MeshFile` → companion `.Mesh.Gbx` | does externalising the cloth mesh restore it at range? |

Commands:

```bash
dotnet run --project Tests/FlagProbe -- --convert \
  "C:\Users\PC\Documents\Trackmania\Items\XertrovItems\ztest-flag3.Item.Gbx" \
  "C:\Users\PC\Documents\Trackmania\Items\02. Freebuff RE Items\XertrovFlag_static.Item.Gbx" \
  --cloth-static
```

Replace `--cloth-static` with `--cloth-nomesh` or `--cloth-external`; add `--keep-placement` to
apply only the variant tag and the placement class without normalising the placement-parameter
values.

## 5. Reproducing the measurements

```bash
# the flag fingerprint over a whole corpus (one TSV row per item)
dotnet run --project Tests/FlagProbe -- --profile "C:\...\Items" --out docs/evidence/flag-item-profile.tsv

# variant tag vocabulary over a corpus
dotnet run --project Tests/FlagProbe -- --tags "C:\Users\PC\Documents\Trackmania\Items" --out docs/evidence/flag-variant-tags.tsv

# LOD distances, masks, per-visual geometry and authored bounds of any file
dotnet run --project Tests/FlagProbe -- --lod <file> [<file> ...]

# LOD distance/level consistency over a corpus
dotnet run --project Tests/FlagProbe -- --lodstats "C:\...\Stadium\Media"

# prefab entries with their transforms (Position/Rotation/Model/ModelFile/Params)
dotnet run --project Tests/FlagProbe -- --entries <file> [<file> ...]

# measure, convert and write
dotnet run --project Tests/FlagProbe -- --convert <in.Item.Gbx> <out.Item.Gbx> [variants]
```

Evidence files: [flag-item-profile.tsv](evidence/flag-item-profile.tsv),
[flag-xertrov-repair.txt](evidence/flag-xertrov-repair.txt),
[flag-lod-vanilla.txt](evidence/flag-lod-vanilla.txt),
[flag-lod-xertrov.txt](evidence/flag-lod-xertrov.txt),
[flag-lodstats-stadium-media.tsv](evidence/flag-lodstats-stadium-media.tsv),
[flag-xertrov-vs-vanilla.txt](evidence/flag-xertrov-vs-vanilla.txt).
