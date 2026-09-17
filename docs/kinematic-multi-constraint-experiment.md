# Multi-constraint synthetic experiment

Scope: issue #22 follow-up — the minimal reference experiment that must pass
before any bounded multi-constraint path editor is considered. This is an
analysis/test deliverable: no editor UI, no conversion API, no production-code
change. Verification date: 2026-09-17, suite `Tests/KinematicReference`
(`feat/multi-constraint-experiment`, stacked on `feat/kinematic-reference-analysis`).

## Experiment design

Synthetic item built **from scratch** with the bundled parser (the
`Tests/Browser/FixtureGenerator` pattern: authored nodes, uncompressed save,
immediate reparse self-check; nothing is committed to disk). Two kinematic
bodies and two constraints, exactly as the issue specifies:

| Entry | Model | Params | Channel | Timeline |
|---|---|---|---|---|
| A | `CPlugDynaObjectModel` v13, `IsStatic=false` | `SInstanceParams` v2, `IsKinematic=true` | — | — |
| B | `CPlugDynaObjectModel` v13, `IsStatic=false` | `SInstanceParams` v2, `IsKinematic=true` | — | — |
| world→A | `NPlugDyna_SKinematicConstraint` sub 3 | `SPrefabConstraintParams` v0, `Ent1=-1`, `Ent2=0` | X, 0→4 m | 3 × Linear 1000 ms |
| A→B | `NPlugDyna_SKinematicConstraint` sub 3 | `SPrefabConstraintParams` v0, `Ent1=0`, `Ent2=1` | Z, 0→2 m, rot Y −90→90° | 2 × Linear 1500 ms |

Distinct translation axes and ranges, synchronized 3000 ms total durations,
zero anchors, flat `CPlugPrefab` v11 root. Both entity layouts are exercised:
`[A, B, world→A, A→B]` and the issue-documented `DTC_Firework200`-style
alternating layout `[A, world→A, B, A→B]`. Each body carries an authored
`CPlugSolid2Model` triangle (reflection-assisted vertex declaration, exactly
like the existing generator) plus empty `CPlugSurface` shape placeholders.

## Invariants verified

- **Construction/round-trip**: from-scratch build → uncompressed GBX save →
  reparse preserves the full graph: item envelope, prefab version, both
  kinematic classifications, both meshes with decoded vertex positions and
  rest transforms, both constraint channels and every timeline key
  (ease/reverse/duration), both binding param sets.
- **Slot-table ordering**: the filtered kinematic slot table is `[A, B]` in
  filtered order in **both** layouts; `Ent2` indexes stay `0`/`1` while the
  original entity-array indexes shift (B moves from index 1 to 2 in the
  interleaved layout). Constraint and non-kinematic entries never occupy
  slots.
- **Binding-resolver classification** (report; `Models/ItemMotionBinding.cs`
  unchanged): world→A resolves as **Supported, world-relative** (parent
  `IsWorld`, raw −1, scoped to the root occurrence; child slot 0 → A's
  array entry). A→B resolves as **Supported, flat parent-child** (parent raw
  slot 0 → A's entry and `/ent:` path, not world; child raw slot 1 → B).
  No guard rejects this shape: it is a flat root prefab, params version 0,
  zero anchors, distinct parent/child entries. Every guard stays active on
  adversarial variants of the same graph: nested occurrence paths and the
  explicit `isNestedPrefabOccurrence` flag, params version 1, nonzero
  anchors, and self-parented slots all still return Unsupported.
- **Edit/rebind invariance**: a typed scalar+timeline edit on the chained
  constraint and an `ApplyTargets` rebind (A→B decoupled to world→B) persist
  through save/reparse; the world→A constraint is untouched; an invalid
  child rebind is refused without mutating either slot.
- **Composed preview**: evaluating both constraints independently and
  composing with `ItemMotionTransforms.ComposeVisual` (B's rest expressed in
  A's frame, A's evaluated signal as the live parent) yields the expected
  world pose (5, 0, 1) m at t = 0.75 s — the two-channel composition the
  documented native vis pass performs (`parentLive * Loc * signal`).

## Conclusion

**Proved**: a bounded two-object/two-constraint graph is fully representable
in the typed schema, constructs from scratch through the bundled serializer,
round-trips losslessly, keeps a stable filtered slot table across compact and
alternating entity layouts, is classified as supported (world-relative and
parent-child) by the existing binding resolver without loosening any guard,
and composes in the deterministic preview. Straight-chain authoring of the
kind requested (A→B with distinct axes and synchronized durations) is not
blocked by the data model, the serializer, or the resolver.

**Rejected / not proved here**:

- No game behaviour. Fresh Trackmania inventory visibility, placement and
  observed composition remain unverified; the doc makes no such claim.
  Per static reverse-engineering notes maintained alongside the fork, the
  native physics pass composes chained constraints **without** a live parent
  term and the vis pass composes with one (with a possible one-frame
  deferred-write lag) — chained *collision* behaviour must therefore be
  treated as unsupported until game-tested.
- The synthetic pair's `StaticShape`/`DynaShape` are empty placeholders; a
  game-ready pair additionally needs authored surface meshes on the dyna
  shapes (the same notes indicate the kinematic spawn path dereferences the
  dyna shape unconditionally).
- Curved arcs are still out of scope: each constraint remains one axis +
  scalar min/max; a curve needs either multiple short straight constraints
  (this experiment's pattern) or a different native model.

An experimental path editor is **not** proposed by this change; the issue's
gate (a validated reference pair) still requires the fresh-game step.
