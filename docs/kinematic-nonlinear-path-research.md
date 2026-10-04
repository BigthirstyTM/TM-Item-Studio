# Kinematic non-linear paths and Havok collision: spec-level research

Scope: follow-up research answering the three questions from the kinematic
dossier (single-body item following a non-linear path with mesh collision over
the full path). This is an analysis deliverable: no production-code change, no
UI, no game claim. Verification date: 2026-10-04, sources below.

## Sources and evidence class

| Source | Class | Used for |
|---|---|---|
| `docs/kinematic-conversion-analysis.md`, `docs/kinematic-multi-constraint-experiment.md` | repo analysis (serializer/preview proven, no game) | slot-table semantics, physics/vis pass notes, `DTC_Firework200` topology |
| GBX.NET `*.chunkl` + `*.cs` (BigBang1112/gbx-net @ master, fetched 2026-10-04) | authoritative format definitions | exact field sets of the four relevant nodes |
| `AchimBunke/TM_CSharpMapping` @ main (public converter) | third-party tooling | how a mature pipeline emits constraints, groups and anchors |
| `graphify-out/` (Trackmania_KB graph, 2026-09-23) | derived index | locating every dyna/constraint symbol and its consumers |
| This repository's tests (`Tests/KinematicReference`) | synthetic round-trip only | schema guards, `LocAnim` absence in the reference pair |

No game installation was available. Everything marked *untested* requires the
fresh-game step described in the experiment matrix.

## 1. Verified format facts

All four nodes are the complete wire format of the kinematic system. There is
no other chunk family involved.

### `NPlugDyna_SKinematicConstraint` — chunk `0x2F0CA000`

```text
archive: version, SubVersion
AnimFunc TransAnimFunc          (bool IsDuration, SubAnimFunc[] SubFuncs)
AnimFunc RotAnimFunc            (same shape)
ShaderTc: type, version, AnimFuncNat[], optional TransSubTextureIn
byte<EAxis> TransAxis   float TransMin   float TransMax
byte<EAxis> RotAxis     float AngleMinDeg  float AngleMaxDeg
SubAnimFunc = byte<AnimEase> Ease, boolbyte Reverse, timeint Duration
EAxis = X | Y | Z
```

Consequences:

- One translation axis and one rotation axis per constraint, each a **scalar
  min/max range** driven by up to four `(ease, reverse, duration)` keys.
  There is no waypoint, per-key position, direction vector, or path field
  anywhere in the chunk. The segment editor in this repository exposes the
  full wire shape; nothing is hidden.
- `TransAxis` and `RotAxis` are independent fields of **one** node: a single
  constraint can translate along one axis while rotating about another at the
  same time (both channels are part of one signal evaluation).
- No physics/collision flag exists in this chunk.

### `NPlugDyna_SPrefabConstraintParams` — chunk `0x2F0C8000`

```text
version: int Ent1, int Ent2, vec3 Pos1, vec3 Pos2
```

The binding is only `(parent slot, child slot, two anchors)`. There is no
"physical", "compound" or "compose" bit. `Ent1 = -1` (or out of table) means
world, `Ent2` is the child slot in the filtered kinematic slot table.

### `NPlugDynaObjectModel_SInstanceParams` — chunk `0x2F0B6000`

```text
version: float PeriodSc, int TextureId, bool IsKinematic
v1+: PeriodScMax, Phase01, Phase01Max
v2+: bool CastStaticShadow
```

No physics-compose flag here either. `IsKinematic` classifies the slot; it
does not select a composition mode.

### `CPlugDynaObjectModel` — chunk `0x09144000`

Serialized body fields (GBX.NET `ReadWrite` order): `IsStatic`,
`DynamizeOnSpawn`, `Mesh`, `DynaShape`, `StaticShape`, `BreakSpeedKmh`,
`Mass`, light durations, `U01..U09`, **`LocAnim` (node ref to
`CPlugAnimLocSimple`)**, `U10`, **`LocAnimIsPhysical` (bool)**, `WaterModel`.

- `LocAnimIsPhysical` is the **only** boolean in the entire dyna spec whose
  name claims a physics/animation interaction.
- Graph check: in the whole 8-repository corpus the `LocAnimIsPhysical` symbol
  has exactly one edge — `defines` from its own class
  (`CPlugDynaObjectModel.cs:L64`). **No code in the corpus reads or writes
  it.** This repository touches `LocAnim` only for traversal and asserts it
  `null` in the tracked reference pair (`Tests/KinematicReference`), and never
  reads `LocAnimIsPhysical`.

