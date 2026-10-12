# Chunk reference for `.Item.Gbx` (TM2020 items)

**Date:** 2026-10-08 (translated to English; content unchanged)
**Corpus:** 7,454 `.Item.Gbx` files under `C:\Users\PC\Documents\Trackmania` (7,301 parsed successfully, 153 failed).
**Distinct chunk ids found:** 86 (0 completely unknown ids — though there are 6 chunks whose *content* nobody decodes, see §6).
**Basis:** our own measurements (scanner + dumps), the decompiled source of the bundled `lib/GBX.NET.dll`, and community sources (Mania Tech Wiki / ManiaPlanet Wiki / GBX.NET repo).

> Scope: only the chunks that occur in `.Item.Gbx` files. Chunks of maps/replays/media etc. are out of scope.

---

## 1. How chunks are stored in an Item.Gbx (format)

An item file has three parts: the **header** (with *user-data* = header chunks), the **reference table** (external files) and the **body** (compressed, LZO). The body is a sequence of classes; each class is a sequence of chunks.

Confirmed by our hex dumps and GBX.NET's own `Write()` code:

| Element | Layout | Source |
|---|---|---|
| Chunk id | `uint32` LE, e.g. `10 10 00 2E` = `0x2E001010` | wiki + dumps |
| **Known** chunk | id + **data directly** — *no* length field; the reader must already know the structure | wiki ("chunk data is not self-describing"), dumps |
| **Skippable** chunk | id + sentinel `0x534B4950` (on disk the bytes `50 49 4B 53` = "PIKS") + `uint32 size` + data | GBX.NET `CMwNod.Write()`, dumps (e.g. all `2E02000x` placement chunks) |
| End of class stream | dummy id `0xFACADE01` (`01 DE CA FA`) | ManiaPlanet wiki; seen in dumps |
| **Header chunks** (user-data) | id + `uint32 size` + data (the header *does* use lengths, including bit 31 = "heavy") | Mania Tech Wiki |
| Embedded class | `classId` + chunk stream + `0xFACADE01` | dumps (including the placement param in `2E00201C`) |

Important consequence: if a reader misjudges the content of a *known* chunk, it immediately goes out of sync — which is exactly what happens in 91 items (§7).

`CGameItemModel` inherits from **`CGameCtnCollector`** (class `0x2E001000`); that is why item files carry chunks with the `2E001xxx` prefix (collector properties: name, icon, skin…) alongside their own `2E002xxx` chunks.

---

## 2. Which classes occur in an item

From the scanner (owner of each chunk set, with file counts):

| Class | Class id | Files | Role |
|---|---|---:|---|
| `CGameItemModel` | 2E002000 | 7,301 | the item itself (header + body chunks) |
| `CGameItemPlacementParam` | 2E020000 | 7,298 | placement / grid settings |
| `CPlugCrystal` | 09003000 | 5,303 (+~98 failed) | collision shape |
| `CGameCommonItemEntityModelEdition` | 2E026000 | 5,306 | entity "edition" data |
| `CGameCommonItemEntityModel` | 2E027000 | 1,743 | entity runtime data |
| `CPlugSolid2Model` | 090BB000 | 1,976 | visual solid model |
| `CPlugMaterialUserInst` | 090FD000 | 1,976 | material instances |
| `CPlugVisualIndexedTriangles` | 0901E000 | 1,976 | triangle meshes |
| `CPlugIndexBuffer` | 09057000 | 1,851/143 | index buffer (2 variants, §8) |
| `CPlugLightUserModel` | 090F9000 | 177 | light parameter |
| `CPlugSkel` | 090BA000 | 173 | skeleton |
| `CPlugSurface` | 0900C000 | 404 | physics surface (custom collision) |
| `CGameVehicleModel` | 2E01C000 | 14 | vehicle items |
| `CPlugLight` / `GxLightBall` / `GxLightSpot` | 0901D000 / 04002000 / 0400B000 | 34/34/1 | light entities |
| `CPlugMaterial` (fully embedded) | 09079000 | 2 | legacy material format |

---

## 3. Header chunks (user-data) of CGameItemModel/CGameCtnCollector

These come **before** the body and hold ident, name, icon and production state. GBX.NET attaches them to the node itself (which is why they show up in `node.Chunks`).

