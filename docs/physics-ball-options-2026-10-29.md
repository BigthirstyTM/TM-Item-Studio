# Physics ball options for Trackmania 2020

## Current proof of concept

The first self-authored candidate is a bare dynamic item:

```text
CGameItemModel.ItemTypeE = 0x0C
EntityModel              = CPlugDynaObjectModel
IsStatic                 = false
DynamizeOnSpawn          = true
Mass                     = 5
BreakSpeedKmh            = 200
DynaShape                = CPlugSurface Mesh (162 vertices / 320 triangles)
StaticShape              = the same collision mesh
SurfaceId                = GolfBall
No prefab and no kinematic constraint
```

The visual mesh and collision mesh were taken from the existing
`RoyalPark/Inflatable/Inflatable_Beachball.Item.Gbx`, then the prefab wrapper
was removed. GBX.NET generated and reparsed the result successfully. The
Trackmania FID loader also accepted the resulting file and reported a loaded
`CGameItemModel`:

```text
Documents/Trackmania/Items/TMIS_PhysicsBall_Probe/Ball_GolfBall.item.Gbx
SHA-256 CA41E1160E4BD5B6D30AED404FFDAF2CFBC5CD3740CB324198114FDC68A7BBE1
```

This proves file construction and native loading. It does not prove gameplay.

## The current blocker

The normal `MapEditor` scene has `NHmsCollision_SMgr`, but it does not create
`NSceneDestructiblePhy_SMgr` or `NSceneDestructibleVis_SMgr`. The free-body path
needs those components to generate a Dyna slot, create the Havok body from
`DynaShape`, apply impulses, and render the body in play. The result is that a
valid `0x0C` item can load but cannot yet be observed as a movable, collidable
object in the standard editor/local-race scene.

There is a second independent blocker: the editor's map-embed type mask omits
`ItemTypeE=0x0C`, so saving a map with the item requires a DEV-only mask change
(`0x683e` to `0x783e`) or an external item file beside the map. The safe
GBX.NET writer can add an embedded ZIP entry and a real anchor, but it cannot
reproduce the editor's internal registration of a newly placed item: even an
ordinary static control item written through that path is absent from
`GetItems` until the map has gone through a native editor save.

The current E++ inventory cache also does not return the fixture, and its
path-based `PlaceItem` call reports `item not found`. Direct native FID preload
does work, so this is an inventory/placement discovery gap rather than a GBX
parse failure.

## Tests performed on a real map

The map writer was extended with two explicit probe switches:
`--embed-item` adds the serialized item to the map's embedded ZIP, and
`--physics-layout` puts one anchor at `(768,128,470)` inside the map bounds.
The source map is never overwritten.

The external-FID copy
`Documents/Trackmania/Maps/My Maps/AutoSave20.Map_PhysicsBallProbe.Map.Gbx`
round-trips in GBX.NET, but Trackmania refuses to load it with a missing-item
dialog. This is a packaging/reference problem, not evidence that the item body
is invalid.

The embedded copy
`Documents/Trackmania/Maps/My Maps/AutoSave20.Map_PhysicsBallCenterEmbeddedProbe.Map.Gbx`
also round-trips and opens natively. Its embedded ZIP contains one exact
`TMIS_Physics_Ball_GolfBall` item file (the GBX.NET round-trip bytes hash to
`E9782F0D356464BE54E2E6A2E06ABDA9D6536C8059D40435673331CB18EBD6BE`). The raw
map contains 14 anchors (13 existing
items plus the ball), while Trackmania's `GetMapInfo`/`GetItems` reports only the
13 existing items. The ball is therefore not instantiated as a normal map item.
`DevSceneComponents` shows `NHmsCollision_SMgr`, but no
`NSceneDestructiblePhy_SMgr` or `NSceneDestructibleVis_SMgr`.

This is the useful boundary for `.Map.Gbx`: embedding fixes the missing-file
dialog, but changing anchors or ZIP contents does not create the runtime
destructible managers and does not make a `0x0C` item physically active.

### Native-anchor A/B (29 October 2026)

To rule out a malformed hand-written anchor, `Tests/ProbeAssets` now has an
opt-in `--native-anchor` switch. It copies the opaque 9-byte `0x03101005`
payload from an anchor that Trackmania itself wrote, while retaining the
normal typed `0x03101002`/`0x03101004` chunks. The source map is read-only and
the output is written under a new `Maps/My Maps` name.

