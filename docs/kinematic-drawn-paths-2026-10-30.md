# Kinematic items and native drawn paths — 30 October 2026

> Follow-up: [live preview, editable points and eight-leg native video proof](live-follow-path-2026-10-30.md). The four-leg cap and origin-only drawing described below document the earlier implementation.

## Result and evidence boundary

Draw Motion Path now compiles a drawn polyline to native GBX kinematic constraints. The exported item animates inside Trackmania 2020 without an Openplanet plugin or runtime script. Openplanet/Editor++ were used only to arrange and inspect the disposable test map.

- **Obstacle:** two points, one directly world-bound moving body, original collision shapes retained. The body's local X axis is aligned to the line, so its visible mesh rotates too.
- **Scenery:** up to four directed legs including the return, one hidden carrier per leg, and a final visible body. The original visible orientation is retained. The moving body's full-size collision is replaced with a 0.1 mm compatibility triangle. Static parts and their collision remain in the item.

A visible model following a path is **not proof of moving collision**. The tested carrier's visible pusher passed through a parked car repeatedly. The car stayed at approximately (463.999, 8.014, 399.998). A directly bound control displaced the car. Keeping a full-size collider on a carrier-driven child would leave misleading collision in another position. Scenery mode therefore removes it explicitly.

This report supersedes speculative path and collision conclusions in the earlier kinematic research documents. It does not establish that every Item.Gbx can be converted, or that every shape behaves as a stable vehicle platform.

## What GBX.NET 2.5 contributes

The application already uses its patched GBX.NET 2.5 parser. Better member names make existing native structures easier to understand and edit. A library update does not add physics primitives to Trackmania.

The relevant structures are `CPlugDynaObjectModel`, `NPlugDynaObjectModel_SInstanceParams`, `NPlugDyna_SKinematicConstraint`, and `NPlugDyna_SPrefabConstraintParams`, inside a `CPlugPrefab`.

A constraint contains one scalar translation range and one scalar rotation range, each with an axis and ease/reverse/duration keys. Binding parameters identify parent and child body slots, with optional anchors. Instance parameters supply period, phase and randomization ranges. Shader animation is separate; the new compiler preserves the visible body's original shader data.

There is no general XYZ waypoint spline field in the inspected constraint schema. A path must be synthesized from constraints. This finding concerns that schema; it does not mean every animation mechanism in the game has been decoded. `LocAnim`, dynamic physics, mesh deformation and shader animation must not be silently reinterpreted as this path format.

## Why the previous implementation failed

The previous implementation discarded the first point and height, accumulated absolute headings through parented rotations, and could stack carriers on each application. Curve detail subdivided straight segments without producing a curved route. The UI was restricted to static items. An unfinished parked-handover method could report success after changing part of the graph.

The replacement validates a complete plan and builds an isolated prefab clone before replacing the live document. Reapplying uses the original session baseline; Restore previous motion recovers that baseline, including a source that was static. Unsupported edits leave the document unchanged. The unfinished handover preset is removed from the UI and refuses without mutation.

## Native compilation

For each directed segment, calculate its world-space delta, length and duration from average speed. The first point sets the visible body's origin. Pauses occur once at each waypoint. Closed loop adds the final-to-first leg; return appends the reversed route; reset jumps to the start at the cycle boundary.

For an obstacle, rotate the body's rest orientation to align local X with the delta. Use a single 0-to-length translation signal: start hold, forward motion, end hold, reverse motion. Reset omits reverse motion. This uses no more than four keys. For two points, Close loop and Return along path are geometrically equivalent.

For scenery, each hidden carrier has a rest orientation aligned to one segment and a zero rotation signal. Its timeline holds at zero, moves once, then holds at full length until cycle end. The chain adds these translations; the visible body has a stationary constraint to the last carrier. This avoids accumulating yaw. Period is normalized to 1, phase to 0, and random maxima to -1, synchronizing the legs.

The measured engine restriction is **four timeline keys per channel**, not four total constraints. A square uses four carrier constraints plus the final body constraint. Four scenery legs is our tested product limit, not a universal engine limit. Do not increase it or emit a longer timeline without a separate native experiment.

## Controls and boundaries

The compact panel provides use, repeat and easing dropdowns; average speed; waypoint pauses; XZ/XY/YZ drawing planes and offset; mouse point placement; undo/clear; editable XYZ coordinates; Apply and in-session Restore.

Right-drag orbits during drawing. Left clicks target the selected plane rather than a mesh under the cursor. Coordinates require Use coordinates before Apply. Clearing points does not erase applied motion. Changing document/variant resets drawing state. These short dropdowns do not need a large searchable list; material search remains in the material controls.

Bounds: 2–5 points, within 10,000 m of the origin, at least 1 cm between consecutive points, average speed 0.1–1000 m/s, at least 80 ms travel per leg, pauses 0–60 seconds. Return legs count toward the four-leg scenery limit. A three-point L returns in four legs; a four-point square closes in four legs; returning along every leg of a four-point route exceeds the tested limit and is refused.

Authoring accepts one supported root moving body and its world-bound constraint with zero anchors. Static-only nested prefabs are preserved. Nested animated graphs, multiple moving bodies, nonzero anchors, unsupported versions, `LocAnim` and dynamize-on-spawn drivers are refused. Simple static meshes use the existing conversion. An arbitrary static prefab is not automatically flattened into a moving mesh.

Generated scenery files remain playable after reopening, but the high-level route and restore baseline are session-only. Reopening does not reconstruct an editable route from the carrier graph. Project/sidecar persistence is a priority.

## Test matrix and observations