| Chunk id | GBX.NET class | Files | What it does (from read code) |
|---|---|---:|---|
| `2E001002` | Chunk2E001002 | 3 | **"Ident"** — legacy TM10/TMSX ident; only in 3 old items, placed before `2E001009` |
| `2E001003` v8 | HeaderChunk2E001003 | 7,300 | **"desc"**: ident (id/collection/author), version, pageName, parentCollectorId (v≥4), flags enum, catalogPosition (i16), up to v5 also `nbAvailableMin/Max` + copperPrice, from v7 `name`, from v8 `prodState` (enum byte) |
| `2E001004` | HeaderChunk2E001004 | 6,341 | **icon pixel data**. First `i16 width, i16 height`; if both carry the `0x8000` bit there is a **WebP blob** inside (plus an extra i16 + length); otherwise ARGB pixels (`Color[w,h]`, rows mirrored vertically). → §10.3 |
| `2E001006` | HeaderChunk2E001006 | 7,300 | **"lightmap compute time"**: FILETIME |
| `2E001008` v0 | HeaderChunk2E001008 | 14 | **"default skin"**: version byte + `defaultSkinName` (vehicle items only) |
| `2E001009` | Chunk2E001009 | 7,300 | pageName + optional icon fid (bool + NodeRef) + `parentCollectorId` |
| `2E00100B` | Chunk2E00100B | 7,300 | **ident** (id, collection, author) again in the body part |
| `2E00100C` | Chunk2E00100C | 7,300 | **"collector name"** — display name |
| `2E00100D` | Chunk2E00100D | 7,300 | **"description"** |
| `2E00100E` | Chunk2E00100E | 5,694 | **"icon render"**: `iconUseAutoRender` (bool) + `iconQuarterRotationY` (i32) |
| `2E001010` v3/v4 | Chunk2E001010 | 7,300 | **skins**: `defaultSkin` NodeRef + `skinDirectory` (string); v≥2 and empty dir → extra NodeRef (`U01`) |
| `2E001011` v1 | Chunk2E001011 | 7,300 | `isInternal`, `isAdvanced` (bools), `catalogPosition` (i32), v≥1 `prodState` |
| `2E001012` | Chunk2E001012 | 1,606 | **4× i32, meaning unknown** — measured values: `U01=0`, `U02=1`, `U03∈{0,1,2,3}`, `U04=0` (TM2020 items only) — candidate for further research (§10.6) |
| `2E002000` (header variant) | HeaderChunk2E002000 | 7,300 | **item type** (enum i32: Ornament/Vehicle/…) |
| `2E002001` v0 (header) | HeaderChunk2E002001 | 7,300 | **"file version"** |
| `090F4000` v8 | HeaderChunk090F4000 | 14 | header chunk of `CPlugGameSkin` (vehicle items with a skin) |

Note: `2E002000` and `2E002001` exist **twice** with different content — as a header chunk (item type / file version) and as a body chunk (vehicle NodeRef / gameSkin NodeRef, §4). Same id, different context.

---

## 4. Body chunks of CGameItemModel (complete list)

Most common order (canonical, 26–29 chunks; uppercase = the header part comes before the body):

```
2E001003 2E001004 2E001006 [2E002000-h 2E002001-h] 2E001009 2E00100B 2E00100C
2E00100D 2E00100E 2E001010 2E001011 [2E001012] 2E002008 2E002009 2E00200C
2E002012 2E002015 2E002019 2E00201A 2E00201C 2E00201E 2E00201F 2E002020
[2E002021 or 2E002023] 2E002025 2E002026 2E002027
```

| Chunk id | Files | Versions (freq.) | What it does |
|---|---:|---|---|
| `2E002000` | 7,300 | – | **vehicle**: NodeRef to the vehicle model (empty for normal items) |
| `2E002001` | 7,300 | – | **gameSkin**: NodeRef to the skin |
| `2E002008` | 7,300 | – | "Nadeo skin fids": array of NodeRefs (`nadeoSkinFids`) |
| `2E002009` | 7,300 | – | "Cameras": array of NodeRefs (deprecated) |
| `2E00200C` | 7,300 | – | "race interface fid" |
| `2E002012` | 7,300 | – | placement/camera ground values: `groundPoint` (Vec3), `painterGroundMargin`, `orbitalCenterHeightFromGround`, `orbitalRadiusBase`, `orbitalPreviewAngle` (floats) |
| `2E002015` | 7,300 | – | "ItemTypeE": a broader item-type enum (i32) |
| `2E002019` | 7,300 | 15 (4,446), 13 (2,854) | **"model"** — the most important reference chunk: optional `phyModelCustom`/`visModelCustom` (when the itemType version is lower), v≥3 `defaultWeaponName` (id), v≥4/5 phy/vis NodeRefs (or -1), v≥6 `actions` array, v≥7 `defaultCam` enum, v≥8 `entityModelEdition` NodeRef (+ `entityModel` if that one is null), v≥13 `vfx` NodeRef, v≥15 `materialModifier` |
| `2E00201A` | 7,300 | – | a bare NodeRef (`U01`), meaning unknown |
| `2E00201C` v5 | 7,300 | 5 | **"default placement"**: v≥5 = NodeRef to `CGameItemPlacementParam` (embedded, §5); v<5 = legacy: array of pivots + 15 floats (in this corpus always v5) |
| `2E00201E` | 7,300 | 7 (4,446), 6 (2,854) | **archetype**: `archetypeRef` (string), v≥5 `archetypeFid` (NodeRef when ref is empty), v≥6 extra string `U01`, v≥7 i32 `U02` |
| `2E00201F` | 7,300 | 11 (4,196), 10 (1,498), 12 (1,456), 13 (150) | **waypoint chunk** (the crash chunk from earlier research): `waypointType` enum; v≤7 legacy `Iso4 U06`; v≥6 `disableLightmap` (bool); v≤12 NodeRef `U07`; v≥11 byte `U08`; v≥12 i32 `U09`+`U10`. **v13 does not exist in GBX.NET's branches (falls back), v12/U08=1 correlates with the Trackmania crash.** Vanilla corpus: v13 (694) and v12 (655) — v13 is the *normal* version in game-authored items (§12.4) |
| `2E002020` v3 | 7,300 | 3 | **icon reference**: `iconFid` (string); v≥3 extra bool byte `U03`; v≤1 legacy (string + NodeRef) |
| `2E002021` v0 | 2,854 | 0 | **group elements**: array of `{Iso4 location, itemRef, item}` — the building blocks of item groups/macroblock-like compositions |
| `2E002023` v0 | 2,920 | 0 | byte `U01` + i32 `U02` (measured always `0, 0`) |
| `2E002024` v0 | 2,920 | 0 | **array of Vec2** (`U01`) — presumably a UV/2D list; in practice always empty (`[0]`) in this corpus |
| `2E002025` v0 | 7,300 | 0 | **bool** (measured always `false`) |
| `2E002026` v0 | 7,300 | 0 | **array of Vec3** (positioning points?) — 100% frequency |
| `2E002027` v0 | 7,300 | 0 | **float** (100%) |

