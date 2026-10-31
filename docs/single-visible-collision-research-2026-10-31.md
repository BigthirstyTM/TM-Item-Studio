# One visible moving platform with collision

Continuation of the [handoff study](collision-handoff-research-2026-10-31.md).
These are local native TM2020 experiments, not a released path-follow feature.

## What changed

The relay can be rendered as one deck by replacing each physical relay body's
mesh with a valid tiny carrier mesh and adding a separate scenery-chain visual.
That fixes the visible duplication, but **does not fix the physics**. The hard
relay still launches the car; the overlap relay can support it beside the visible
deck and eventually loses it. Invisible extra collision is not acceptable as a
faithful single moving platform.

A different approach keeps the same physical bodies throughout the whole motion:
one deck and four side walls, each driven directly from the world with identical
translation/rotation channels. They form one visible moving cage, without parked
duplicate decks, a child collision dependency, or collision handoffs. The initial
circular run carried the passive car for more than three 24-second cycles.
The compound run added eased 16 m X translation and also retained the car for
more than three cycles. This is evidence for bounded curved passenger platforms,
not arbitrary user-drawn paths or arbitrary imported geometry.

## Experiments

| Fixture | Observation |
|---|---|
| OverlayHard | One visible deck; car launched, sampled Y up to 68.485 m. Rejected. |
| OverlayOverlap | One visible deck; ghost support beside it and eventual loss of support. Phase alignment is unverified. Rejected. |
| OverlaySoft1 / Soft2 | Gradual folding with per-body phase offsets; car moves diagonally too early and falls. The phase schedule is not established, so these do not isolate easing effectiveness. |
| OverlaySoftOut | Explicit global 36 s schedule, no phase offsets; one-second pauses, incoming snap and eased outgoing fold. Car launched at first handoff, sampled Y up to 27.8592 m. Rejected. |
| OrbitCage | 82 s recording, 308 samples; Y 16.018–16.202 m, no sampled all-wheels-air state. Minimum raw up.y 0.998104. |
| OrbitCageRepeat | Fresh map, same bytes; 154 s recording, 595 samples over 148.75 s (>6 cycles); Y 16.013–16.206 m, minimum up.y 0.998106, no sampled all-wheels-air state. |
| OrbitCompoundCage | 82 s recording, 308 samples; Y 16.017–16.158 m, minimum up.y 0.998887, no sampled all-wheels-air state. |
| CurvedLoopCage | 138 s recording, 532 samples over 132.797 s (>4 cycles); Y 16.008–16.043 m, minimum up.y 0.999829, no sampled all-wheels-air state. Initial framing clips part of the route; camera was widened during the recording. |
| CurvedLoopRepeat | Fresh map, unchanged item, corrected camera throughout; 138 s video, 532 samples over 132.75 s (>4 cycles); Y 16.009–16.043 m, minimum up.y 0.999829, no sampled all-wheels-air state. Car remains within the visible cage and slides against its walls. |
| ChainBaseline | Restored source collision on the scenery-chain child; car stays on terrain while the visible deck moves. |
| ChainPhysicalFlag | Setting LocAnimIsPhysical=true does not make child collision follow the parent chain. |
| ChainPhysicalZeroAnim | Adding an explicit zero-displacement local animation still does not propagate collision. |
| ChainNonKinematic | Native placement crash. Quarantined and excluded from default generation/installable evidence. No playable result. |

The cage's walls matter: earlier open rotating decks lost the passive car. The
car is not welded to the platform and may slide against its walls. A retained
car is not evidence that its position relative to the deck remains fixed.

## Geometry and timing

Source is the locally measured cube with bounds +/-4 m. Deck dimensions are
16 x 2 x 16 m; four walls are 1 m thick and 4 m tall. Mesh and both distinct
collision shapes receive the same transform. The circular deck is offset 12 m
in X from its world-root pivot and rotates 360 degrees in 24 s. Compound motion
adds X=0→16→0 with two 12 s QuadInOut keys. All five pieces use identical clocks.

