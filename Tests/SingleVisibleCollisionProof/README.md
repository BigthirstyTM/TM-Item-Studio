# Single visible collision experiments

Research fixtures only; these are not a supported arbitrary-path exporter.
See [native results](../../docs/single-visible-collision-research-2026-10-31.md).

Requires the local measured 8 m cube and the earlier relay/handoff fixtures:

```powershell
dotnet run --project Tests/SingleVisibleCollisionProof -- <Cube.Item.Gbx> <Trackmania/Items> <output-folder>
```

The generator reparses eleven fixtures and validates dynamic-body bindings, positive
duration keys, and the four-key ceiling. Overlay variants need
`TMIS_CollisionFollow2/TMIS_CF2_RelaySquare.Item.Gbx` and
`TMIS_CollisionHandoff/TMIS_CH_SquareOverlap.Item.Gbx` under the second argument.
It does not install items. Copy only the default generated files to a dedicated
local Items folder, preload/register them, and wait for the inventory scan.

From the game menu, with the local MCP/E++ stack running:

```powershell
python Tests/MotionCollisionHandoffProof/native_probe.py TMIS_SV_OrbitCage SV_OrbitCageNew --output artifacts/single-visible-collision --seconds 154
```

Use a new run label each time. The runner saves an isolated map, checks exactly
one requested item, records the native window and passive-car telemetry, and
returns to the menu. Transport success is not a physics pass. Inspect the video,
position, wheel contact, attitude and zero inputs together. Sample timestamps are
relative to telemetry startup, not the game animation clock.

`--include-crash-fixture` is a forensic reproduction switch, **not a normal test**.
It writes `ChainNonKinematic`, which crashed native placement on 2026-10-31.
Never install or run it in a normal batch. The observed crash file is quarantined
outside Trackmania's Items directory; the evidence package excludes it from Items.

Soft1/Soft2 and the overlap visual use unverified instance phase semantics.
Their synchronization is not established. SoftOut instead uses explicit global
timings and zero phase; it still launches the car at a handoff. None of these
overlays is suitable for production.

## Drawn-route feasibility

`drawn_route_analysis.py` takes JSON `[[x,z], ...]` points and computes the
arbitrary slide/rotation signals that would geometrically reproduce the drawing.
It deliberately does not export GBX or claim native feasibility. See
[the feasibility report](../../docs/drawn-collision-route-feasibility-2026-10-31.md)
for the distinction between geometric reachability and native timeline limits.

```powershell
python -m unittest discover -s Tests/SingleVisibleCollisionProof -p test_drawn_route_analysis.py -v
```