Not found in this corpus (though defined in GBX.NET): `2E002002` (old race interface), `2E002003` (LowQualitySolid), `2E00200A/B` (decorator/stem materials), `2E00200D` (forced skins), `2E00200E/2E002011` (StadiumCar materials), `2E002010` (banner profile), `2E002013` (audio env), `2E002014/2E00201B` (bare NodeRefs), `2E002022` (int + string[] + int + ushort[]).

---

## 5. CGameItemPlacementParam — placement chunks (NEW: fully decoded)

These are **skippable** chunks (`SKIP` sentinel + size). Exactly three combinations occur in the corpus:

| Seq. | Count | Chunks |
|---|---:|---|
| **A** | 3,806 | `2E020000, 2E020001, 2E020003, 2E020004` |
| **B** | 2,854 | `2E020000, 2E020001, 2E020003` |
| **C** | 638 | `2E020000, 2E020001, 2E020004, 2E020005` |

| Chunk id | Files | Status | Content |
|---|---:|---|---|
| `2E020000` v0 | 7,298 | parsed | basic placement: `flags` (i16; bit1=yawOnly, bit2=notOnObject, bit3=autoRotation, bit4=switchPivotManually), `cubeCenter` (Vec3), `cubeSize`, `gridSnapHStep/VStep/HOffset/VOffset`, `flyVStep/VOffset`, `pivotSnapDistance` (floats) |
| `2E020001` | 7,298 | parsed | "pivot positions": `pivotPositions[]` + `pivotRotations[]` |
| `2E020003` | 6,660 | **raw data** (GBX.NET only reads the version) | **decoded — see below** |
| `2E020004` | 4,444 (custom) / 495 (vanilla) | **raw data** | **decoded — see §12.3**: `i32 version (=0) + i32 count + count × (Vec3 position, Vec3 rotation in degrees)`, 24 B per entry. In the custom corpus `count` is 0 in all 4,444 files, which is why it looked like a constant 8-zero-byte payload; vanilla items carry real lists (count 1…24). The *presence* of the chunk is the signal, the list itself is optional content |
| `2E020005` | 638 | parsed | NodeRef `placementClass` + `placementClassFile` (external file instead of inline) |

### Decoding `2E020003` ("PlacementClass", all 6,660 payloads analysed)

Four unique payloads, all with **version = 3**:

| Size | Count | Hex (core) | Structure |
|---:|---:|---|---|
| 32 B | 2,854 | `03…00 06…00 FFFF…` | `i32 v=3, i32 X=6, 6× i32 = [-1, 0, 0, 1, 0, 0]` |
| 48 B | 102 | `03…00 08…00 FFFF…` | `v=3, X=8, 10× i32 = [-1, 0, 0, 1, 0, 0, 0, 1.0f, 0, 0]` |
| 48 B | 3,673 | `03…00 0A…00 FFFF…` | `v=3, X=10, same 10 values` |
| 71 B | 31 | `03…00 0A…00 00000040 03…"1x1"…` | `v=3, X=10,` third i32 `0x40000000` (float 2.0?), then a **string `len=3` → "1x1"**, followed by fields including `1.0f` and an `i32=4` |

Correlations (perfect, no exceptions):
- **32 B ↔ seq. B** (no `2E020004`), **48/71 B ↔ seq. A** (with `2E020004`).
- The 71 B variant with the string **"1x1"** comes from one unique item (31 copies in the corpus, all identical, entity = `NPlugItem_SVariantList`, no icon). The string looks like a **size/grid label** ("1x1" = footprint).
- `X` (6/8/10) is *not* a count of the i32s (X=8 and X=10 carry the same 10 words) — presumably an internal version/flags field.

**Position of `2E020005` ↔ specials:** 637 of 638 seq.-C items have **no icon** (corpus-wide only 39%), and C is the only sequence containing the special item types (Character, Vehicle, Wagon, Turret, Spot, PickUp, EntitySpawner, Undefined) — these are items that load their placement class **externally** instead of inline.

---

## 6. Entity model classes

