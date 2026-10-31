# Experimental collision paths (Studio 0.5.8)

The user explicitly requested access despite the failed stability gate. This release exposes the relay as **Experimental**, not as a reliable passenger platform. The earlier release-gate report records the previous decision and remains valid failure evidence.

## Authoring

Open Kinematics > Draw motion path > Use > Experimental - polyline with collision. Draw or enter points, preview, and apply. The existing searchable segment selector, complete route overview, viewport selection, segment speed/duration edits and route JSON save/load work with this mode. Start delay and parking distance remain editable in the segment editor. Restore previous motion restores the source document.

The compiler supports decoded static triangle visuals and version 6/7 triangle collision surfaces, up to 128 legs (including return), with a combined expanded visual/collision budget of one million vertices. It rejects vertical/near-vertical legs, rotated source bodies, compound/cached collision, skins, morphs and secondary/external position streams. Rejection leaves the document unchanged. This is not universal item support.

Each segment has its own world-bound body and native translation/fold driver. Inactive copies fold below the route. They **retain collision**: map authors must place them beneath terrain and keep their space inaccessible. Parking distance does not guarantee clearance or physics stability. Static source parts are retained once. Normals/tangents and bounds transform with geometry. Source geometry is retained for subsequent previews and applies, avoiding anchor drift from already transformed relay bodies.

## Verification

- All 14 console suites and tracked tool builds passed locally.
- ItemMotion: 41 checks pass, including relay topology, world-space geometry with nonzero anchor, independent native bindings, channel key budget, synchronized timelines, source isolation, unsupported-shape refusal, invalid triangle indices, editable delay/parking and route JSON round-trip. A 48-case matrix covers every supported repeat/easing option with and without waypoint pauses and initial delay.
- Release build succeeds.
- Browser: unsupported stock obstacle refuses without mutation; supported cube converts, previews, applies, edits delay, reapplies without anchor drift, exports and restores the original static document.
- Browser export reparses as three collision bodies with three world-bound drivers. Existing eight-point preview, segment-edit and smooth-path browser regressions pass.
- Production compiler exported and reparsed the 128-leg coaster route: 294005 ms per cycle, 8000 ms initial dwell, 5 m/s average speed, 64 m parking distance.

## New native tests

Both off-centre tests use the production compiler, a rail-free map, fixed vehicle spawn and item offset +1.25 m X/+1.25 m Z. No steering, throttle or braking input is applied.

- Linear: sustained launch at 42.234 s, peak upward velocity 66.763 m/s. The recording continues five seconds after detection.
- QuadInOut: sustained launch at 42.094 s, peak upward velocity 66.347 m/s. Easing **does not fix the handoff**. It stops/starts at each segment and reaches twice the average speed mid-segment, so it is not a lower-peak-speed experiment.
- Both failures occur at the same problematic transition. This supports investigating collider activation/folding/contact transfer, rather than assuming easing solves the defect. It does not establish the engine's internal cause.
- CE_ProductionFull: the new production-compiler export completed the entire centred coaster lap (294.969 seconds sampled), returning to approximately (399.597, 19.014, 399.986). Peak upward velocity 4.4530 m/s; no launch above the 15 m/s detection threshold. Native video observation confirms the car rides on the platform. This single successful lap does not invalidate the off-centre or earlier repeat-lap failures.

A transient MCP socket disconnect occurred while opening the first disposable map, before item placement. Trackmania remained running, no new crash log appeared, and the bridge recovered. That attempt provides no item-physics evidence; the test was restarted from a confirmed menu under a new label.

## Video

`artifacts/collision-experimental/Achtbaan-collision-50sec.mp4`: 49.167 seconds, 1280x720, 30 fps, H.264, faststart, no audio. Alternate follow and fixed-wide shots, 6x playback, clearly labelled as a montage of two separate test runs. Sources are the earlier smooth centred lap and the new CE_Wide lap; this is not one continuously recorded camera-changing run and is not evidence of increased physical item speed. CE_Wide completed 294.954 seconds without a sustained launch (peak upward velocity 4.7584 m/s).

## Next research, not implemented guarantees

1. Separate visual handoff from collider handoff, with an overlapping contact interval while both colliders are stationary. Prior overlap experiments introduced ghost support/drift; require off-centre and repeated-loop tests before recommending it.
2. Keep incoming/outgoing collider orientation aligned near contact, or use an independent non-rendered contact proxy. Avoid sweeping a large offset collider through the passenger; validate native bounds and actual contact, not only scene preview.
3. Compare reduced folding radius and reduced physical speed independently. Clearance must be verified over the entire elevated route; a smaller radius may expose parked bodies.
4. Test longer contact decks, vehicle offsets, steep crests, loop seams and at least two cycles. A successful centred lap is insufficient.

Reproduction: `Tests/CollisionRelayExport` generates Linear and QuadInOut fixtures from a source cube and route JSON using the production compiler. `Tests/MotionCollisionHandoffProof/native_probe.py` records native tests. Browser test: set `STUDIO_BASE_URL` and `STUDIO_RELAY_FIXTURE`, then run `node Tests/Browser/collision-relay.cjs`. Fixtures and recordings remain local artifacts; no game assets are newly published in the repository.