The clean base map
`TMIS_NativePrimer_NativeOnly_20261021.Map.Gbx` was used for four copies:

| Fixture | Item graph | Native editor result |
|---|---|---|
| `...Map_NativeAnchorGolfBallClean.Map.Gbx` | bare Dyna, `EntitySpawner`, `DynamizeOnSpawn=true` | opens; `nbItems=1` (only the original `Flag16m`) |
| `...Map_NativeAnchorOrnamentClean.Map.Gbx` | bare Dyna, `Ornament`, `DynamizeOnSpawn=true` | opens; `nbItems=1` |
| `...Map_NativeAnchorStaticUntilHitClean.Map.Gbx` | bare Dyna, `EntitySpawner`, `DynamizeOnSpawn=false` | opens; `nbItems=1` |
| `...Map_NativeAnchorPrefabDynamicClean.Map.Gbx` | `CPlugPrefab` containing a non-kinematic Dyna body | opens; `nbItems=1` |

All four maps parse and round-trip with two anchored objects and one embedded
item model. The extra anchor is absent from `GetItems`, so adding the native
anchor chunk, changing `ItemTypeE`, disabling `DynamizeOnSpawn`, or wrapping
the body in a prefab does not make a free Dyna object enter the normal editor
item scene. The prefab wrapper was made from a copy of the same ball and
reparsed with `IsStatic=false`, `DynamizeOnSpawn=true`, both shapes present,
and `IsKinematic=false`; it did not change the result.

There is an important control: the same GBX.NET map writer, embedding and
anchor code with `Test Exported items/CustomItem_Static.Item.Gbx` also opened
with `nbItems=1`, so this hand-written map path does not by itself register a
new ordinary item with the editor either. A native-saved map containing an
ordinary static item does show that item. The result above therefore rules out
the anchor-chunk explanation within this safe writer probe, but it does not
replace a direct native-editor placement test; that test would require
preloading the custom FID and carries a crash risk for a bare Dyna body.

The loaded scene component list contains `NHmsCollision_SMgr`,
`NHmsMgrInstDyna_SMgr`, `NHmsMgrInstDyna2_SMgr`, and `NSceneDynaVis_SMgr`, but
no `NSceneDestructiblePhy_SMgr` or `NSceneDestructibleVis_SMgr`. This is a
runtime scene bootstrap boundary, not a missing map anchor or missing ZIP
entry. After fixing the writer to preserve the source ZIP, the variant
`AutoSave20.Map_NativeAnchorGolfBallPreserveZip.Map.Gbx` opened without a
missing-item dialog; its raw GBX has 15 anchors and two embedded item models,
while the native editor reports 13 normal items. The original map's 13
ordinary items remain, the old unsupported ball remains absent, and the new
ball is also absent. `PlayMap` could not enter Race for these fixture maps (it
returned to Menu without a map), so no claim is made about a Race-only manager
path.

The generated native-anchor golf-ball map has SHA-256
`83DEFA028775A722C10733B6FA779468C3691532E1D58A45FD7BBD319AA94E87` and the
reparsed embedded item has SHA-256
`E9782F0D356464BE54E2E6A2E06ABDA9D6536C8059D40435673331CB18EBD6BE`.

Generated disposable artifacts:

```text
AutoSave20.Map_PhysicsBallProbe.Map.Gbx
  SHA-256 82FE568A757A3062B0AC14FDF0562FF05991DE746FF906A72D24C3E413DDB514
AutoSave20.Map_PhysicsBallEmbeddedProbe.Map.Gbx
  SHA-256 1C8AC336CDF95C3228843B927AFD0D8A7DDA88FADD29D4423F4D3F453984788B
AutoSave20.Map_PhysicsBallCenterEmbeddedProbe.Map.Gbx
  SHA-256 36C05D3BEED46648F3A98A7E2D91FDD5FA91425F73142A049203CB1491DB24C1
AutoSave20.Map_PhysicsBallStaticCenterEmbeddedProbe.Map.Gbx
  SHA-256 C846FFF7AE00FC703357E5E32150CCDA2A2F06C2C34C1C618AC0298964A17827
```