| Chunk id | Class | Files | Versions | Content |
|---|---|---:|---|---|
| `2E026000` | CGameCommonItemEntityModelEdition | 5,306 | 7 (5,257), 8 (49) | Very large chunk: `itemType` enum, `meshCrystal` NodeRef, string `U01`, `CPlugSolid U02`, image array, spriteParams[], particle emitter, anim, lightball states, 7 floats (`U07–U13`: including bounding-like values), `Iso4 U14`, v≥3+PickUp `mass`, bool `U15` + optional `CPlugCrystal U16`, `U17` block with i32+Iso4, `U20`; **v≥1: `inventoryName`, `inventoryDescription`, `inventoryItemClass`, `inventoryOccupation`** (the inventory fields!), v6–7 extra NodeRef `U21`, v≥7+PickUp bool `U22` |
| `2E026001` | CGameCommonItemEntityModelEdition | 5,306 | – | **Raw (Ignore=true): always exactly 8 zero bytes** in 5,306/5,306 files. Only exists after `2E026000`. Probably an empty/reserved structure (e.g. two i32s = 0) |
| `2E027000` | CGameCommonItemEntityModel | 1,743 | 4 (1,335), 5 (296), 6 (112) | Runtime entity: v0 `phyModel`/`visModel` NodeRefs; v3 strings `U01/U02`; then `Iso4 U03`, particle `U04`, actions `U05`, NodeRef `U06`, five strings `U07–U11`, `Iso4 U12`, `i32 U13`, `byte U14` |
| `2E01C000` | CGameVehicleModel | 14 | 3 | `phyModel`+`visModel` NodeRefs, `itemOccupantSlotModels[]`, v≥1 extra NodeRef array |
| `2E028000` | **`CGameCharacterModel`** | 2 (parse failure) | – | **Filename-derived identification (§10.2)** — class id taken from GBX.NET's name table; no parser for it. Payload of CharacterPilot: `i32 5,4,5`, then a `0xFF…` run (NodeRef nulls), `float 40.0`, `i32 0`, ending in `FACADE` (56 B of data) |

---

## 7. CPlugCrystal — collision shape + the desync cluster (NEW)

Fixed order in all 5,303 successful files:
`09051000, 09003003, 09003004, 09003005, 09003006, 09003007`

| Chunk id | Files | Versions | Content |
|---|---:|---|---|
| `09051000` | 5,242 | – | **Inherited chunk from `CPlugTreeGenerator`** (the base class of CPlugCrystal — hence the "odd" id in crystal context). One `uint`, value `1` in 100% of files |
| `09003003` v2 | 5,303 | 2 | **"materials"**: list of materials (name + optional CPlugMaterialUserInst) |
| `09003004` v1 | 5,303 | 1 | `byte[] U01` (length-prefixed blob) + i32 `U02` (v≥1). GBX.NET reads it as opaque data |
| `09003005` | 5,303 | 0–… | **"layers"**: `version`, `i32 count`, per layer `i32 layerType` + payload. Known types: Geometry, SubdivideSmooth, Translation, Rotation, Scale, Mirror, MoveToGround, Extrude, Subdivide, Chaos, Smooth, BorderTransition, Deformation, Cubes, Trigger, SpawnPosition, Light |
| `09003006` | 5,303 | 2 (3,805), 1 (763), 0 (735) | **"lightmap UVs"**: v0 = full Vec2 per vertex; v1 = compressed uint16 pair; v2 = + optimised index array |
| `09003007` v0 | 5,303 | 0 | **"smoothing groups"**: two int arrays |

### Why 153 files fail — 100 of them crystal-related

Error distribution (153 total):

| Error | Count | Analysis |
|---|---:|---|
| `Unknown unskippable chunk (??? 0x…) Previous chunk: CPlugCrystal 0x09003005` | 91 | The reader goes out of sync **immediately after** reading the layers chunk: a `2E…` id is read as `0x00000001/0x2/0xBF7FFFFC/0xC1B80000/0x413C8310…` (float/count values from the layer payload). = GBX.NET's `Layer.Read` **consumes too few bytes** for some layers. Evidence from the `FenceChickenWire0Curve01` dump: layer count **3** (working files: 2), payload 0x4409→0x41065 (248,924 B) |
| `Layer type -1082130436 is not supported` | 7 | Same cause, but the drift reaches the layer-type int: `-1082130436` = `0xBF7FFFFC` = float **-1.75** — clearly data being read as a type |
| `Unknown class ID: 0x0915C000` | 21 | = **`CPlugFxSystem`** (§10.1) |
| `Length cannot be negative` | 21 | garbage lengths = desync (probably partly the same family) |
| `Unknown class ID: 0x2E028000` | 2 | = `CGameCharacterModel` (§10.2) |
| `Length exceeded allowed maximum (…)` | 9 | garbage lengths (desync) |
| `LightmapCoord count mismatch` | 2 | `09003006` v0 counts unread UVs (count does not match layer geometry) |
| **Total** | **153** | of which **100 (65%) crystal-related** (91 desync + 7 layer type + 2 lightmap) |

**Root cause in one sentence:** some crystals in the corpus contain layer payloads that are newer/richer than what GBX.NET's `CPlugCrystal` layers read; because known chunks carry no length, such a partial read ends in chaos. This affects ~1.3% of all items (mainly City Scenery Set 4 / NationsConverter maps).

---

## 8. Other model/material chunks

