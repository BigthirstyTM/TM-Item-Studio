# Kinematic reference suite

Run with the unchanged bundled parser:

```sh
DOTNET_ROLL_FORWARD=Major dotnet build Tests/KinematicReference -m:2
DOTNET_ROLL_FORWARD=Major dotnet run --project Tests/KinematicReference --no-build
```

The console suite links the production `Models/ItemMotion*.cs` sources. Its
fixtures are the tracked exported pair
`Test Exported items/CustomItem_{Kinematic,Static}.Item.Gbx` (identity pinned by
SHA-256) plus synthetic graphs. The exported artifacts are already-in-repo test
items, not freshly game-validated references; no game installation or assets
are required to run the suite.

The default mode asserts: full structural maps of both archives,
parse→save→reparse invariance with byte-stable second saves, typed constraint
and timeline field round-trips, `ItemMotionBindings.Resolve` classification of
the tracked world-relative constraint, the typed segment schema (one axis +
scalar min/max per channel; keys carry only duration/easing/reverse), and the
explicit collision surface meshes of the exported kinematic template.

`--dump <item-paths>` prints the kinematic structure of any item archive for
diagnosis; it writes no files. Expectations in the checks are derived from the
reference dump documented in `docs/kinematic-conversion-analysis.md`, not from
the code under test. No claim in this suite establishes game behaviour; the
fresh-game step remains open.

The experiment checks build the issue #22 minimal pair from scratch (two
kinematic objects, world→A and A→B constraints, distinct axes/ranges,
synchronized timelines, compact and alternating entity layouts) and assert
save/reparse invariance, filtered slot-table ordering, binding-resolver
classification with every guard still active, edit/rebind round-trips and a
composed preview pose. See `docs/kinematic-multi-constraint-experiment.md`.
Nothing is written to disk; the synthetic graphs exist only in memory.