Automated .NET tests cover planner validation, 3 repeat modes × 4 easings × 2 pause choices for obstacles, timeline budgets, 3D scenery midpoints, orientation, source isolation, unsupported anchors, invalid forged plans, near-negative-X alignment and atomic handover refusal. The full repository suite passed before final UI hardening; targeted tests were rerun afterward.

Browser tests export and replace nine routes, use actual pointer events on all three planes, check coordinate synchronization, invalid-input isolation, undo/clear, simple static conversion/restore, export/reopen and document reset. Helper coverage checks names/help and responsive widths. The existing pointer-selection/keyboard/export workflow is exercised. One earlier pointer-selection run timed out; a subsequent full workflow passed without changing selection code. That isolated failure is not evidence of a fixed selection defect.

Native map: `Documents/Trackmania/Maps/TMIS_DrawnBrowser_20261030.Map.Gbx`. Exports: `Documents/Trackmania/Items/TMIS_DrawnPath_20261030`. Additional contacts are saved in `TMIS_DrawnBrowser_Contact_20261030.Map.Gbx`.

| Export | Route/options | Native observation |
|---|---|---|
| RootX | 16 m X, return, linear | Motion visible; parked car displaced from spawn (144,8,144) to about (146.976,8.014,145.263) |
| RootDiagonal | Offset start, 12/16 m diagonal, quadratic in/out, 500 ms pauses | Motion visible; isolated additional placement displaced car from (400,8,400) to about (389.235,8.014,414.643), without driving input |
| RootVertical | 8 m Y, quadratic in, 500 ms pauses | Vertical motion visible; contact moved the car sideways, but static housing overlaps the initial area: not proof of sustained lift transport |
| RootNegative | Negative X, reset, quadratic out, pause | Negative-direction/reset fixture visible and animated |
| Square | Offset start, four-leg closed loop | Native prototype followed a full square; browser export animates with preserved orientation |
| Triangle | Closed, quadratic in/out, pauses | Browser export visible and animated |
| ReturnL | Two outgoing legs plus reversed return, pauses | Browser export visible and animated |
| Space | Triangle with height differences | Browser export visible and animated |
| ResetL | Two legs, quadratic out, reset | Browser export visible and animated |

Test environment: Trackmania executable file version `2026.2.2.1751`, Openplanet `1.29.14`, patched GBX.NET `2.5.0.0`. The final nine browser exports match the ingame files byte-for-byte; see the checked-in SHA256 manifest.

All nine browser exports parsed with at most four keys per channel and were placed together in the saved map. Twenty fixed-camera frames were captured roughly one second apart. These establish native animation across the fixture set; they are not sub-frame numerical measurements of every easing curve or contact condition. The square prototype sequence covers a full cycle. Static conversion is browser/round-trip tested separately, not part of the nine-item native claim.

Full evidence is retained under `artifacts/motion-path`: browser-all and square frame sequences, carrier contact captures, browser-root-contact, diagonal-contact and vertical-contact captures, JSON calls, exports, manifest and suite logs. Selected evidence and a compact hash/status report are checked in under `docs/evidence/drawn-path-2026-10-30`.

## Further features worth developing

### Near-term, using established primitives

1. **Lifts, sliding doors, pistons and pushers:** two points, speed, dwell and phase. Offer explicit mesh-axis preparation to preserve a desired visible orientation. A flat-platform fixture must prove support over multiple cycles before claiming a validated lift.
2. **Spinners, swinging doors and pendulums:** pivot/anchor visualization, angle range, period and easing. The rotation channel exists; verify collision at pivot and outer edge for each preset.
3. **Synchronized timelines:** shared period and named phase offsets for independent bodies, useful for alternating doors and obstacle waves. Distinguish independent bodies from one object transferring between colliders.
4. **Editable path projects:** persist source, points, options and generator version; reopen, drag points, recompile and undo. Do not assume every imported carrier graph originated here.
5. **Collision preview and capability badges:** distinguish moving shapes, static shapes, compatibility triangles and unresolved dependencies. Separate visual playback from vehicle-contact verification.
6. **Cycle preview:** scrub a cycle, mark arrival/dwell times, show total duration before applying, and explain return-leg limits.

### Promising, requiring native experiments

7. **Circular/orbital motion:** a rotation with offset mesh or suitable pivot can produce an arc. Verify native anchors and vehicle collision before releasing a circular-platform preset.
8. **Screw/helix motion:** combine translation and rotation on a directly bound body. The schema supports both channels; collision, axis conventions and return behavior need dedicated tests.
9. **Heading along a route:** separate visible heading from carrier orientations. Define corner, roll, vertical-segment and reversal behavior. Adding absolute yaw at every parent recreates the old defect.
10. **Smooth curves:** sample a spline into a measured number of legs or establish another driver. Subdivision alone is not curvature. Current bounds prevent a misleading unlimited curve tool.
11. **Moving lights/effects:** inspect ownership and test attachment in a native map. A moving mesh does not prove its emitters follow.
12. **Animated materials:** preserve and selectively expose supported shader/subtexture animation independently of spatial motion. Texture availability and native rendering need separate evidence.
13. **Reusable native QA:** hash exports, place tagged fixtures, run cycles, sample vehicle state and capture screenshots. Add controlled contacts for XYZ, diagonals, rotation, speed extremes and different vehicle geometries.

### Not promised by this format

Arbitrary collision-correct 3D waypoint travel, gameplay-triggered paths, branching routes, player-sensitive stops, guaranteed conveyor surfaces and unlimited keys are not established by GBX.NET 2.5. Some may need another native mechanism or map/gameplay scripting. A plugin moving editor objects is not a standalone Item.Gbx solution.

Recommended order: persistent editable paths; verified lift/door/spinner presets; synchronized multi-body timelines; collision visualization; then bounded curved/oriented scenery and experimental orbit/helix modes.