| Chunk id | Class | Files | Content (from read code) |
|---|---|---:|---|
| `090BB000` | CPlugSolid2Model | 1,976 | Large header chunk: version (29–34), strings/ids `U01–U06`, arrays `U09/U10/U12` (e.g. parts/visual references), NodeRef `U14`, float `U15` |
| `090BB002` | CPlugSolid2Model | 1,976 | **"fake occlusion"**: `fileImageBytes` (Data blob) + `fakeOccProjs[]` |
| `09006001, 09006005, 09006009, 0900600B, 09006010` | CPlugVisual | 1,976 | visual construction: `U01` id/NodeRef, `subVisuals[]`, splits array, `morphCount` (v) |
| `0900600F` v6 | CPlugVisual | 1,976 | vertex data blob (`U04`) + counts |
| `0906A001` | CPlugVisualIndexed | 1,976 | index/morph continuation |
| `0902C002, 0902C004` | CPlugVisual3D | 1,976 | NodeRef `U01` and **tangents** (`Tangents1/2` byte arrays with counts) respectively |
| `09057000` | CPlugIndexBuffer | 143 | `Flags` (DataInt32) + `count` + `count× uint16` indices (old format) |
| `09057001` | CPlugIndexBuffer | 1,851 | index data (current format) |
| `090FD000` | CPlugMaterialUserInst | 1,976 | material: `materialName`, `model`, `baseTexture`, `surfacePhysicId` (+v≥10 `surfaceGameplayId`, v≥11 `isUsingGameMaterial`), `link`, tiling… |
| `090FD001` v5 | CPlugMaterialUserInst | 1,976 | atlas NodeRef, `tilingU/V` enum, `textureSizeInMeters`, v≥4 extra i32 |
| `090FD002` | CPlugMaterialUserInst | 1,976 | i32 `U01` |
| `090BA000` | CPlugSkel | 173 | skeleton version 18/19/20 + trees (Name, ParentIndex…) |
| `0900C003` | CPlugSurface | 404 | collision surface: v≥2 `surfVersion`, materials list, v<3 `byte[]`, uv/index arrays, occasional strings |
| `09079001, 09079007, 09079010, 09079011, 09079015, 09079016, 09079017` | CPlugMaterial | 2 | Fully embedded material: deviceMaterials, customMaterial NodeRef, shader NodeRef, arrays |
| `09079012, 09079013` | CPlugMaterial | 2 | **Ignore=true: always 4 zero bytes** |
| `09079019` | CPlugMaterial | 2 | **Ignore=true: always 8 zero bytes** |

---

## 9. Light chunks

| Chunk id | Class | Files | Content |
|---|---|---:|---|
| `0901D002` | CPlugLight | 2 | `flags` enum |
| `0901D003` v1 | CPlugLight | 34 | `imageAnim` NodeRef, `animPeriodMin/Max`, v≥1 `animTimerName` |
| `0901D004` | CPlugLight | 32 | `light` NodeRef + 5× i32 |
| `0400100A` v0 | GxLight | 34 | colour (Vec3), flags enum, intensity/diffuse/shadow/flare intensities, shadowRGB |
| `04002008, 04002009, 0400200A` | GxLightBall | 34 | radius parameters (radius, specular, shadow, flare, emitting…), attenuation floats, ambientRGB + bare floats `U01` |
| `04003004` | GxLightPoint | 34 | `flareSize`, `flareBiasZ` |
| `0400B003` v1 | GxLightSpot | 1 | angles (inner/outer/flare/shadow), falloff, v≥1 extra bytes |
| `090F9000` v1 | CPlugLightUserModel | 177 | `U01` i32 + colour, intensity, distance, emission parameters, spot angles |

---

## 10. New findings (not yet documented by the community)

1. **`0x0915C000` = `CPlugFxSystem`** — 21 items (the ShadocAlex FogVolumetric series) fail because GBX.NET has the name in its table but no parser class. Our dump of the embedded class shows: `i32` fields, `float 2.0`, strings **"Fogger16m"** (twice, presumably FX/emitter preset names) and an embedded `CPlugParticleEmitterModel` (`090B3000`).
2. **`0x2E028000` = `CGameCharacterModel`** — 2 items (CharacterPilot). The same "name known, parser missing" situation. Useful for tooling: GBX.NET's "unknown class name" error is misleading; the name can be looked up in `ClassManager`.
3. **Icon variants in `2E001004`**: besides ARGB pixels the chunk supports a **WebP** path (both width and height with bit `0x8000`, then i16 + length + blob). Arcane: 6,341 items have the chunk, 5,801 have visible pixels → **~540 items (7.4%) presumably use the WebP path** (Icon==null but chunk present).
4. **`2E020003` fully decoded** (§5): four payloads, version 3, `X∈{6,8,10}`, with a "1x1" string variant. Either inline (`2E020003`) *or* external (`2E020005`) — never both, and `2E020005` correlates with iconless special items.
5. **Constant-zero chunks**: `2E026001` (5,306×), `09079012/13/19` are in *all* files exactly 8 and 4 zeroes respectively — no exception. They mark a structure variant without content. (`2E020004` was wrongly listed here too; the vanilla corpus disproved it — it is a real, often non-empty list, see §12.3.)
6. **Open puzzles**: `2E001012` (4 ints; only `U03` varies: 0/1/2/3), `2E00201A` (bare NodeRef), `2E002023`+`2E002024` (always `0,0` and empty Vec2 array respectively — 2,920 items), `2E002025/26/27` are present in 100% of items but GBX.NET gives them generic fields. Candidates for follow-up research.
7. **Crystal layer desync** (§7): 100 items with exact location (offsets, layer counts) — ready for a follow-up parser fix.

