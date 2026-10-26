# Flag items (TM2020): measured metadata and waving-cloth exports

**Date:** 2026-10-15; runtime corrections through 2026-10-22
**Current implementation:** [Waving flag converter and embedded-map export](waving-flag-converter.md).
**Current native evidence:** [Embedded custom flags without Openplanet](custom-flag-embedded-no-openplanet-2026-10-22.md).
Long, swallowtail and round custom flags now animate in a dependency-complete
embedded map after a cold start without Openplanet or loose custom assets,
including an ordinary Local race. The proven route still requires a genuine
native Flag16m anchor and dependency/reference repair after native saving.
The authoring package contains an Item plus three sibling dependencies; a
single independently initialized Item remains unresolved. New SourceMesh
conversion writes actual animated geometry but is offline verified only.
The first comparison on the 2026-02-02 game build showed normally sized, waving
custom candidates. Later isolated tests **did reproduce severe rendering failures**:
oversized/deformed cloth, missing cloth, and edited geometry not visibly respected.
Those early failures are historical controls. Placement metadata alone does not
create cloth or repair its rendering; the new waving exporter is a separate path.
**Evidence:** the game's own files under `C:\Users\PC\OpenplanetNext\Extract\GameData`
(`Stadium/Items/Flag16m.Item.Gbx`, `Stadium/Media/Prefab/Items/Flag/Flag16m.Prefab.Gbx`,
`Stadium/Media/Dyna/Flag/Flag.DynaObject.Gbx` and `Flag.Mesh.Gbx`,
`Stadium/Media/Material/ItemFlag.Material.Gbx` and `ItemFlagNoAnim.Material.Gbx`,
`Stadium/Media/PlaceParam/Flag.PlaceParam.Gbx`) and the item corpus under
`C:\Users\PC\Documents\Trackmania\Items` plus the 8 802 `.Item.Gbx` files profiled in
[docs/evidence/flag-item-profile.tsv](evidence/flag-item-profile.tsv).
**Reproduce:** `Tests/FlagProbe` (`dump`, `--tags`, `--placement`, `--profile`, `--describe`,
`--lod`, `--lodstats`, `--entries`, `--convert`) and `Tests/FlagConversion` (the conversion suite).

Every value below was read out of files. The vanilla flag is the reference for this investigation.

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
| Authored local AABB size | approximately 0.270 × 14.016 × 0.270 m across pole visuals | LOD0: approximately 3.930 × 3.628 × 2.016 m |

`BoxAligned` stores **center and half extent**, not minimum and maximum. The earlier
dimensions were calculated incorrectly. The actual LOD0 cloth vertex envelope over
all 86 frames is approximately 4.031 × 4.523 × 4.209 m; this differs from its authored
AABB. Recalculating bounds over all frames did not by itself fix custom rendering.

The cloth's two materials are the animated one (`ItemFlag.Material.Gbx`, whose shader is
`Techno3\Media\Material\Tech3_Warp_TDiffSpec_VertexTween.Material.gbx` — a vertex-tween "warp"
shader) and a non-animated one (`ItemFlagNoAnim.Material.Gbx`) for the far LOD. A mesh can carry
them either as external `Materials` references (vanilla) or as `CustomMaterials` records whose
`Link` names the game material (`ItemFlag`, `ItemFlagNoAnim`). The link form loaded
and produced motion in some tests, but did not establish correct rendering of an
independent edited cloth mesh.

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
- Verified by `Tests/FlagConversion` (74 checks on 2026-10-21: measured-shape assertions, save/reparse round
  trips, export snapshot isolation, refused-write byte equality) and registered in
  `Tests/run-all.sh`.

### The XertroV flag (placement metadata correction)

`ztest-flag3.Item.Gbx` was tagged like a flag but carried different placement metadata. Measured deviations
before and after `Tests/FlagProbe --convert`:

