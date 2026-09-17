# Static → kinematic conversion analysis

Scope: issue #22 work item 4 (kinematic authoring analysis). This document maps
the tracked kinematic/static pair with the bundled parser, states the minimal
transformation set required for a static→kinematic conversion, and records the
conclusion. Analysis and tests only — no conversion UI or conversion API is
added. Verification date: 2026-09-17, suite `Tests/KinematicReference`.

## Provenance of the analyzed pair

| | `CustomItem_Kinematic.Item.Gbx` | `CustomItem_Static.Item.Gbx` |
|---|---|---|
| SHA-256 | `a51f35b4b589cf39fd2fd3fdf0f9152c1b01b26091b3c76c467d67aad84796f4` | `afde25629cd7030844b1cbf2f1c400bed1280205f7c1e167b6d89048ddf4939c` |
| Size / decompressed | 121,593 / 167,359 bytes | 35,076 / 50,822 bytes |
| Ident | `("", "Stadium2020", "BigthirstyTM")` | `("", "Stadium2020", "-oTBhm4_S1-UxnlnBizUDA")` |

Both files are exported test artifacts that already exist in this repository
(`Test Exported items/`). They were **not** exported as a same-object pair: the
author identifiers differ, and the meshes are unrelated. They are valid for a
structural comparison of the two entity-model shapes, and nothing more. Fresh
Trackmania validation of these files is not documented in this repository and
is not claimed here.

Structure dump command: `DOTNET_ROLL_FORWARD=Major dotnet run --project
Tests/KinematicReference --no-build -- --dump "Test Exported items/CustomItem_Kinematic.Item.Gbx" "Test Exported items/CustomItem_Static.Item.Gbx"`.

## Kinematic graph map

`CGameItemModel` (`ItemType`/`ItemTypeE` = Ornament, `DefaultPlacement` present):

```
EntityModel = CPlugPrefab version 11 (no external ents)
├─ ent[0] NPlugPrefab::SEntRef (identity transform, no name)
│  ├─ Params = NPlugDynaObjectModel_SInstanceParams version 2
│  │    PeriodSc=1  PeriodScMax=-1  Phase01=-1  Phase01Max=-1  TextureId=0
│  │    IsKinematic=true  CastStaticShadow=true
│  └─ Model = CPlugDynaObjectModel version 13
│       IsStatic=false  DynamizeOnSpawn=false  Mass=100  BreakSpeedKmh=100
│       Mesh        = CPlugSolid2Model                  (visual subgraph)
│       StaticShape = CPlugSurface { Surf = Mesh, Geom = null, 0 materials }
│       DynaShape   = CPlugSurface { Surf = Mesh, Geom = null, 0 materials }
│       LocAnim     = null
│       (StaticShape and DynaShape are distinct nodes with equal content)
└─ ent[1] NPlugPrefab::SEntRef (identity transform)
   ├─ Params = NPlugDyna_SPrefabConstraintParams version 0
   │    Ent1 = -1 (world parent)  Ent2 = 0  Pos1 = Pos2 = (0,0,0)
   └─ Model = NPlugDyna_SKinematicConstraint version 0, subversion 3
        Translation: axis Y, TransMin 0 → TransMax 1 (metres)
        TransAnimFunc: IsDuration=true, 2 keys
          [0] QuadInOut, forward, 10 000 ms
          [1] QuadInOut, reverse,  10 000 ms
        Rotation: axis Y, AngleMinDeg 0 → AngleMaxDeg 0
        RotAnimFunc: IsDuration=true, 1 key Linear 10 000 ms
        ShaderTc: type None
```

Every kinematic datum is owned by a node that exists only in this shape: the
prefab envelope, the dyna object model with its two collision surfaces, the
instance params with `IsKinematic`, and the constraint node with its binding
params and timelines.

## Static graph map

`CGameItemModel` (Ornament, `DefaultPlacement` present):

```
EntityModel = CGameCommonItemEntityModel
├─ StaticObject = CPlugStaticObjectModel version 3
│    Mesh = CPlugSolid2Model  Shape = null  IsMeshCollidable = true
├─ VisModel = null
├─ PhyModel = null
└─ TriggerShape = null
```

Collision is requested through the mesh-collidable flag; there is no explicit
collision surface subgraph, no prefab, no entity array, and no params nodes.

## Minimal transformation set

What a static→kinematic conversion of this shape must produce:

1. **Entity-model swap.** `CGameCommonItemEntityModel` must be replaced by a
   `CPlugPrefab` (version 11 in the reference) carrying an `Ents` array. The
   two entity-model classes are unrelated node types; there is no field-level
   upgrade path between them.