For the planned `DynamizeOnSpawn` A/B, the second fixture is
`Documents/Trackmania/Items/TMIS_PhysicsBall_Probe/Ball_StaticUntilHit.item.Gbx`
(`DynamizeOnSpawn=false`, SHA-256
`EF7B93043875F59F580F88F45F69189C7B571390A4B80195B0D34F7FAFEEED79`).
It is ready for the same scene-component experiment; it should not be called
"breaks on contact" until a live body and contact callback are observed. Its
embedded-map copy was also opened natively; the result was again 13 visible
items and only `NHmsCollision_SMgr`, so changing `DynamizeOnSpawn` cannot be
evaluated before the destructible scene bootstrap exists.

## Options to try

| Option | What we build | Expected result | Cost / risk |
|---|---|---|---|
| **A. Native free rigid body** | The candidate above, plus a DEV-only scene patch that enables the destructible PHY/VIS components; test `GolfBall`, `Rubber`, and `Plastic` surfaces, mass, and break speed. | Real Havok body that falls, rolls, and receives car impulses. | Highest value; runtime patch is version-sensitive and can crash the game. Also needs the `0x0C` embed fix for self-contained maps. |
| **B. Break-on-contact Dyna** | `IsStatic=false`, `DynamizeOnSpawn=false`, a real `DynaShape`, and a chosen `BreakSpeedKmh`. | Starts as a static obstacle and becomes a free body after a sufficiently hard hit. | Uses the same missing scene components; easier to compare once A works. |
| **C. Kinematic ball** | `ItemTypeE=1` prefab with a Dyna body, `IsKinematic=true`, and a root `NPlugDyna_SKinematicConstraint`. | A visible ball that rolls/spins along a predefined timeline. Existing Crown/pusher items show this path works in normal scenes. | Not pushable physics; the car cannot impart an arbitrary impulse. Best fallback for a moving visual. |
| **D. Physical `LocAnim` experiment** | Add `CPlugAnimLocSimple` and set `LocAnimIsPhysical=true` on a Dyna body. | May make a one-axis periodic spin/bob participate in physics. | Low confidence; only one axis and period-driven, no car response. Treat as a diagnostic probe. |
| **E. Openplanet-simulated ball** | Keep a normal visible static item and let a DEV plugin integrate position/velocity from car state, then update the scene transform. | A convincing scripted push/roll demo in a controlled plugin or game mode. | Not native item collision; private scene APIs and lifecycle hooks are version-sensitive. |
| **F. Segmented kinematic approximation** | Several independent root kinematic bodies hand over at path corners. | Collision-looking motion over a designed path. | Does not produce a single freely responding ball; unnecessary if A succeeds. |
| **G. `CGameObjectPhyModel` route** | Author Hit/Move/Trigger shape DataRefs instead of a bare Dyna. | Possible special-object physics if all mandatory DataRefs resolve. | Not the proven path for a placeable `0x0C` item; do not use as the first prototype. |

## Recommended order

1. Implement a DEV-only scene-component experiment for **A** and keep the
   current `Ball_GolfBall.item.Gbx` as the fixture.
2. Add a second fixture with `DynamizeOnSpawn=false` for **B**.
3. Run an A/B contact map: flat ground, ramp, wall, one car approach, and a
   reset. Record visibility, fall, roll, impulse, car response, and whether the
   body survives map reload.
4. Only after a native body works, expose surface material, mass, and break
   speed in TM Item Studio. Until then those fields should remain an
   experiment-only export profile.
5. Keep **C** as the production-safe fallback when a map must work without a
   runtime patch.

The next decisive experiment is a DEV-only runtime patch at the scene-kind
gate in `NGameApp_GameSceneCreate`, or an equivalent post-scene bootstrap that
registers the destructible PHY/VIS managers. The candidate byte pattern is
version-specific (`Trackmania.exe` currently contains `44 39 74 24 78 75 14`
near VA `0x140DB4E70`); it must be found by a unique pattern at runtime and
guarded by a reversible toggle. A successful patch is only proven when the
manager dump shows both destructible components, a Dyna slot/body is created,
and a play test records position, velocity, rotation and contact after a car
hit.

## Evidence boundary

- **Round-tripped:** the generated GBX preserves the `0x0C` bare-Dyna graph,
  `DynamizeOnSpawn`, mass, break speed, sphere-like collision mesh, and
  `GolfBall` material id.
- **Native-loaded:** Trackmania's FID loader returns a loaded `CGameItemModel`
  for the fixture.
- **Not yet playable:** no body/impulse claim is made until the destructible
  scene components are enabled and a real play test observes the contact.
