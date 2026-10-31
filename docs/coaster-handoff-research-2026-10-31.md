# Coaster collision: targeted handoff experiments

Research continuation of [the full-route tests](coaster-collision-research-2026-10-31.md).
These are experimental native Item.Gbx fixtures, not a production feature.

## Question and controls

The original 128-point coaster repeatedly launched the passive passenger around
leg 102 to 103. We isolated source points 99 through 106 (zero-based), retaining
the seven slope/turn deltas at 5 m/s. The short fixture is normalized to
(400,15,400), starts with an eight-second dwell, and holds at the endpoint until
its 60-second cycle ends. Native recordings stop before that reset.

The short map has only the moving item, with no coaster rails to provide
alternative support. The vehicle starts above the 8 m cube (spawn grid Y=11),
with no gas, brake or steering. The following camera changes only the camera.

Variants:
- Focus: original +180 degree fold, 128 m local parking offset.
- Reverse: -180 degree fold, same 128 m offset.
- Near: +180 degree fold, 32 m offset.
- Offset: unchanged Focus item, translated +0.55 m X and +0.49 m Z relative to
  the fixed spawn, approximating the passenger's lateral offset before the
  previous full-route failure. This does not reproduce its complete history.

All four short runs retain support through the seven legs and endpoint dwell.
Consequently they **do not reproduce the failure**, and cannot establish a fix.
The full-route comparison is necessary. Heights, upstream contact history and
floating-point coordinate operations differ from the complete route.

## Reproduce

Generate using Tests/CoasterCollisionProof with the cube source, original
route128.json and an output folder as the first three arguments. Add `focus`
for the seven-leg fixtures or `full-variants` for the full 128-leg variants.
Full Reverse keeps parking at 128 m; Full Near uses 64 m because the full route
has larger height differences. Both preserve original positions, speed, timing
and bounds. The generator asserts parked-body clearance, active corner alignment,
bindings, native key limits, equal clocks and serialized bounds.

The existing three-argument generator remains byte-identical for all four
previous fixture exports. Research changes do not change production exports.

Use Tests/MotionCollisionHandoffProof/native_probe.py from Menu with the item ID,
a unique label, `--output artifacts/coaster-handoff --seconds 35 --cube-spawn
--follow-car-camera`. For the offset control add `--item-offset 0.55 0.49`.
For the full route use `--seconds 300 --coaster-context --follow-car-camera`.

The first setup attempt had a connection reset while querying readiness after
opening an empty editor. The game remained alive; GetMode and GetItems verified
an empty map. We returned to Menu and reran with a fresh label. Another premature
runner invocation was rejected by its Menu precondition and did not mutate a map.
Neither setup-only attempt is counted as a physics test.

Local recordings, call logs, item bytes and schedules: `artifacts/coaster-handoff`.

## Full-route result: 64 m parking

CH_FullNear completes the full route and returns to the start with the passive
vehicle still on the visible cube. The native video covers the previously failing
crest and the loop seam. The 1,385 samples cover 294.64 seconds; no driver inputs
occur. Maximum upward velocity is 4.2925 m/s, versus 35.4761 m/s in the earlier
128 m BoundsFull run. There are three all-wheel-air samples, at approximately
86.531, 204.578 and 207.141 seconds. Minimum up.Y is 0.922843. The largest fitted
distance to the scheduled cube top is 1.457 m; final position is
(399.548,19.014,400.003), near the expected (400,19,400).

This is a completed native lap, **not uninterrupted contact or smooth arbitrary
path support**. The frame at approximately 204.766 seconds shows a brief roll
near the rising transition before the crest. The mechanism still switches among
independent world-bound bodies and parks inactive bodies underground. It does
not make parent-chain collision work, and it does not provide one continuous
physical collider. The parking radius change is experimentally associated with
removing the previous severe launch; the engine's internal cause remains unproven.

The full-route Reverse fixture was generated and statically validated but has
not been run in the game. Once Near completed the route, native testing prioritized
repeating that promising variant rather than claiming an untested alternative.

CH_FullNearRepeat also completes the route and loop seam. Its 1,375 samples span
294.953 seconds, all with zero driver inputs. Maximum upward velocity is
4.2149 m/s, minimum up.Y is 0.908207, and the maximum fitted distance to the cube
top is 1.463 m. Final position is (399.596,19.014,399.971). No all-wheel-air samples
were captured in this repeat; sampling is about 4.7 Hz, so this does not prove
continuous contact between samples. The visible roll still occurs at the rising
handoff. These are two independent full-lap runs, not a multi-lap endurance test.

All six saved maps were reparsed with the unsupported lightmap cache excluded:
four isolated maps contain one expected item and two complete-route maps contain
the expected moving item plus NewCoaster. Installed bytes match generated hashes.
Build, Python syntax, original-export byte regression and diff checks pass.
The existing MorphCount deprecation warning remains. The game returned to Menu.
Compact telemetry, hashes and summary are in
`docs/evidence/coaster-handoff-2026-10-31`. No production UI or deployment changed.

Video artifacts:
- CH_FullNear.mp4: full native recording at normal speed.
- Achtbaan-collision-volledige-ronde-4x.mp4: same recording, explicitly 4x speed.
- Achtbaan-collision-overdracht-64m.mp4: 28-second normal-speed excerpt from
  video time 198 seconds, covering the problematic crest.

Future tests should separate radius sensitivity from discontinuous vertical
velocity at slope changes, validate passengers away from the centre over a full
lap, remove the rail context as an additional independent control, and test
multiple consecutive laps before promoting this to a general Studio option.
