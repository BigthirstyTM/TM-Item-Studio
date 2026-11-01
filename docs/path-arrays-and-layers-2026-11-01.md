# Path arrays, animation layers and preview editing — 2026-11-01

Version 0.5.12 adds a Blender-style array along an authored scenery route. In
Path segment & timing, expand **Array along path** or **Animation layers**.
The preview timeline appears above the detailed path editor.

## Array behavior

- 1–32 copies, with a time delay between consecutive followers.
- Deterministic XYZ spread, random seed, and 1–4 shared uniform size variants.
- Train/cart, bird-flock and fish-school setup buttons. Alternate wing direction
  reverses the configured flap excursion on every other copy; it is not an
  arbitrary phase offset for all animations.
- Same-size visual geometry is shared in the GBX. Native body/constraint schedules
  remain per follower, so this does not mean zero additional data or rendering cost.
- Delays are baked into rebased route schedules, not the unverified native
  `Phase01` field. Split quadratic distance functions retain acceleration.
- Physical distance between followers varies with speed. There is no rigid
  coupling, collision avoidance, or constant-distance train solver.

Requires a looping scenery route, zero waypoint pauses, route orientation and
Linear easing or a compiled speed curve. A copy's clock cut must leave at least
80 ms and 1 cm per split leg; otherwise preview/export refuses with an adjustment
message. The authored route remains limited to 128 legs; clock rebasing can add
one internal split per copy. A conservative 16,384-body budget also applies.
Embedded lights cannot be scaled. Collision-relay arrays are explicitly refused.
Start with 2–8 copies; many carriers and parts can reduce game performance.

## Layers and editing

Up to four enabled/muted, reorderable local layers compose after path yaw/pitch/
roll and before part hinges: translation oscillation (metres), rotation oscillation
(degrees), and continuous spin (one turn per period). Each has an axis, period and
reverse option. Arrays/layers persist in route JSON and custom templates.

Scrubbing pauses the draft at the chosen time. Pause/resume and Start operate
without applying the animation to the item, including after stopping the preview.
Optional XYZ axes display the first copy's path anchor and existing part hinges.
Undo/Redo stores 40 draft changes, including route points, profiles, groups and
layers. Apply remains explicit; draft history is not whole-document undo.

## Verification

- All repository suites and tracked research-project builds passed.
- ItemMotion: 64 passed, 0 failed; covers exact quadratic rebasing, serialized mesh
  sharing, native body-only bindings, source preservation and rejected inputs.
- Browser `path-array.cjs`: copies, shared preview geometry, undo/redo, paused scrub,
  resume, hinges, layer mute/add/remove, JSON persistence and Apply.
- Existing `path-profile.cjs` and `path-dragging.cjs` passed: ramps/rotation,
  preview/export parity, camera-locked point dragging and preview resumption.
- Native V1 exposed visible parked branches: concatenated prefab entity offsets
  incorrectly included constraint entries. Fixed by placing all bodies first and
  remapping only body indices; a regression assertion covers that invariant.
- Native V2/V3 show four carts following one another, eight birds and eight fish,
  with shared size variants, moving parts and local sway/bob layers. Isolated bird
  run V4 reaches game clock 01:05.21, exceeding two complete 32-second cycles.
  Tests leave Trackmania in Menu. No passenger collision is claimed.

Local artifacts: `artifacts/instance-layers/Path-Array-Showcase.mp4` (36 seconds),
`TMIS-Path-Array-Examples.zip` (items, editable sources, route drafts), raw native
videos/call logs, and browser evidence. Items are installed in the existing
`Documents/Trackmania/Items/TMIS_Template_Showcase` folder. SHA-256 and byte counts
are recorded in `docs/evidence/path-arrays-2026-11-01.json`.

The procedural examples contain about 81 KB (four carts), 135 KB (eight birds)
and 127 KB (eight fish). These are example sizes, not estimates for user meshes.
