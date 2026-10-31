# Collision experiments on the original 128-point coaster

## Result and boundary

The exact route from the [scenery coaster proof](coaster-follow-path-2026-10-31.md)
was exported as collision experiments and played on the same NewCoaster asset.
The original prop is the red 8 m cube, not a train model. No route simplification,
planar flattening, new circle, or substitute track was used.

Restoring full collision to the scenery child does not make it carry the car.
A relay of 128 independently world-bound physical cubes **does carry the car
through many original route segments**, including turns and changes in elevation.
At 20 m/s it eventually launches the car. At 5 m/s it lasts substantially longer,
but a full-duration run also launches the car. It is not a reliable finished
coaster passenger item and is not enabled in the production editor.

## Construction

All 128 source points and all segment directions are retained. The route is
translated by minus its original start (-134.4675,4.218,0); the coaster is shifted
by the corresponding offset, preserving the original relative alignment.
Moving item placement is (400,15,400), coaster placement (534.4675,10.782,400).
The cube starts with its top at Y=19. The test car spawns at grid (12,11,12),
above the cube, with zero throttle, steering and brake throughout recorded play.

The generator adds an 8-second dwell at the beginning of each cycle, allowing
the car to land before departure. Per-leg speed is constant, with durations
rounded to integer milliseconds as in the existing compiler. The original
20 m/s movement takes 71.509 s, giving a 79.509 s cycle with dwell. The 5 m/s
variant has a 294.005 s cycle. Speed and initial dwell are the timing changes.

Each relay body moves over one original leg and rotates to a parking position
below terrain when inactive. Geometry is counter-rotated within each body's
segment frame so that the active cube retains the original fixed orientation.
The local frame keeps its Y direction upward on slopes, making the parking
offset downward. There is one nominally visible active cube. Body identity and
rotation change at handoffs; this is still a discontinuous physical relay.
Parked objects still have collision below the terrain. This is fixture-specific
parking, not a universally safe object for any map or underground route.

## Native observations

- `CC_Chain`: initial attempt used the thin-platform spawn height, inside the
  taller cube. The car stays near (400.127,10.814,397.366), supported by the
  coaster rather than following the cube. Its spawn is confounded; use the
  corrected `CC_ChainAbove` repeat for the collision conclusion.
- `CC_ChainAbove`: corrected spawn above the cube, with a following camera.
  The car drops to the rails near (400.162,10.814,397.158) and stays there while
  the visible cube departs. This directly rejects restored child collision as
  a passenger solution for this route. 160 samples in a40-second recording.
- `CC_Relay20`: corrected spawn demonstrably lands on the cube (Y about19.016).
  Car follows the early turns and climb. Later sampled upward velocity reaches
  61.7092 m/s, car Y reaches98.6502, and the car flips. 483 samples, 29
  all-wheels-air samples. Rejected as stable passenger motion.
- `CC_Relay5`: first96-second recording follows a large initial portion of the
  route. 491 samples, Y19.013–54.3992, minimum up.y0.999773; one all-wheels-air
  sample. This short observation alone is insufficient to establish a full lap.
- `CC_Relay5Full`: 300-second recording covers the294.005-second animation cycle,
  but the car does not complete it on the cube. At sample time207.234s the car
  is launched with49.3158 m/s upward velocity and is already tumbling. Sampled
  maximum Y114.09, minimum up.y-0.99736, 22 all-wheels-air samples among1588.
  There is an earlier brief loss of wheel contact at86.5s. No driver input.
- `CC_Bounds`:145-second recording with the corrected bounds and following
  camera. 590 samples across139.844s; Y19.013–54.945, minimum up.y0.999787,
  zero all-wheels-air samples, zero input. The car is visibly on the cube through
  the inspected curves and slopes. Maximum distance to the scheduled cube top
  is0.91m using the fitted startup offset. This run ends before the earlier
  late failure and therefore is not evidence of a complete successful lap.
- `CC_BoundsFull`:300-second repeat of the corrected-bounds item, with the car
  followed by the camera throughout. The late launch recurs near the same
  transition: maximum upward velocity35.4761 m/s, sampled Y up to98.8396,
  minimum up.y-0.930113. 20 all-wheels-air samples among1386; zero input.
  Thus fixing visibility did not resolve the physical handoff failure.

Telemetry time begins after spawning/camera setup and is not the native animation
clock. `analyze.py` fits an approximate startup phase only to the first9 seconds.
Its distance diagnostic is not direct measurement of native body positions.
The coaster itself has collision: ground-contact flags alone cannot distinguish
cube support from riding or resting on the rails. Videos and height/trajectory
must be inspected together.

## Rendering correction

The first relay fixtures transform visual vertices far from their original
origin but leave old visual bounds. Close-camera inspection of Relay5Full shows
the car supported above the rails while the cube is missing from the view.
This is not acceptable visible-platform evidence by itself.

`Relay5Bounds` recomputes every visual's centre/extents from its transformed
vertices. The collision geometry, segment schedules and parking mechanism are
unchanged. The corrected close-camera run shows the red cube under the car on
curves and slopes. This fixes the observed visibility issue in that test; it
does not establish that the collision launch has been fixed. This is a separate
visual correction, not a new physical algorithm.

The beginning of the bounds recording was briefly occluded by another desktop
window after restart. The game was brought forward before the clear close-camera
observations. Any shared excerpt starts after that obstruction; full local
recordings preserve setup and failures. Cameras are operated through MCP; item
and vehicle motion during play are native, with no plugin moving either body.

## Validation and artifacts

The generator is under `Tests/CoasterCollisionProof`. It checks active cube-corner
transforms, parking clearance, body counts/bindings, positive durations, matching
cycle lengths and at most four native keys per channel after export/reparse.
The bounds variant also checks that serialized visual bounds enclose every
transformed vertex. Generated and installed item bytes are compared. Build and
Python syntax checks pass; a clean build retains the existing MorphCount warning.

Items were preloaded by the native loader, discovered in inventory after restart,
placed into newly created maps and read back before playback. Each recorded map
contains exactly the requested moving item and NewCoaster. Saved-map inspection
uses ProbeAssets with unsupported lightmap cache0304305B excluded; it is placement
evidence, not a claim that every map chunk roundtrips.

Full local material: `artifacts/coaster-collision`. The research package contains
the items, maps, source route, recordings, schedules and telemetry. The unsafe
nonkinematic-child experiment from earlier work was not run or included.
No production feature or deployment changed.
All seven saved maps were inspected and contain exactly the expected two items.
The game returned to Menu after the final run. The package is
`artifacts/coaster-collision/Achtbaan-Collision-Onderzoek.zip`; compact telemetry
and hashes are committed under `docs/evidence/coaster-collision-2026-10-31`.
The normal-speed60-second working excerpt starts at35s of CC_Bounds. The
24-second failure excerpt starts at200s of CC_BoundsFull. Both excerpts are
labelled as partial research evidence; their full recordings are retained.

## Next experiment

Isolate the failing transfer near sample time207s of the slow run, retaining the
source slope change, positions and vehicle offset. Test arrival/removal timing
and support overlap there before another full-route build. A prior square
overlap fix cannot be assumed to solve this coaster's slopes, and overlap must
not leave visible duplicates or ghost support away from the displayed cube.
The result is progress toward the requested route, not a full working solution.
The fitted clock places the Relay5Full launch near the transition from leg102
to103 (one-based), at schedule time208.850s. This is an approximate localization,
not a direct native body-clock measurement. It is near the highest rising part
before the route bends, not at the loop seam.