CurvedLoopCage tests a further 32 s route: eased +32 m X, a 180-degree arc,
eased -32 m X, and the reverse arc. Its geometry is offset 12 m in Z from the
pivot. Four keys per channel preserve the same physical body identities; this
is a constrained two-line/two-arc shape, not a general polygon compiler.
QuadInOut brings motion to rest at each line/arc junction; this is not constant
speed around the whole route. The two arcs use opposite angular traversal and
are not a stadium-shaped racetrack. The car slides relative to the deck and is
contained by the walls, rather than being rigidly attached to its centre.

All fixtures are placed at (400,15,400), scale 1, zero rotation; deck top is Y=16
and terrain Y=8. The car spawns through testFromCoord (12,10,12), North. The
bridge performs setup/camera/read-only telemetry, not runtime motion. Videos
are real-time game-window captures. Sampling is approximately 4 Hz and cannot
prove uninterrupted contact between samples; inspect video and telemetry together.
Telemetry time starts after test/camera setup and is not the animation clock.

## Crash and recovery

At 12:54:54 local time on 2026-10-31, the chain child with IsKinematic=false
crashed immediately after placement. Trackmania.exe reported access violation
at relative offset 0x6C39D4. The specific engine defect is not established.
The exact item, crash report and Openplanet log are preserved locally under
`artifacts/single-visible-collision/crash-chain-nonkinematic`.
The file was removed from the native Items directory and is excluded from
normal generation. Do not repeat it in a test batch.

After restart, the first SoftOut setup failed safely because the inventory had
not discovered the newly installed item. It placed nothing and produced no
video. Refreshing inventory and confirming the exact entry resolved setup;
the later recorded run is the physics result above.
CurvedLoopCage also needed a restart after FID preload/inventory refresh alone
failed to register it. Both unsuccessful setups placed zero items. The runner
now refreshes and waits boundedly for the exact inventory entry before placement.

## Verification and evidence

All eleven default fixtures were exported, reparsed, and checked for valid body
indices and one to four positive-duration keys per channel. Generated bytes match
the installed files. Build and Python syntax checks passed; the existing
MorphCount deprecation warning can appear on a clean build. Production code is
unchanged, so these are fixture/harness checks, not a claim of a new full-app test.
Saved maps are inspected with ProbeAssets, excluding unsupported lightmap cache
chunk 0304305B: this establishes placement only, not full-map roundtrip.
All thirteen recorded maps contain exactly one expected item at (400,15,400),
scale 1. The game returned to Menu after the final test.

Local package: `artifacts/single-visible-collision/Single-Visible-Collision-Research.zip`.
It contains the default items, recorded maps, full videos, telemetry and generator
source. The crash fixture is deliberately excluded. Committed telemetry and
SHA256 manifest: `docs/evidence/single-visible-collision-2026-10-31`.
`One-Platform-Curved-Path-Proof.mp4` is a 40 s, normal-speed excerpt starting at
4 s of the full CurvedLoopRepeat recording; full footage is retained.
`summarize.py` reads original GetVehicleState responses for older samples missing
attitude/brake fields; all recorded passive runs have zero gas/steer/brake.

## Remaining gate

No production exporter/UI/deployment changed. Before offering a curved platform
tool, establish repeatable saved-map reload behavior, placement rotations/scales,
reversed turns, speed/radius limits, entry/exit driving and imported geometry.
Determine whether the constrained curves can faithfully represent the requested
drawn path; do not silently replace a user's route with an unrelated circle.
An arbitrary collidable path with one faithfully aligned visible platform remains
unresolved. A successful cage fixture does not remove that limitation.

Generator and reproduction instructions:
[`Tests/SingleVisibleCollisionProof`](../Tests/SingleVisibleCollisionProof).