| Field | Before | After |
| --- | --- | --- |
| `VariantList.Version` | `0` | `1` |
| `Placement.FlyVStep` | `1` | `0` |
| `PlacementClass.AlwaysUp` | `false` | `true` |
| `PlacementClass.AlignToWorldDir` | `false` | `true` |
| `PlacementClass.AlignToInterior` | `true` | `false` |
| `PlacementClass.PatchLayouts` | 9 entries | 8 entries |

Six deviations became zero, and the file reparses with `isFlag=True`.
This proves a metadata correction, **not the cause of the reported rendering problem**.
On 2026-10-21 both the original and the metadata-corrected dynamic flag rendered in the
controlled placement tests. See [flag-xertrov-repair.txt](evidence/flag-xertrov-repair.txt)
for the earlier file measurements and the current runtime report for the game observations.

## 4. Runtime failures: early visibility was insufficient evidence

Early 2026-10-21 tests showed normally sized dynamic inline candidates through 550 m,
including at `[1360,8,1360]`, in the editor and editor playground, and after saving
and reopening a map. These were observations of visible flags, not proof that the
renderer respected the custom vertices. Later tests with an unmistakably different
raster mesh rendered oversized/deformed cloth, including after a clean game start
with that candidate alone. Other material-storage variants rendered no cloth.
The specific earlier origin/distance mechanism has not been isolated.

Updated measurements:

- The original custom cloth's parsed vertex positions/topology, `LodMaxDistAtFov90`,
  `LodMask` list, material link names and prefab entry transform match vanilla. A mesh with
  more LOD levels than distances is the corpus norm (§2).
- A structural difference is *storage*: Nadeo keeps the cloth outside the item —
  `Ents[1].ModelFile` → `Dyna\Flag\Flag.DynaObject.Gbx` → `MeshFile` → `Flag.Mesh.Gbx` — while the
  custom item embeds both the dyna object and its mesh inline in the item, and therefore also keeps
  that mesh in the item's own node tree. Inline graphs loaded in live tests;
  loading is insufficient evidence of correct custom rendering.
- The original custom file also differs in index-buffer flags (`0` versus vanilla `2`),
  material representation, and its missing `090F4000` game-skin header. Both index-flag
  candidates rendered; setting `2` is preservation of vanilla data, not a confirmed fix.
- The animated LODs each contain 86 subvisual animation frames. The far LOD is intentionally
  nonanimated. A geometry edit must account for every frame and LOD; changing classification
  alone cannot create this animation.
- Note that no working item in the user's corpus references a sibling `.Mesh.Gbx` either: some
  files sit next to their item, but the items do not reference them (`AutoTerrainer_Test.Item.Gbx`
  has `MeshFile = null`). So "externalise the mesh" is an experiment, not an established fix.

Earlier `Tests/FlagProbe --convert` experiments isolated the storage question. They are
research fixtures, **not recommended exports**:

| File | Arrangement | What it answers |
| --- | --- | --- |
| `XertrovFlagRepair.Item.Gbx` | inline dyna object with inline cloth mesh | visible in the 2026-10-21 placement check; older failure not reproduced |
| `XertrovFlag_nomesh.Item.Gbx` | dyna entry kept, its `Mesh` removed | is the inline cloth mesh itself what the item renderer mishandles? (cloth should be invisible) |
| `XertrovFlag_static.Item.Gbx` | cloth moved onto a plain `CPlugStaticObjectModel` entry, no dyna body | **crashed the game on placement** on 2026-10-21; do not use |
| `XertrovFlag_external.Item.Gbx` + `XertrovFlag_external.Mesh.Gbx` | Nadeo's arrangement: dyna `MeshFile` → companion `.Mesh.Gbx` | does externalising the cloth mesh restore it at range? |

The historically named `--research final` command generates **research candidates**,
not approved custom templates. All generated models have been archived outside the
live Items directory. Static-cloth, material-name-only and frame-table-removal
experiments caused crashes and are rejected; successful GBX.NET parsing is
insufficient to establish engine safety.

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
