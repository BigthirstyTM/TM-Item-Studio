# Collision path: release gate and smooth recording

The user authorized integration only if the remaining checks passed. The
off-centre passenger check fails, so the relay compiler is **not promoted into
the production Studio UI**. The earlier two centred laps remain valid evidence
for that specific setup, not a general collision guarantee.

## Off-centre passenger

Use the same SHA-256-verified TMIS_CC_Relay5BoundsFullNear item. Translate both
the moving item and rail context by +1.25 m X and +1.25 m Z while keeping the
vehicle spawn fixed. The vehicle starts within the 8 m deck, not at its edge.
Inputs remain zero. The native car camera changes observation only.

- CG_OffsetFull: visible launch at about 42.08 seconds of recovered telemetry,
  upward velocity 63.2653 m/s. The run was interrupted after the failure.
  Samples were recovered from the append-only RPC log; time is relative to the
  first response timestamp, rather than the runner's monotonic clock. Its
  interrupted MP4 has no moov atom and is **not valid video evidence**.
- CG_OffsetProof: separate 65-second recording reproduces the launch near
  telemetry time 42.141 seconds, upward velocity 63.685 m/s. The car flips and
  falls to terrain. This finalized recording is the visual evidence.
- CG_OffsetProof also sampled a very short transient at 22.547 seconds with
  implausibly large reported velocity and displaced position, followed by a
  return to the platform on the next sample. Preserve it as a telemetry anomaly;
  do not equate its 1,028.29 m/s reported vertical peak with the later sustained
  ballistic launch. The later launch is supported by consecutive samples and
  the video, not just a single maximum statistic.

The fitted animation clock places the sustained failure at the leg 15 to 16
handoff (schedule 43.790 seconds). This is a different handoff from the previous
centred 128 m parking failure. The 64 m change is therefore insufficient for
arbitrary passenger positions. A product-level release gate fails even if
centred endurance subsequently succeeds.

## Rail-free endurance

CG_NoRailsTwoLaps contains only the moving item, verified by saved-map reparse.
The centred passenger completes the first lap without the rail asset. The
planned 600-second run automatically stops on a measured upward impulse near
telemetry time 344.250 seconds during lap two. The last sample has upward
velocity 56.4547 m/s, position Y=48.9366 versus Y=41.474 on the preceding sample,
up.Y=-0.58251 and all wheels in air. Inputs are zero. The fitted clock places
this near leg 21 to 22 of the second lap (schedule 51.905 seconds).

Because this initial stop-on-impulse version finalized immediately, its footage
cuts off around the onset. It demonstrates one completed lap and an unsafe
second-lap event, **not two completed laps**. A follow-up recording retains five
seconds after a trigger to distinguish a sustained failure from a transient
sample. The first run's 1,796 samples are retained without deleting anomalies.

CG_EnduranceConfirm repeats the rail-free setup with a five-second post-trigger
recording. This time it fails in the **first** lap near the high crest: 1,125
samples cover 211.938 seconds, maximum upward velocity is 27.187 m/s, minimum
up.Y=-0.983062, and 22 samples have all wheels in air. Consecutive telemetry and
the video show the car leaving the cube, tumbling and reaching the ground.
This is not a reproduction at the identical second-lap handoff; it is another
failure of the same release gate and shows run-to-run sensitivity. No rail-free
two-lap test passed. The exact internal engine cause is still unproven.

A separate serialized-shape audit of all 128 bodies found version-6 mesh
surfaces, each with 24 vertices and 12 triangles. No cooked triangles, octree
cells or AABB tree cells are serialized in these shapes. A stale serialized
acceleration tree is therefore not an explanation for this fixture; this does
not rule out engine runtime contact/cache effects.

## Recording changes

The earlier free camera was repositioned once per second. The 4x edit magnified
those jumps. The new runner can use the engine's per-frame cam1/cam2/cam3 and
requests 30 or 60 fps at native speed. It checks the returned camera mode. It does not
interpolate synthetic frames or change item/car transforms.

The 60 fps GDI capture actually delivered roughly 46 fps, with dropped frames.
The delivery recording uses a constant 30 fps output and the native per-frame
camera at normal speed. A decoded moving window at video 45-47 seconds contains
60 distinct frames with zero adjacent duplicates. This is a cadence sample, not
a claim that every frame of the complete recording is unique.

CG_SmoothCoaster completes a centred full lap with rails: 1,568 samples over
294.969 seconds, all zero inputs, final position (399.597,19.014,399.970).
There is one all-wheel-air sample and the minimum up.Y is 0.907935: physical
handoff jolts have not been edited away. The delivery file
`Achtbaan-collision-vloeiende-camera.mp4` is a remux of that complete recording,
300 seconds, 1280x720, 9,000 frames, avg/r frame rate 30/1. Full decode succeeds.
This smooth camera recording is a successful demonstration, not release approval.

The runner now allows bounded tests up to 900 seconds, graceful stop files and
an optional upward-impulse stop. Recordings use fragmented MP4 so a future
interruption does not require a final moov atom. Native camera modes and the old
polling camera are mutually exclusive. Cleanup explicitly requests leaving the
editor playground before returning to Menu. The camera smoke initially exceeded
the old cleanup retry budget; the next preflight confirmed Menu before proceeding.

All new artifacts are local under `artifacts/coaster-gate`. The corrupted first
recording is excluded from any delivery package. No production feature or
deployment is changed on the strength of the two earlier centred demonstrations.

The runtime is Openplanet 1.29.14 (6fd4200f), engine build 2026-02-02 17:51:19,
with the existing tm-control-mcp/E++ stack. No plugin behavior or runtime physics
patches were added. Python syntax and diff checks pass. Saved maps verify the
intended item counts and positions; the game returned to Menu after testing.

Next acceptance condition: a handoff must retain support independently of the
passenger's position within the deck and across repeated cycles. Adding a
central-position-only option or calling this safe because the parser accepts it
would not meet that condition. Keep the existing straight-line obstacle and
scenery options unchanged while investigating the relay's physical discontinuity.
