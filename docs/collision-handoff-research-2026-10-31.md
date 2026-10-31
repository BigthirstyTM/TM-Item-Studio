# Collision handoff: overlap, settling and repeated corners

Follow-up to [the first relay experiments](collision-relay-path-research-2026-10-31.md).
These are native TM2020 experiments, not a new supported Item Studio feature.

## Findings

Pre-existing overlapping collision and delayed removal produced a repeatable
single 90-degree handoff without the earlier launch. In the overlap test and
its repeat, car Y stayed between 16.014 and 16.016 m, with no sampled instant at
which all four wheels were airborne. The car moved along X and then Z with zero
throttle/steering. A no-fold control and a longer removal delay produced the
same supported-height result.

This is not yet a stable full-route passenger platform. The incoming stationary
deck begins supporting the car early, causing it to stop around X=408.881 rather
than reaching the outgoing deck centre X=416. It then rides the second deck
near its edge. The experiment changes both incoming activation and outgoing
removal; it does not isolate which individual change is sufficient.

The 40 s square with early incoming activation and delayed outgoing removal
avoided upward launches in a 130 s recording. However, its car path drifted
relative to the moving deck centres. After one cycle, the car was near
(405,16,393), not back at its starting relative offset. In the second cycle it
fell off: the first sample below Y=15.5 was at sample time 50.453 s, Y=14.8184.
The whole run's sampled maximum Y was only 16.016. Thus the failure mode changed
from a launch to loss of support at an edge; the loop is still not usable as a
reliable passenger platform.

## Controlled fixtures and observations

All isolated fixtures use two 16 x 2 x 16 m decks, 2 m/s motion, no driver input,
and a single transition before a 120 s timeline reset. First deck waits 2 s and
moves +16 m X in seconds 2-10. The second starts at X=16 and moves 16 m along X
or Z. World-root bindings and the 48 m parking offset match the preceding study.

| Fixture | Changed behavior | Native result |
|---|---|---|
| StraightHard | Both bodies switch folded state at 10 s; same straight direction | Car gets a sideways impulse, about 14.7 m/s Z shortly after handoff, then falls. No corner is required to reproduce the fault. |
| CornerHard | Same switch, second leg +Z | Launch to sampled Y=27.6777; later lands on the second deck. End-state support alone would falsely call this a pass. |
| CornerOverlap | Incoming deck present from start; pause at corner 10-12 s; old deck folds at 14 s | Supported transfer, Y=16.014-16.016; stops short on first leg and rides near second deck edge. |
| CH_OverlapRepeat | Same unmodified item, new empty fixture after game restart | Same end position within 1 mm and same sampled height range; zero all-wheels-air samples. |
| CornerNoFold | Incoming present from start; no folding; second departs at 10 s | Same stable height and final position; both physical decks remain. |
| CornerClearance | Same as overlap, outgoing folds later at 19 s | Same stable height and final position; no improvement in relative position. |
| SquareOverlap | Four bodies, 40 s cycle; incoming appears 8 s before departure; outgoing folds 4 s after arrival | No upward launch; offset changes accumulate and car falls off during cycle two. |

These observations distinguish collision presence, supported transfer, and
faithful full-route carrying. Overlap can help the second without solving the
third. It also visibly creates extra platforms and leaves extra collision in
the scene; it is not visually or physically equivalent to one moving body.

## Verification and limits

Native build `17.1-TM20201212-E02021751`; local tm-control-mcp 0.4.0 with E++ pack.
All six generated items passed GBX.NET reparse/binding/timeline assertions and
native FID preloading. Inventory placement was read back and maps saved. Generated
bytes were compared with the installed native fixtures. Videos are real-time
game-window captures; no plugin drives either the items or the car during play.
All seven saved maps were also reparsed with ProbeAssets: each contains exactly
one expected item at (400,15,400), scale 1. This check excludes the unsupported
lightmap cache chunk 0304305B and is placement evidence, not full-map roundtrip.

Use zero-rotation item placement (400,15,400), Stadium terrain Y=8, deck top Y=16,
editor testFromCoord (12,10,12), North. Samples begin after camera setup and are
approximately 4 Hz, not per-physics-tick. Sample time differs from the game timer.
No all-wheels-air samples is a bounded observation, not proof of uninterrupted
contact between samples. Videos were also inspected.

An initial CornerHard setup accidentally retained the preceding item because a
menu transition had not completed. The recorder then failed its camera preflight.
That attempt is marked INVALID and excluded from all results. The harness was
corrected to require an empty new fixture, exactly one requested item before
playback, and confirmed Menu state between runs. The valid CornerHard test uses
`TMIS_CH_CornerHard_Isolated.Map.Gbx`. No invalid map is in the evidence package.

The checked-in generator and local runner are under
[`Tests/MotionCollisionHandoffProof`](../Tests/MotionCollisionHandoffProof).
The runner's full end-to-end verification was the successful CH_OverlapRepeat
run, including map save, video, telemetry and return to Menu. It is not a CI test
and requires the local game/MCP setup. No production code or public deployment
changed in this research.

## Evidence and next gate

Local package: `artifacts/collision-handoff/Collision-Handoff-Research.zip`, with
seven valid videos, six items, seven maps, telemetry, source and SHA256 manifest.
Raw telemetry and manifest: `docs/evidence/collision-handoff-2026-10-31`.

The next test should measure and control passenger offset over successive turns:
vary overlap depth, deck size, arrival/departure timing and relative surface
height separately. Keep a control without any folding. A route compiler would
also need to handle visible duplicates, extra collision, safe parking, reverse
corners, arbitrary source geometry and different speeds. A reliable one-item
arbitrary path with collision has not been demonstrated by these tests.
