# Typed motion fixtures

Run with the unchanged bundled parser:

```sh
DOTNET_ROLL_FORWARD=Major dotnet build Tests/ItemMotion -m:2
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/ItemMotion --no-build
```

The console suite links the production `Models/ItemMotion*.cs` sources. Fixtures
are synthetic; reference coordinates and easing values are calculated independently
in the tests. Archive save/reparse tests exercise the bundled GBX serializer,
including repeated prefab references. No game installation or assets are required.

`ItemMotion.Read` exposes exact scalar ranges, axes and separate timelines.
`Apply` validates and prepares all requested edits before mutation; a null timeline
edit preserves that authored timeline, including unknown keys. Source edits affect
all instances that share the source node. Translation is metres, angles are degrees,
key storage is integral milliseconds, and evaluator time is seconds.

The visual preview uses an **integer-microsecond clock**. Time in seconds and each
timeline's phase offset are independently rounded to the nearest microsecond, with
midpoints rounded away from zero. Modulo, phase addition and key selection then use
exact integers; phase 1 wraps to phase 0. Thus sub-microsecond differences may display
the same pose. Continuous adjacent-double ordering and native physics precision are
not promised. Very large finite times are reduced by an integer-second multiple of
the period before scaling to avoid overflow; the input double's precision still limits
the available clock resolution. This preview policy does not quantize stored edits.

`ItemMotionBindings.Resolve` returns raw parent/child slots, the filtered slot table,
original entity-array indexes, scoped occurrence paths and non-serialized source
handles. Flat root prefabs use filtered original order. A world parent is scoped to
that root occurrence. Native nested flattening retains constraint indexes without
rebasing, so nested occurrences explicitly return unsupported rather than selecting
a guessed leaf-local target. `/ent:` occurrence paths trigger this guard; callers
using other nested edges must pass `isNestedPrefabOccurrence: true`. Invalid children
never target a fallback mesh. External references fail without dependency resolution.

`ItemMotionTransforms.ComposeVisual` accepts separate child-rest, parent-rest and
parent-live rigid world transforms. It returns an unframed visual pose using
System.Numerics row-vector composition. It does not simulate chained collision or
renderer-deferred parent motion.

`ItemMotionTiming` edits exact version-gated instance period/phase fields without
upgrading the wire version or changing texture/kinematic/shadow fields. Negative
phase/max values are preserved as native sentinels; the editor accepts canonical -1
for inherited phase or disabled max randomization. Snapshot flags expose those modes.
Negative base periods remain preserved but unsupported for editing/preview. Their
mapping to kinematic signal timing is unverified; `ResolvePreviewPhase` returns
unsupported. `Evaluate`'s phase argument is a separate preview-only control.

The deterministic preview supports Constant, Linear, QuadIn, QuadOut and QuadInOut,
up to four keys per timeline. It distinguishes empty timelines (minimum) from
nonempty zero-total-duration timelines (zero translation/identity rotation).
Both timing representations are supported: `IsDuration=true` stores segment durations;
`false` stores cumulative end times. Preview derives each later duration as
`max(0, end[i] - end[i-1])` using original adjacent values, without rewriting stored
flags or times. Validation limits the normalized period, not the sum of endpoints.
Unknown easing, nonzero anchors, unresolved targets and unsupported
instance versions are reported rather than coerced. Synthetic checks do not establish
game motion or collision parity.