## 11. Reproducibility / evidence

- Scanner: [Tests/ItemChunkIndex](../Tests/ItemChunkIndex/Program.cs) — `dotnet build Tests/ItemChunkIndex`, then
  `dotnet run --no-build --project Tests/ItemChunkIndex -- "<corpus>" docs/evidence/item-chunk-index --deep=2E020003,2E020004,2E026001`
- Raw dumps: [Tests/PathFollowerDiag](../Tests/PathFollowerDiag/Program.cs) — `--raw --dump=<hexid>:<bytes>` prints a hex dump after every match.
- Payload sampling: the scanner keeps one sample per **distinct payload size** per chunk id (up to 4), so structurally different variants of the same chunk are all captured instead of just the first two files.
- Vanilla corpus evidence: [evidence/vanilla-item-chunk-index/](evidence/vanilla-item-chunk-index/) — see §12.
- Evidence files (generated, in this repo):
  - [summary.tsv](evidence/item-chunk-index/summary.tsv) — all 86 chunk ids with frequencies, versions, positions, field values
  - [payload.tsv](evidence/item-chunk-index/payload.tsv) / [deep.tsv](evidence/item-chunk-index/deep.tsv) — raw payloads of the 6 undecoded chunks
  - [sequences.tsv](evidence/item-chunk-index/sequences.tsv) — exact chunk order per class
  - [features.tsv](evidence/item-chunk-index/features.tsv) — chunk × item property correlation (lift)
  - [items.tsv](evidence/item-chunk-index/items.tsv) — per-file features + placement sequence
  - [failures.txt](evidence/item-chunk-index/failures.txt) — 153 parse errors with category
  - [samples/](evidence/item-chunk-index/samples/) — binary samples of raw payloads

## 12. Vanilla GameData corpus (Openplanet extract) — cross-check

**Corpus:** the full game data extracted with Openplanet to `C:\Users\PC\OpenplanetNext\Extract\GameData` — **1,354 `.Item.Gbx` files**: Stadium/Items 632, RedIsland/Items/Stadium 595, RedIsland/Items/Vegetation 53, BlueBay/Items/Vegetation 39, Stadium/Items/Theme 29, Vehicles/Items/Cars 4, ShootMania/Items/Characters 1, Stadium256 DynaObjects 1.

**Result: 1,349 parsed, 5 failed, 33 distinct chunk ids — and all 33 are a subset of the 86 ids from the custom corpus.** The game's own items therefore add **no new chunk ids**; the 53 extra ids in the custom corpus come from items produced by NadeoImporter/downloaded content. What vanilla *does* add is corrections (the payload shapes) and two classes that only occur in game items.

Evidence: [summary.tsv](evidence/vanilla-item-chunk-index/summary.tsv), [sequences.tsv](evidence/vanilla-item-chunk-index/sequences.tsv), [items.tsv](evidence/vanilla-item-chunk-index/items.tsv), [features.tsv](evidence/vanilla-item-chunk-index/features.tsv), [failures.txt](evidence/vanilla-item-chunk-index/failures.txt), [payload.tsv](evidence/vanilla-item-chunk-index/payload.tsv) and [samples/](evidence/vanilla-item-chunk-index/samples/).

### 12.1 Structural difference: external vs inline models

| | Custom corpus (Documents\Trackmania) | Vanilla game items |
|---|---|---|
| Chunks per item | 25–29 (mostly 26 or 29) | 25–28 |
| Embedded Plug tree (`CPlugCrystal`, `CPlugSolid2Model`, `CPlugVisual*`, `CPlugMaterial*`, `CPlugLight*`) | present inline | **absent** — referenced externally via NodeRefs in `2E002019` |
| Entity models `2E026000/2E026001/2E027000` | present inline (5,306 / 1,743) | **absent** |
| `2E00100E` (icon render), `2E002021/23/24` (group elements), `2E020003` (inline placement class) | present | absent |
| `2E001002`, `2E020004` non-empty, `2E020005` | rare / always empty | `2E020005` in all 495 placement items, `2E020004` often non-empty |

Consequence for tooling: chunk/class ownership and order must be handled for **both representations**. A tool that assumes the visual/entity model is embedded inline will find nothing in vanilla items, and vice versa.

### 12.2 Classes that only show up in game items (from the 5 failures)

| File | Error | Meaning |
|---|---|---|
| `Stadium/Items/Podium.Item.Gbx`, `RedIsland/Items/Stadium/Podium.Item.Gbx` | `Unable to cast CGameItemModel to CPlugMediaClipList` | an item file embedding a **`CPlugMediaClipList` (chunk `09189000`)** where the loader expected one — a **class not seen in the custom corpus** |
| `Stadium/Items/Theme/DesertGateGameplay.Item.Gbx`, `…/RallyGateGameplay.Item.Gbx` | `Known unskippable chunk (CPlugGameSkinAndFolder 0x0915D000) cannot be processed` | **new class `0x0915D000` = `CPlugGameSkinAndFolder`** — GBX.NET knows the name but has no reader for its chunk |
| `ShootMania/Items/Characters/CharacterPilot.Item.gbx` | `Unknown class ID: 0x2E028000` | `CGameCharacterModel` (same situation as §10.2) |

