# Collision and trigger capabilities

This documents what the typed scene model can honestly say about collision and trigger
data in `.Item.Gbx` files, and what the NadeoImporter conventions actually persist.
It is an inspection capability inventory: there is deliberately no collision on/off
toggle, no geometry regeneration, and no enable/disable proposal, because no proven
native source/target pair for such a toggle exists in this codebase (see below).

Fixture provenance: all evidence from synthetic fixtures in `Tests/ItemScene` plus the
bundled GBX.NET 2.4.4 development build (`lib/GBX.NET.dll`, pinned source
`15dba8bf`), Editor++ runtime research, and Trackmania RE notes listed at the end.
No game assets and no game-behavior claims.

## Classification model

`ItemSceneCollision` records carry a `Source` (which native slot the record was
classified from) plus a `State` (`Present`, `Absent`, `Unresolved` for external,
`Unsupported`, `Invalid`, `Cycle`). "Generated" is a source, not a state:

| `ItemSceneCollisionSource` | Native slot | Typical states |
|---|---|---|
| `StaticObjectShape` | `CPlugStaticObjectModel.Shape` (`CPlugSurface`, skipped by the serializer when `IsMeshCollidable` is set) | present / absent / external / unsupported |
| `GeneratedMeshCollision` | `CPlugStaticObjectModel.IsMeshCollidable` — the engine regenerates a shape from the mesh at load; nothing is serialized to inspect | unsupported |
| `DynamicObjectShape` | `CPlugDynaObjectModel.DynaShape` (post-destruction hull) | present / absent / external |
| `DynamicObjectStaticShape` | `CPlugDynaObjectModel.StaticShape` (pre-destruction hull) | present / absent / external |
| `CommonItemTrigger` | `CGameCommonItemEntityModel.TriggerShape` | present (typed `CPlugSurface`) / absent / unsupported (other node) |
| `ItemPhyModel` | `CGameItemModel.PhyModelCustom` reference (external file) | unresolved |
| `GameObjectHitShape` / `GameObjectMoveShape` / `GameObjectTriggerShape` | `CGameObjectPhyModel.HitShapeFid` / `.MoveShapeFid` / `.TriggerShapeFid` | present / absent / external / named (unresolved) |
| `SurfaceSlot` | a plain `CPlugSurface` reference such as a tree surface or a direct visit | mesh states |

How mesh collision connects to the static object: `CPlugStaticObjectModel` (body
version 3) always serializes `Mesh`, then `IsMeshCollidable`; only when that flag is
clear does it serialize `Shape` as a `CPlugSurface`. So an imported item's concrete
collision is `StaticObject.Shape`, and flag-set items carry no shape bytes at all —
every loading client regenerates it. The traversal records both branches explicitly
instead of inferring one from the other.

Where `PhyModel` fits: the modern alternative layout keeps
`CGameItemModel.PhyModelCustom` as a `CGameObjectPhyModel` whose Hit/Move/Trigger
shapes are separate surface references (external fids or inline nodes). In the bundled
serializer, `CGameCommonItemEntityModel.PhyModel`/`VisModel` are gated to body
version exactly 0 (the chunk DSL's `v0=` is an equality test), so they never coexist
with the version-4+ `StaticObject`/`TriggerShape` fields — modern items reach a phy
model only through `PhyModelCustom`. The traversal inventories all three fid slots
without invoking the resolving getters (the `*Fid` properties resolve external files;
the node is only read when no file reference is set, and external fids are inventoried
with an `external-reference` diagnostic like every other external slot) and counts
`Triggers` (`CPlugTriggerAction[]`) without interpreting them. From chunk `2E006001`
version 11 the serializer writes a shape *name* string instead of a node ref whenever
that name is non-empty; such a slot is a reference the traversal cannot resolve, so it
is classified `Unresolved` (with the name reported) rather than absent.

## Do NadeoImporter conventions persist as independent native fields?

### `notcollidable` — negative result

There is **no independent native "collision enabled" boolean** serialized on imported
items, and none is exposed by the bundled GBX.NET 2.4.4 public API. The convention
materializes in one of three distributed forms:

1. **Per-triangle-set material ids inside the collision surface.** `CPlugSurface`
   (chunk `0900C003`) serializes a `Materials` vector (`SurfMaterial`: either a
   `CPlugMaterial` node reference or a `SurfaceId` of the `MaterialId` enum) plus
   counted `SurfaceIds` vectors, and every triangle carries a `SurfaceIndex` selecting
   the material row. `MaterialId.NotCollidable` is enum value 28 (file-level enum in
   the bundled serializer; "Phys 28 = NoCollision" in the RE notes). An item is
   drive-through because *its triangle groups point at NotCollidable*, not because of
   an item-level switch.
2. **Shape absence.** A decor-only import serializes `IsMeshCollidable = false` and no
   `Shape` node at all.
3. **Mesh-collidable generation.** The flag-set layout defers collision to load-time
   generation from the visual mesh.

Consequently an "enable/disable collision" toggle has no single native field to flip:
flipping it would mean rewriting every triangle group's material id (or
adding/removing/regenerating a whole surface), which is authoring work, not a
capability bit. The stock item editor and Editor++ implement the same conclusion at
runtime — Editor++'s "make non-collidable" walks `Shape.MaterialIds[i].PhysicId`
(Openplanet runtime API names) and calls `UpdateSurfMaterialIdsFromMaterialIndexs()`
afterwards, i.e. a per-triangle-set rewrite plus a runtime recompute, not a flag
write. The importer-side convention is equally material-scoped: NadeoImporter's
material library stores a physics id byte per material, so "notcollidable" enters the
file through materials, never as an item flag. The bundled public API exposes
`CPlugSurface.Materials` and `SurfaceId` for inspection; it does not expose the
physics id of a surface's referenced `CPlugMaterial` node (a `SurfaceId` exists on
`CPlugMaterial` chunk `0x00E`, but its exact native mapping is unverified and not read
by this traversal).