### `CPlugAnimLocSimple` — chunk `0x090F8000`

```text
0x000: int version, int RotPeriod, int TransPeriod, float TransY
v1+:   int Axis
v2+:   int RotPeriodMax, int TransPeriodMax
v3+:   byte RotFunc, float RotAngle
```

A period-driven, single-axis oscillation (translation amplitude `TransY`,
axis `Axis`, rotation angle `RotAngle` over its periods). It is **structurally
simpler than the kinematic constraint**: no easing segments, no reverse keys,
no independent ranges, still one axis.

## 2. Question 1 — is there a spec flag that forces Havok to transform child bodies in a compound parent-child relation?

**Answer: no such flag exists in the format.** The three places where such a
switch could live are fully enumerated above:

- `SPrefabConstraintParams` carries only `Ent1/Ent2/Pos1/Pos2`.
- `SInstanceParams` carries only period/phase/shadow/classification fields.
- The constraint chunk carries only channels and timelines.

The one candidate, `CPlugDynaObjectModel.LocAnimIsPhysical`, does **not**
encode parent-child composition. It is a per-body flag attached to the body's
*own* `LocAnim` channel, not to a constraint edge; a child constraint never
touches it, and no consumer of it exists anywhere in the analyzed corpus. Its
plausible meaning — "apply this body's `CPlugAnimLocSimple` animation to the
physics body instead of only the scene graph" — is a hypothesis, not a
documented fact.

Two related flags that change *other* behaviour and are worth knowing:

- `DynamizeOnSpawn` (bool, per body): would hand the body to Havok dynamics on
  spawn; presumably incompatible with scripted kinematic motion. Untested.
- `IsStatic` on the body plus the `StaticShape`/`DynaShape` pair: the tracked
  reference kinematic item uses `IsStatic=false` with both surfaces authored.
  The repo's static reverse-engineering notes (maintained alongside the fork)
  indicate the kinematic spawn path dereferences the dyna shape
  unconditionally, so neither surface may be skipped.

**Conclusion for Q1:** within the GBX specification there is no mechanism to
promote a chained child constraint to a physical one. The composition decision
is made by the game binary, not by data.

## 3. Question 2 — alternative chunk configurations for a single mesh on a non-linear path with collision

### 3.1 `CPlugAnimLocSimple` is not an alternative

It is strictly weaker than `NPlugDyna_SKinematicConstraint`: one axis,
period-driven, only scalar knobs (periods, `TransY` amplitude, `Axis`,
`RotAngle`) and no segment timeline: no easing keys, no reverse keys, no
independent min/max ranges. It cannot
express an L-shaped path, and nothing in the corpus pairs it with collision
authoring. The only open question is whether `LocAnimIsPhysical = true` makes
the body's oscillation physical — a single-axis experiment at best (E3 below).

### 3.2 A single root constraint already has two independent channels

Because `TransAxis` and `RotAxis` live in one node, **one root constraint**
(world-relative, `Ent1 = -1`, therefore in the composition mode the repo's
notes say is physical) can produce:

- translation along one axis **plus** rotation about another axis
  simultaneously, and
- a composite sweep whose collision envelope is non-linear even though the
  body origin stays on a straight line.

This is the strongest spec-level lever for "one body, non-linear collision
sweep with 100 % Havok collision" that requires no new fields. Whether the
physics pass applies both channels in the same tick is untested (E1).

### 3.3 Rotation pivots: the ent transform is the arc mechanism

From the vis compose formula recorded in
`docs/kinematic-multi-constraint-experiment.md` (`parentLive * Loc * signal`),
the constraint signal is applied in the body's local frame, i.e. rotation is
about the entity origin. Therefore:

- **Offsetting the geometry from the entity origin** (or equivalently putting
  `Loc` translation between origin and mesh) makes a rotation-only root
  constraint swing the mesh along an **arc**: an `AngleMinDeg=0 →
  AngleMaxDeg=90` quarter turn about a corner pivot traces a 90° arc with the
  root constraint's physical composition — a rounded "L" with collision over
  the whole path.
- The authoring schema of the third-party converter independently supports
  this concept: `MovingGroupConfig.AnchorPosition` is documented as *"Rotation
  anchor for that group. Required in Trackmania coordinate space"*. Note that
  its `MeshBuilder` currently emits `Pos1 = Pos2 = Vec3.Zero` and I could find
  no consumer of `AnchorPosition` in that code base, so whether the anchor is
  realized through the ent `Loc`, through `Pos1/Pos2`, or not at all in that
  tool is unverified. In this repository nothing sets either (E2 tests it
  directly).