2. **Body swap.** `CPlugStaticObjectModel` → `CPlugDynaObjectModel`
   (version 13 in the reference) with `IsStatic=false`, `Mass`, `BreakSpeedKmh`.
   The `CPlugSolid2Model` visual mesh subgraph is the only part of the static
   archive that can be re-parented as-is.
3. **Collision authoring.** The mesh-collidable flag has no dyna equivalent
   here: the reference carries explicit `StaticShape` and `DynaShape`
   `CPlugSurface` nodes, each with a decoded `Surf` mesh. Static reverse
   engineering notes (Ghidra, maintained alongside the fork, not part of this
   repository) indicate the kinematic spawn path dereferences the dyna shape's
   surface unconditionally, so this subgraph cannot be skipped or left null.
4. **Instance params.** A `NPlugDynaObjectModel_SInstanceParams` (version 2)
   with `IsKinematic=true` must be attached to the dyna entry. Without it the
   entry is not classified kinematic (both natively and in
   `Models/ItemMotionBinding.cs`).
5. **Constraint authoring.** A second prefab entry holding the
   `NPlugDyna_SKinematicConstraint` (axis/range scalars, two timelines of up to
   four `SubAnimFunc` keys each) plus `NPlugDyna_SPrefabConstraintParams`
   version 0 binding it into the prefab's filtered kinematic slot table.
6. **Unchanged.** `ItemType`/`ItemTypeE` (Ornament in both files) and
   `DefaultPlacement` are orthogonal to kinematics; no placement-layer edit is
   required.

## Slot-table and `Ent1`/`Ent2` semantics

`NPlugDyna_SPrefabConstraintParams` targets the prefab's **filtered kinematic
slot table**: dyna-object entries that carry typed instance params with
`IsKinematic=true`, in entity-array order. Static entries, dyna entries without
params, and the constraint entries themselves are not slots. `Ent1` is the
parent slot, `Ent2` the child slot; a parent of `-1` (or otherwise outside the
table) is the world. The tracked pair uses `Ent1=-1, Ent2=0`: the single
kinematic body animated relative to the world. `ItemMotionBindings.Resolve`
classifies exactly this and returns the filtered table with original array
indexes (`Tests/KinematicReference`, world-relative classification check).

Reference guidance (issue #22, documented topology of the user item
`DTC_Firework200.Item.Gbx`, inspected locally by the reporter and not part of
this repository): a `CPlugPrefab` root with 104 entries, 51 constraints, all
`Ent1=-1`, children `Ent2=0..50` each exactly once, an alternating
dyna-object/constraint entity layout, `NPlugDyna_SPrefabConstraintParams`
version 0 with zero anchors, and `TimeIsDuration=true` four-key timelines. That
item composes its effect from 51 **independent world-relative** constraints,
not from a parent-child chain — consistent with the scalar-channel model below.

## Segment semantics

Confirmed on the tracked constraint and pinned by the schema check in
`Tests/KinematicReference`: each channel (translation, rotation) carries
**one axis and one scalar min/max pair**, and each `SubAnimFunc` key carries
**only duration-or-end-time, easing, and reverse**. The typed adapter
(`ItemMotionEdit`, `ItemMotionKey`) exposes exactly this shape: extra keys
shape timing/repetition/reversal of a single-axis channel; no per-key
positions, directions or axes exist on the wire. The tracked reference's own
timelines illustrate it: two mirrored `QuadInOut` 10 s keys sweep one fixed
0→1 m Y translation. The segment editor must therefore not be described as a
waypoint or path editor.

## Conclusion

Static→kinematic conversion is **not an in-place editor operation**. It is
construction of a replacement entity-model subgraph: a different entity-model
class, a different object-model class, a collision-surface pair that the static
archive does not contain, an instance-params node, and a constraint node with
binding params and timelines. Only the visual mesh subgraph is portable. This
matches the existing capability audit (`docs/control-persistence.md`): the
current Convert buttons toggle `ItemType` and preview flags only and never
produce any of the nodes above, so they can never yield a real kinematic item.

A Studio-side conversion would have to be a guided authoring pipeline that
builds these nodes from scratch (the pattern `Tests/Browser/FixtureGenerator`
already uses for from-scratch items), or the conversion belongs in an external
authoring tool (BlenderMania/NadeoImporter-style). Either way it is graph
authoring with explicit collision surfaces, not a field edit; the analysis
here does not propose or add such a pipeline.

## Non-claims

- No game installation was available: inventory visibility, placement and
  playback of any file mentioned here are unverified; the fresh-game step
  remains open.
- The static reverse-engineering statements cited above come from notes
  maintained alongside the fork; they are static analysis, not claims verified
  in this environment.
- The tracked pair is structurally representative of the two entity-model
  shapes compared, nothing more (different authors, unrelated meshes).