### `trigger` — a persisted field, but not an importer-authored or independently editable one here

`CGameCommonItemEntityModel.TriggerShape` **is** an independently serialized native
field (chunk `2E027000`, serialized from body version 2, alongside an `iso4` transform
that the bundled public API does not expose). So trigger presence persists and is
classified (`CommonItemTrigger`: present/absent; unsupported when the slot holds a
non-surface node). Two hard limits remain:

- The ManiaPlanet-era importer could author `<TriggerShape>` (static and DynaObject
  AABB triggers); the current TM2020 NadeoImporter authors `Type="StaticObject"` items
  only and produces self-contained files, so nothing in the current import path
  creates this field. Items carrying it come from older tools or the in-game editors.
- The bundled API types the slot as `CMwNod` (untyped) and keeps its transform
  private, so the traversal reports presence plus the `trigger-transform` (Unsupported)
  diagnostic and does not interpret placement or semantics. `CGameObjectPhyModel`
  adds `TriggerShapeFid` and `Triggers` (`CPlugTriggerAction[]`) as further
  independent fields; they are inventoried (and counted) without interpretation.

No verified native source/target pair exists for toggling trigger behavior either;
none is proposed.

## Tests

`DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ItemScene -m:2` covers every
classification above with synthetic fixtures: present/absent/external static shapes,
generated mesh collision, present/absent/non-surface triggers, dynamic shape slots,
phy-model fid slots with a resolution trap (including a name-referenced shape and the
counted trigger actions), plain surface visits, and a save/reparse check proving the
inventory is read-only (serialized bytes unchanged by traversal; identical
classification after reparse).

## Evidence index

- Bundled serializer source (pinned `15dba8bf`, includes the GmSurf support and C003
  material-ID vector corrections): `CPlugStaticObjectModel.chunkl`,
  `CGameCommonItemEntityModel.chunkl`, `CGameObjectPhyModel.cs`,
  `CPlugSurface.cs`/`.chunkl` (`SurfMaterial`, `MaterialId` enum, per-triangle
  `SurfaceIndex`).
- Editor++ `src/Components/ItemEditor/IE_AdvancedTab.as`
  (`SetAllItemPhysicsNoCollide` runtime rewrite) and research
  `2026-08-24-ItemAndGhostCollisions.md` (static vs dynamic collision systems,
  `Phys 28 = NoCollision`, `CGameObjectPhyModel` LoadData DataRefs).
- Research `2026-09-11-EmbeddedItemShapeBaking.md` (`IsMeshCollidable` /
  GenerateShapeFromMesh wire format and load-time regeneration).
- Research `turbo/extracted/web-research-moving-items.md` (TM2020 NadeoImporter is
  static-only; MP-era `<TriggerShape>`/`<MoveShape>` conventions; importer
  material-library physics ids) and `turbo/ghidra-out/MaterialLib_ParsePhysicsId.c`.
