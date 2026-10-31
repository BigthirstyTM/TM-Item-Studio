# Collision-preserving path follow: native relay experiments

Research only. These probes do not establish a reliable arbitrary-path platform
feature and are not enabled in Item Studio's production UI.

## What was demonstrated

A flat deck carried a stationary car around an entire 16 x 16 m square, including
all four corners and the loop seam. It used four independently world-bound
kinematic bodies, timed so that the next body took over at each corner. The car
followed both world axes at approximately 2 m/s without throttle or steering.
This is native item physics: no plugin updates body/car transforms during play.

However, that run failed in its second lap. At sample time 38.094 s it briefly
lost wheel contact, recovered, then at 46.344 s was launched at 11.716 m/s upward.
Sampled car Y peaked at 23.234 m, versus about 16.014 m when supported. It later
fell to the ground. The video preserves both the successful first lap and failure.

Repeating the unchanged flat relay from the saved map after a game restart failed
in the first lap (sampled Y peak 67.5732 m). Thus the first successful full lap is
a real observation, but not a reproducible stability guarantee. The different
failure timing warrants phase/contact-transition testing before further claims.

Adding side walls did not fix the handoff. Neither did reducing the parking
pivot offset from 48 m to 8 m. Both variants launched the car during their first
lap, reaching sampled Y around 67 m. This rules out promoting either adjustment
as a measured fix. It does not prove that every possible relay design must fail.

## Fixture and mechanism

Generator: [`Tests/MotionCollisionRelayProof`](../Tests/MotionCollisionRelayProof).
It writes complete converted kinematic documents using the GBX.NET 2.5 models,
then reparses and checks world-root bindings, body references, key limits,
channel cycle lengths and constraint counts. Both distinct collision vertex
arrays and the visual mesh receive the same geometry transform.

Source: local `BF2_ASSETS/Cube.Item.Gbx`, measured local bounds +/-4 m.
Flat deck: 16 x 2 x 16 m. Square vertices: (0,0), (16,0), (16,16), (0,16).
Four 8 s legs, 32 s cycle, no pauses. Every constraint has Ent1=-1 and targets
its own body. Translation uses a hold/move/hold schedule of at most three keys.
Rotation uses constant 0/180 degree keys to fold inactive decks under the terrain.
There is no interpolated corner and no parent-child collision dependency.

At handoff the visible deck occupies the same nominal geometry, but its native
body identity changes and both bodies undergo discontinuous rotation. The
observed launches are consistent with a contact/velocity discontinuity. The
exact engine impulse source has not been isolated; do not present that hypothesis
as an engine implementation fact.

The cage adds four 1 m thick, 4 m tall walls per deck (20 bodies total), sharing
the same segment timing. The low-pivot test changes only the flat relay's parking
offset. Inactive geometry is hidden below this fixture's terrain; that does not
make it collision-free or safe to place anywhere in an arbitrary map.

## Native verification

TM2020 build `17.1-TM20201212-E02021751`, Stadium editor test mode; local
tm-control-mcp 0.4.0 and E++ pack 0.1.0. The bridge handles setup, camera and
read-only telemetry, not the item animation. FID acceptance, inventory discovery,
placement readback, map save, visible movement and vehicle contact were checked
separately. Exports added after inventory initialization needed a game restart.

Place relay items at (400,15,400); deck top Y=16, terrain Y=8. Spawn through
editor testFromCoord (12,10,12), North. The initial car position is near (400,16,400).
No gas or steering was applied. Samples begin after test startup and camera setup,
so sample time is **not** the game timer; consult video timestamps for exact phase.
Sampling is approximately every 0.55 s and cannot resolve every physics tick.

Other probes in this batch:

- Offset Y-rotation deck (24 s orbit): collision initially supports the car,
  but the car does not stay on for a full circle.
- Rotation plus X translation: same loss of support; no stable passenger result.
- Rotation plus Y translation: rises with the car briefly, then the car falls off.
- An initial orbit deck at terrain height was confounded by ground support.
  Its recording is retained but excluded as evidence for carrying the car.

These rotating probes are placed at (388,15,400), with mesh and collision offset
12 m along local X. Failure to carry a passive car is distinct from having no
collision. Prior direct-root lift and rotating-obstacle contact evidence remains
in [`smooth-path-collision-2026-10-31.md`](smooth-path-collision-2026-10-31.md).

## Evidence and reproduction

The local evidence directory is `artifacts/collision-follow2`. It contains full
native recordings, raw telemetry, setup/preload logs, saved-file hashes and a
portable evidence ZIP with items and maps. Raw telemetry and the manifest are
also retained under `docs/evidence/collision-relay-2026-10-31`.

Run the generator with the source cube and an output directory. Its generated
items were compared byte-for-byte against the installed test items. Geometry,
clock phase, spawn placement and all test parameters matter: this is a bounded
fixture experiment, not an arbitrary imported-item compatibility result.

## Next research gate

The next useful experiment should isolate handoff behavior: single transition,
both directions, different phase offsets and a controlled overlap/settling
window. Measure car velocity and wheel contact before generalizing to more legs.
Investigate an alternative to instantaneous folding before adding UI options.
Continuous single-root translation/rotation is a separate avenue for constrained
curved obstacles, but the passive-car experiments here do not prove a usable
curved passenger platform.

A production exporter would additionally need a parking-clearance strategy,
geometry/orientation continuity for non-square items, arbitrary-route timing,
different speeds and elevations, save/reload equivalence and repeatable long-run
contact. No new supported path-follow mode or deployment is claimed by this work.