### 12.3 `2E020004` decoded (corrects §5 and §10.5)

Vanilla items with a placement param (495: gates, inflatables, vegetation, cars, one dyna object) all use the external placement-class sequence `2E020000, 2E020001, 2E020004, 2E020005` — that is seq. C of §5. There, `2E020004` is **not** a constant 8-zero-byte payload:

| Size | `version` | `count` | Decoded entries (position, rotation in degrees) |
|---:|---:|---:|---|
| 8 B | 0 | 0 | — (empty list) |
| 56 B | 0 | 2 | (−8, 8, 0) / (−90, 0, 0) and (8, 8, 0) / (90, 0, 0) |
| 80 B | 0 | 3 | (4, 0, 0)/(90, 0, 0), (−4, 0, 0)/(90, 0, 0), (4, 0, 0)/(0, 0, 0) |
| 128 B | 0 | 5 | (0, 2, −2)/(180, 0, 0), (2, 2, 0)/(90, 0, 0), (−2, 2, 0)/(−90, 0, 0), (0, 4, 0)/(180, −90, 0), (0, 0, 0)/(0, 90, 0) |
| 584 B (max) | 0 | 24 | not decoded byte-for-byte; `584 = 8 + 24 × 24` exactly |

So the layout is `i32 version (always 0) + i32 count + count × (Vec3 position, Vec3 rotation in degrees)`, **24 bytes per entry**, and it holds a list of **alternative placement positions/rotations** (pivot-like offsets in metres plus angles). The custom corpus has `count = 0` in all 4,444 files — that is why §5 saw a constant payload; the vanilla corpus shows the chunk is a real, often non-empty list.

### 12.4 Other enrichments from the vanilla corpus

- **`2E00201F` (waypoint): vanilla uses v13 (694) and v12 (655)** — v13 is thus a normal, current version in game-authored items even though GBX.NET has no branch for it (§4). Only 1 of 28 Theme items uses v13 there; the split is mostly by content family, not by theme.
- **`2E001012`**: present in 1,349/1,349 vanilla items (`U01=0`, `U02=1`, `U03∈{0,1,2,3}`, `U04∈{0, 0.639f}`). Decoded: `1059296641 = 0x3F239581 = 0.639f` — so `U04` is (at least sometimes) a float, not just 0. It is not vehicle-specific: the 4 Cars have `U04=0`. In gates `U03=2` or `0`.
- **Placement representation**: “external placement class” (§5, seq. C) is not limited to the special item types — it is the form used by game-authored items in general (gates, inflatables, vegetation, cars).
- **Item-type and waypoint spread (vanilla)**: Ornament 1,345 / Vehicle 4; waypoint None 1,269, Checkpoint 26, Start 18, Finish 18, StartFinish 18 — i.e. the waypoint chunk is present in every item, only its `waypointType` differs.

### 12.5 Limitations of this cross-check

- The extract contains the GameData tree, not every item the game can load (no workshop/NationsConverter content).
- 5 files could not be parsed at all; their findings come from the error message only.
- The 584-byte `2E020004` payload is inferred from the exact size arithmetic (`8 + 24 × 24`); only the 8/56/80/128 B samples were decoded byte-for-byte.
- Same as everywhere in this document: not verified in-game.

## 13. Limitations

- Corpus = what is in `Documents\Trackmania` (mostly Ornament items, 7,285/7,301) plus the vanilla GameData extract (§12). Vehicle/character items stay thinly represented (4 cars, 1 character).
- The semantics of `U01…` fields are inferred from read code + correlations, not from Nadeo documentation; interpretations marked "presumably" are flagged as such.
- The 153 failed files could not be walked completely; their analysis is based on raw dumps (§7).
- File analysis and unit-level round trips only. In-game verification is done separately with hand-made test items (§14.4); findings in this document are still not confirmed by the game engine itself.

---

## 14. Modification surface: what the studio writes

This section is the contract the chunk reference turns into: which bytes the studio is allowed to change, which API performs the change, and what it refuses to do. Every entry here is covered by a console suite in `Tests/` (see §11 for how to run them).

### 14.1 Editable records

