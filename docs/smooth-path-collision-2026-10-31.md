# Smooth paths, collision and route editing — 2026-10-31

## Native results

Tested in TM2020 build `17.1-TM20201212-E02021751`, local Stadium editor test mode.
All three collision items use a complete converted kinematic document, not a static
root with a substituted entity. Native loading, inventory placement, map save and
playback were exercised separately. No throttle or steering was applied during
the recorded vehicle samples.

| Experiment | Native observation | Scope of evidence |
|---|---|---|
| World-bound lift | Car stayed on the cube over repeated 12 m up/down cycles; sampled Y 8.003–20.008 m | Usable vertical platform support, not just an initial impact |
| World-bound Y rotation, mesh and shapes offset 12 m | Cube followed a circle and pushed the parked car from Z 399.997 to 415.247 m | Circular obstacle contact; not proof of riding a full circular platform |
| Two world-bound constraints driving the same body on X and Z | Item loaded and collided, but visibly travelled along one line instead of the intended square | This combination does not solve arbitrary polyline collision |
| Browser-exported smooth scenery route | Orange moving body followed the rounded closed route in game; source static housing remained fixed | Native motion proof for 20 sampled legs; moving collision intentionally absent |

The videos are real-time native window captures. Full local package:
`artifacts/path-collision/Path-Collision-Smooth-Bewijs.zip`.
It contains four videos, four items/maps, the preview schedule and an edited draft.
Committed hashes and timestamped vehicle positions/velocities/inputs:
[`evidence/path-collision-2026-10-31`](evidence/path-collision-2026-10-31).
The browser smooth export SHA256 exactly matches the file placed in game:
`BE851AECD3CA6AB03BA0EE1854CB00E84312CA1663E356F39BBB6A8909C7E0E4`.

Collision fixture generator:

```powershell
dotnet run --project Tests/MotionCollisionProof -- <Cube.Item.Gbx> <output-directory>
```

Use the measured 8 m cube for equivalent contact geometry. Native placements:
Lift `(400,4,400)`, Orbit `(412,12,400)`, DualAxis `(400,12,392)`, zero rotation.
Spawn via editor `testFromCoord (12,9,12), North` (car near `(400,8,400)`).
Lift: 2 s hold, 4 s rise, 2 s hold, 4 s fall. Orbit: 360° in 12 s.
DualAxis: four 3 s phases per axis, X/Z staggered by one phase.

## Smooth scenery tool

The existing drawn polygon remains editable. A separate Smooth scenery path
panel rounds its corners inward using quadratic Beziers, then compiles sampled
straight legs to the already measured carrier chain. Smoothness 0 retains the
polygon; 100% uses 45% of the shorter adjacent edge on each side of a corner.
Open endpoints are retained. Closed loops round every corner including the seam.
The route generally does not pass through the sharp control corners.

Curve detail is 2/4/8/16 samples per corner, reduced automatically for the native
80 ms minimum leg duration. The entire route, including returns, must fit the
tested 128-leg product limit. This is not a newly discovered engine ceiling.
Linear timing is used; waypoint pauses are excluded from the smooth generator.
Orientation is fixed. This is a sampled curve, not native analytic rotation arcs.

Preview draws the actual compiled route in cyan while preserving draggable
control markers. The preview and export use the same schedule. Creating a new
straight or smooth plan replaces the current per-segment edits.

## Route and constraint editing

Path segments & timing offers a searchable segment dropdown and a scrollable
whole-route table. Each segment shows endpoints, length, speed, duration and
start time. Editing an endpoint changes adjacent legs; closed seams and
ping-pong geometry stay connected. Position edits retain duration. Duration or
speed edits reschedule following channels. A straight obstacle's outbound and
return times stay linked because they share one native scalar channel.

Edits only change the draft and preview until Apply edited route. Restore previous
motion remains available. Save route draft writes JSON containing the complete
compiled schedule; load the original/source item and this JSON to continue later.
Anchor selection is taken from the current editor settings. Original smooth
control points are not reconstructed from that compiled schedule. Reopening only
an exported GBX exposes native constraints, not the original authoring draft.

Detailed constraints & timeline edits is collapsed by default and uses a
searchable dropdown to show one native constraint at a time. Translation and
rotation controls remain available. These advanced edits apply immediately.
Changing carrier rotation can alter the route; reapplying a generated path
replaces advanced changes. Free rotation per path segment is **not supported**
by this compiler, and no such native guarantee is claimed.

## Verification

- Full console suites passed before and after the change; ItemMotion includes
  curve seams/endpoints, timing, budgets, edit synchronization, refusal and JSON tests.
- Browser tests verify smooth draft/live changes, exact midpoint parity between
  preview and applied graph, export, per-segment edits, JSON roundtrip, searchable
  controls and preservation of imported native motion.
- Native proof is limited to the fixtures and settings described above. It does
  not establish arbitrary collidable paths, unlimited point counts or successful
  contact for every imported model.