### 3.4 What is definitively ruled out by the data model

- Two root constraints on the same `Ent2` slot: your dual-writer freeze; also
  structurally discouraged — the `DTC_Firework200` reference gives every
  child `Ent2` exactly once over 51 constraints.
- Waypoint paths inside one constraint: no such field exists (§1).
- Axis switching over time: `TransAxis` is a single scalar field, not
  animated.

## 4. Question 3 — is it a hard engine limit?

**Practically yes, for an exact L-path with collision on one body — given
current knowledge.** The reasoning chain:

1. The format offers no child-physical flag (Q1) and no path field (§1).
2. The repo's static reverse-engineering notes state the native physics pass
   composes chained constraints **without** a live parent term while the vis
   pass composes **with** one — which matches your observation exactly:
   child moves visually, collision stays at the origin.
3. Two writers on one body freeze the transform (your finding B).
4. Nesting does not help: the repo's binding resolver documents that native
   **flattening retains raw constraint slots without rebasing**
   (`Models/ItemMotionBinding.cs`), so a nested prefab occurrence does not
   give its child a fresh world-relative frame — `Ent1 = -1` always means the
   single flattened world.

So the proven route to collision along a two-segment path remains **multiple
independent world-relative root bodies** — exactly the `DTC_Firework200`
pattern (104 entries, 51 constraints, all `Ent1 = -1`, children `Ent2=0..50`)
and the default mode of the third-party converter (`Ent1 = -1` unless a
`RelativeMovingParentGroup` is requested, which it does emit for *visual*
relative motion).

Two caveats before calling it a binary-level hard limit:

- Every statement above is static analysis or data-model reasoning; the
  fresh-game step has not been run for these specific variants.
- Two untested spec levers (§3.2 combined channels, §3.3 pivot arc) could give
  a single body a genuinely non-linear physical path *without* needing a
  second axis — not an exact sharp L, but possibly within tolerance for the
  intended gameplay.

## 5. Experiment matrix (fresh-game step)

Each item is constructible with the existing from-scratch generator pattern
(`Tests/KinematicReference` / `Tests/Browser/FixtureGenerator`), one variable
per item, all with authored `StaticShape`/`DynaShape` surfaces and a valid
non-null `Mesh` (sub-millimetre triangle where a helper body is needed):

| # | Item | Variable under test | Pass criterion |
|---|---|---|---|
| E1 | One root constraint, `TransAxis=X` 0→4 m **and** `RotAxis=Y` 0→90°, synced 4-key timelines | both channels physical in one signal | car is pushed along the whole sweep; mesh visibly rotates while translating |
| E2 | One root constraint, rotation only, mesh authored 2 m off the entity origin | pivot = entity origin (`Loc`) | mesh arcs 90° **and** collision follows the arc (not the origin) |
| E3 | Chained child body with `LocAnim` (`CPlugAnimLocSimple`, `TransPeriod`, `TransY`) + `LocAnimIsPhysical = true` | semantics of the only physical flag | does the child's own oscillation collide? does it inherit parent motion? |
| E4 | Two root constraints (`Ent1=-1`) targeting the same `Ent2` | re-confirm dual-writer freeze natively | expected: no motion |
| E5 | Baseline A→B chain (`Ent1=0`) | re-confirm child is visual-only | expected: child collision at origin |
| E6 | Split L: two independent root bodies, handover-synchronized at the corner | proven-pattern control | collision continuous across both legs |

E1 and E2 are the two answers that could change the "single body" conclusion;
E3 is the only test of a spec flag; E4–E6 pin the known behaviour so a single
game session yields a complete comparison.

## 6. Non-claims

- No game installation was available: inventory visibility, placement,
  playback, collision and freeze behaviour of every variant above are
  unverified; all "untested" labels stand.
- The physics/vis pass statements come from static reverse-engineering notes
  maintained alongside the fork, not from this repository's tests.
- `LocAnimIsPhysical` semantics are inferred from its name, position in the
  serialization order and the absence of consumers; no documentation for it
  was found.
- GBX.NET's `CPlugDynaObjectModel.ReadWrite` applies no version conditionals
  around `LocAnim`/`LocAnimIsPhysical`; behaviour on pre-reference versions is
  out of scope here.
- `graphify-out/` indexes eight sibling repositories, not this one; it was
  used only to locate symbols and prove the absence of consumers.