| Target | Chunk / field | API | Rule |
|---|---|---|---|
| Collector name | `2E00100C` `name` | page edit | Free text; no struct change. |
| Item flags, cube size, pivot snap | `2E020000` `Flags`, `CubeSize`, `PivotSnapDistance` | [`ItemEdits`](../Models/ItemEdits.cs) | Field-by-field; unknown flag bits are preserved. |
| Placement pivots | `2E020000` pivot positions/rotations | `ItemEdits.ReadPivots` / `AddPivot` / `EditPivot` / `EditPivotPosition` / `RemovePivot` | Never invents an origin pivot and never repairs absent or mismatched rotation arrays; a coordinate-only edit keeps every rotation byte. |
| Placement alternatives | `2E020004` | [`ItemPlacementAlternatives`](../Models/ItemPlacementAlternatives.cs) | Whole-list or per-entry edit; refuses a payload whose version, count or length does not match the documented layout (§12.3). |
| Waypoint type | `2E00201F` `waypointType` | page edit | Enum only. |
| Group motion | motion chunks, up to 10 segments | [`ItemMotion`](../Models/ItemMotion.cs) | Segment count is capped at the authored maximum, not at the editor's dropdown. |
| Light, prefab and variant records | `090F9000`, `CPlugPrefab` entries, `NPlugItem_SVariantList` | `ItemEdits.EditLight` / `EditOwnerPosition` / `EditLightTransform` / `EditVariant` | Explicitly selected records only — no graph discovery, no cloning. Tag patches treat `null` as "remove" and preserve every unmentioned key. |
| Material UV0 transform | `090FD000` / visual chunks | [`ItemMaterialEdits`](../Models/ItemMaterialEdits.cs) | Scoped to one visual path in one document ordinal. |

### 14.2 Refusal over guessing

The single rule behind every row above: **an edit that cannot be expressed in the documented layout is rejected, and the bytes stay untouched.** In practice that means unreadable or unexpected payloads (wrong version, wrong count, wrong length, non-finite floats, out-of-range index) raise an exception and produce no write. `Tests/ItemEdits` and `Tests/PlacementEdits` assert both halves of this: the successful byte-for-byte round trip *and* the refusal.

### 14.3 Read-only by design

Collision (`CPlugCrystal`, `CPlugSurface`), geometry and index buffers, icons (`2E001004`, including the undecoded WebP path of §10.3) and the undecoded chunks of §10.6 and §12.2 are **never rewritten**. They are round-tripped verbatim: a file that is loaded and saved with no edit must come back byte-identical, which is what `Tests/ParserSentinels`, `Tests/ParserSurfaces` and `Tests/VariantExport` check.

One consequence worth stating explicitly, because it is easy to get wrong: opaque payloads survive a load/save without any special flag. GBX.NET always stores the raw bytes for `Ignore`-marked chunks and for unknown ids; `GbxReadSettings.SafeSkippableChunks` only changes the *fallback* when a *known* chunk fails to parse. `Tests/PlacementEdits` pins this with an item-level round trip through both settings, so the `2E020004` list cannot silently disappear from an export.

### 14.4 In-game test items

Hand-made items used to check behaviour in Trackmania are dropped into `C:\Users\PC\Documents\Trackmania\Items\02. Freebuff RE Items`. The folder is a plain Items subfolder: anything written there shows up in the in-game item browser without an import step, which makes it the shortest possible loop from a byte-level edit to engine feedback. Items there are test artefacts, not evidence for §1–§12.

---

## 15. Roadmap

Ordered by the ratio of confirmed knowledge to remaining work. "Confirmed" means a console suite exists and passes; "engine" means the game itself has been checked.

### 15.1 Correctness first (no new format knowledge needed)

1. **Crystal layer desync** — 100 items with exact offsets and layer counts (§10.7) are ready to be turned into a parser fix plus a regression list. This is the largest group of currently unparsable files.
2. **Known name, missing parser** — `CPlugFxSystem` (`0915C000`), `CGameCharacterModel` (`2E028000`), `CPlugMediaClipList` (`09189000`) and `CPlugGameSkinAndFolder` (`0915D000`) are named in `ClassManager` but have no reader, so the affected files fail as "unknown". A tooling-side fix (report the known name, treat the class as opaque) would turn 5 vanilla plus 21 custom failures into pass-throughs.
3. **`2E00201F` v13** — a current version in game-authored items with no GBX.NET branch (§12.4). Needs a read path before waypoint editing can be trusted on those files.

### 15.2 Capability

4. **Icon pipeline** — ~7.4% of items appear to use the WebP path of `2E001004` (§10.3), which nothing in the studio decodes or writes today. Requires samples first, then a reader.
5. **Placement alternatives as a full editor** — `2E020004` is written and tested, but the UI still treats entries as numbers rather than as pickable candidate placements. The 584-byte/24-entry maximum (§12.3) is the practical ceiling on what the game itself authors.
6. **Motion beyond ten segments** — the current cap matches the authored dropdown. Whether the engine accepts more is an open question (`Tests/ItemMotion`, `Tests/KinematicReference`).
7. **Undecoded remainder** — `2E001012`, `2E00201A`, `2E002023/24/25/26/27` (§10.6) are present in every item and give the studio the least to work with.

### 15.3 Verification

8. **Engine checks** — the items in §14.4 exist to convert "round-trips in the console suite" into "loads and behaves in the game". Until that is done for a given feature, this document keeps calling it unverified.
9. **Test CI** — every console suite now runs on push (`.github/workflows/tests.yml` via [`Tests/run-all.sh`](../Tests/run-all.sh)). `Tests/Browser` and `Tests/Openplanet` are excluded: they need a browser and the game respectively.
10. **Corpus refresh** — the reference is a snapshot (2026-10-08) of what is in `Documents\Trackmania` plus one GameData extract. New Nadeo content or a fresh workshop download can change the id list; re-running §11 restores it.
