# Path rotation and speed profiles — Studio 0.5.9

## Authoring

Generate or load a route draft, then open **Path segments & timing**.

- **Path tilt & rotation**: Follow route direction initializes yaw/pitch from averaged route tangents, with local +X as forward. Edit yaw/pitch/roll on either endpoint of the selected segment. Whole-route offsets are additive and apply on the button click. Loop endpoint edits stay linked, retaining complete yaw turns.
- **Whole-route speed curve**: enable the curve, select a timeline point or use the searchable point dropdown, then edit its m/s value or slider. The selected point also selects its outgoing route segment. Range ramps interpolate speed with travelled distance. The coaster preset uses `sqrt(25 + 4.4*(highestY-y))`; it is an authored height/speed profile, not a physics simulation.
- Curve timing overrides the original segment timing/easing. The segment overview displays compiled durations and average speeds; direct duration edits are disabled until the curve is disabled. Disabling restores the original manually authored timings. Route JSON retains profile settings and original geometry.
- Preview remains separate from Apply. Shared compile logic drives both preview and export, and failures leave the source document unchanged.

## Native representation and limits

Endpoint speeds vary linearly with distance. Travel time over length L is `L/(v1-v0)*ln(v1/v0)`, with the constant-speed limit `L/v0`. The compiler approximates this with up to 16 linear samples per source leg, restricted by the existing 128-leg budget and 80 ms minimum native segment duration. At the budget limit, a leg can receive only one average-speed sample; the UI explicitly describes this. Samples do not imply continuously varying native velocity. Speed profiles currently require zero waypoint pauses and speeds 0.1–1000 m/s.

Rotation uses world-bound relay branches and three shallow yaw/pitch/roll carriers plus the shared visible mesh. The maximum depth is five, with one driver per body and at most four positive-duration keys per scalar channel. It avoids the rejected long accumulated orientation chain. The original shader animation, texture/shadow instance settings and unrelated static entries are preserved. Unrotated source bodies, decoded static triangle visuals and relay-compatible directions are required; near-vertical relay legs are refused.

Scenery replaces collision with native-compatible micro-shapes. In experimental collision-relay mode, collision keeps the source orientation, while the visible mesh interpolates its rotation. This is not continuously tilted collision. Passenger launches and collider handoff effects remain unresolved. The ordinary single-driver straight obstacle mode does not expose these additional profiles.

## Verification

- ItemMotion: 43 passing checks, including logarithmic travel time, acceleration ordering, route preservation, rotation sampling, malformed profile rejection, JSON roundtrip, source isolation, shallow unique bindings and serialized key/timing limits.
- Browser: `path-profile.cjs` exercises curve points, range ramps, rotation fields, segment selection synchronization, JSON roundtrip, native export and all compiled midpoint matrices against the live draft. Maximum matrix difference is recorded in `docs/evidence/path-profiles-2026-11-01.json`.
- Existing `path-segment-edits.cjs` and `smooth-motion-path.cjs` pass.
- All console suites and tracked research-project builds were run. A concurrent edit initially interrupted VariantExport's app build; after completion it passed all 38 checks. This was a local build-order failure, not a runtime test failure.
- Native `Studio_RampPose`: the browser-exported file was installed, inventory-selected, placed in a new disposable map, saved and observed over a 30-second recording. The cart visibly moves around the route and changes attitude. Its SHA256 matches the final browser export. The observer car is remote; this is a visual test, not a passenger test or a quantitative native speed measurement.
- The native runner finishes in Menu. Full game videos and saved maps are local under `artifacts/coaster-flight`.

To reproduce the browser proof, generate the research cart fixture first (`Tests/CollisionHandoffVariants/CartBuilder.cs`), build the app, run `node Tests/Browser/serve-studio.cjs`, set `STUDIO_BASE_URL` to its printed URL and run `node Tests/Browser/path-profile.cjs`. Native tests require the local Openplanet/E++ stack and registered inventory items; they are excluded from CI.
