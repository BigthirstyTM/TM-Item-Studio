# Smooth coaster cart and animated birds — native research fixtures

These are standalone experimental Item.Gbx fixtures, not a production Studio UI update. The passenger launch problem remains unresolved. No passenger was on the cart during these tests; the observer vehicle is parked remotely at approximately (80,8,80).

## Cart

`TMIS_Cart_GravitySmooth` uses the same 128-point deck route. Its moving average is 10.0001 m/s (previous version: 5 m/s), with a 2-second initial dwell and a 145.002-second cycle. Segment speed varies from 4.628 to 12.865 m/s. Speed is derived from an energy-style height function and normalized to the target lap time: higher sections are slower, descents regain speed. This is an authored animation, not a free gravitational simulation; speed remains constant within each of the 128 translation segments.

The visible cart now interpolates yaw, pitch and bank between adjacent waypoint attitudes. Waypoint heading uses the incoming/outgoing tangent average. Bank comes from the original deck cross-sections. Each relay body has a shallow rotation branch and one visible cart child. All visible copies share a mesh; inactive branches fold below terrain. At a handoff the next branch starts with the outgoing branch's ending attitude. Native update order can still produce a small handoff artifact: this is not a single rigid dynamic cart.

The original world-bound chassis collider relay is preserved at the new timing. Its collision attitude is still fixed per segment, while visual attitude interpolates. This is NOT a claim of continuously rotating physical collision or safe passenger transport.

A long accumulated carrier-chain prototype was rejected after native observation showed unwanted delay. The delivered cart graph has maximum body depth five, birds depth six. Every body has one driver; channels have at most four positive-duration keys. Production segment/vertex limits were not changed.

## Birds

Three original low-poly gull-like models fly separate phase-offset loops over the western coaster area, at 12, 13 and 14 m/s. Each has a body/head/beak/tail and two actual moving wing meshes. The wings rotate oppositely around the forward axis, between -25 and +40 degrees, using SineInOut half-strokes. Their full periods are 620, 710 and 800 ms. These are native GBX animation channels, not an external script moving the models.

Flight paths pass both above and below the track. A periodic height bump clears an intermediate crossing. Independent sampled centreline checks measure at least 4.98 m vertical separation where the route is within 4 m horizontally of the deck centreline. This does not prove clearance against every support beam or the entire banked deck mesh. Final wingspan is approximately 2.58 m (the initial oversized observation model was reduced to 30%). Birds are scenery with only compatibility micro-shapes, not usable obstacles.

The 54-second demonstration combines final-scale cart/bird footage with ten seconds from the full cart lap. All clips play at native speed (no speed-up or frame interpolation). The full cart run used the earlier larger bird observation model in the western background.

## Placement and files

- Cart and birds: world position (400,15,400), zero rotation.
- Existing `NewCoaster.Item.Gbx`: (534.4675,10.782,400), zero rotation.
- Local installed items: `Documents/Trackmania/Items/TMIS_CoasterCollision`.
- Generated source, native calls and recordings: `artifacts/coaster-flight`.
- Source tools: `Tests/CollisionHandoffVariants/FlightScene.cs`, `make_bird.py`, `check_flight.py`.

## Verification

- GBX reparse and graph validation: unique drivers, valid slots, no cycles, maximum depth six, positive-duration channels with at most four keys.
- Independent schedule/geometry checks: average speed, monotonic timing, altitude-speed relationship, waypoint heading reconstruction and bird over/under crossings.
- Native run `Cart_GravityC`: initial bends/sections and flying birds visible.
- Native full-route run `CoasterFlight_Full`: 180-second capture, cart observed around the route and into its next lap, with changing heading/pitch/bank. Fixed cameras keep most sections visible; this is visual evidence, not millimetre wheel-contact measurement.
- Bird normals/periodic crossing correction: verified in `CoasterFlight_FinalB`. Final reduced-scale models were recorded in `CoasterFlight_Delivery` and `CoasterBird_Tracked`. Close tracking shows changing wing poses, flight above the low track and descent below the elevated track between its supports. Camera-only tracking uses discrete MCP updates; the bird animation itself is native. All successful runners finish in Menu.

The map is a visual test fixture without a race start/finish. Use the editor's test-from-cursor mode to watch the animation. This is not a validated race map.

The saved delivery map reparses with exactly three anchored objects and embeds all three model files. The first editor readiness query after several restarts suffered a transient MCP connection reset while the game remained responsive; read-only readiness retries are now bounded in the runner. No game/plugin production code was changed.
