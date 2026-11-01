# Collision handoff and custom coaster cart research

No production change is justified by this test batch. Studio 0.5.8 remains experimental. The user authorized a production update only if the anti-launch tests succeeded.

## Controlled native variants

All three use the same original 128-point route and red cube, with a fixed passive vehicle spawn and item offset +1.25 m X and Z. The map has no coaster rails; vehicle inputs are zero. Each file was generated with the production compiler, reparsed, checked for at most four keys per channel and synchronized duration. Inactive collision vertices were checked at both segment endpoints against Stadium terrain at Y=8.

- Parking48: 48 m rather than 64 m folding offset; 5 m/s. Highest inactive collider Y=-3.468 m, safely below terrain in this fixture. Sustained launch at 42.891 s, peak upward speed 58.082 m/s. Failed.
- Slow25: 2.5 rather than 5 m/s; 64 m parking. Sustained launch at 89.516 s, peak upward speed 48.190 m/s. Failed. Slowing the route changes the failure time but does not remove the failure.
- Overlap1500: incoming collider appears 1.5 s before its movement; outgoing collider remains for 1.5 s after the next segment starts. No >15 m/s upward impulse, but the passenger falls to terrain at 10.329 s and remains behind while the platform moves on. Failed. Absence of a launch is not successful passenger transport. This diagnostic can expose multiple visible platforms; it is not a shippable visual mode.

Recordings, exact fixtures and full telemetry remain in `artifacts/handoff-cart`. Compact non-account results are in `docs/evidence/handoff-cart-2026-10-31/results.json`.

## User-requested cart

No identifiable coaster cart was found in the local item filenames/headers or relevant ZIP contents. Tourist wagons are different props. The user then explicitly requested a newly created cart.

`make_cart.py` creates an original four-seat cart in Blender: chassis, front/rear/side panels, seats, lap bars, four 12-sided wheels and hubs. It exports editable `.blend` and triangulated mesh data. Wheel centre spacing is 7.8 m to match the known deck strip; the body is 6.5 m long and 7.4 m wide. Blender colors are a design preview. The game version uses stock RoadBump/PlatformTech/RoadTech material links and therefore looks different.

`CartBuilder.cs` preserves a proven native visual/vertex declaration layout, including both UV channels, packed normals and tangents. Raw channel buffers are cleared when resizing, then the library repacks the new arrays. Newly constructing a minimal vertex layout produced an item that parsed and had collision but was invisible in the game. The later source-layout-preserving export is independently checked as a stationary cart before route testing. This is why parse success alone is not sufficient.

Collision is a simple chassis box; seats, walls and wheels are visual geometry. This is a research prop, not a simulated wheeled vehicle. The source item is stationary with zero translation/rotation amplitude. It retains a kinematic template so Studio can author a route from it.

## Orientation extraction

The known NewCoaster deck strip supplies approximately 7.8 m cross-section edges. `route_frames.py` follows the same strip topology used by the earlier route extractor and associates each route point with its nearest strip cross-section after removing the old cube's 4.2 m clearance offset. The largest association distance is 0.189 m. This is specific to the known mesh, not a universal automatic banking extractor.

The forward vector is the outgoing segment tangent. The cross-section direction is projected perpendicular to it; their cross product gives the up direction. The 128 frames include slopes roughly -43.42 to +46.86 degrees and up to 44.43 degrees of bank. A centreline alone cannot supply the true rail bank.

The experimental compiler bakes each segment's target heading/pitch/bank into its visual geometry and chassis collider while retaining the existing world-bound relay driver. Orientation is constant within each straight segment and changes at handoffs. It is not continuous rotational interpolation, does not roll the wheels, and does not fix passenger collision handoffs. A complete cycle is 294005 ms at 5 m/s with 8 s start dwell.

Place the generated banked cart at (400,15,400), and NewCoaster at (534.4675,10.782,400), both with zero placement rotation. The cart path removes the former cube clearance so its wheel-contact plane follows the deck. Actual contact alignment and native visibility must be visually verified; analytical frame extraction is not an in-game proof.

## Native cart evidence

The corrected Cart_Static2 run visibly renders the four-seat cart and supports the passive car for 16.734 seconds (Y 16.064?16.065, zero airborne samples). This establishes stationary chassis collision only.

Cart_Banked4 recorded 95 seconds of native game output with three fixed camera views. Contact-sheet inspection shows the cart on the initial curve and on an inclined track section, with changed heading and attitude. Later views miss the cart; this is not full-lap or continuous wheel-contact proof. The passive car does not complete the route: final position is approximately (399.986,10.814,400.027), with 20 airborne samples. The visible timer also resets during the capture; this recording must not be represented as an uninterrupted successful passenger ride.

CoasterCart-ingame-preview.mp4 is a 43-second extract (source seconds 7?50), at original speed, showing the initial visible sections. It is a cart appearance/movement preview, not proof of solved collision transfer. Native cleanup ends with GetMode=Menu. No production code was changed or deployed.

## Next step

A different collision transfer mechanism is needed: folding while the passenger still overlaps the outgoing body remains suspect. Test a separate visible body and overlapping contact proxies, with explicit support tracking and timed departure clearance. The present overlap counterexample shows why overlap alone cannot be treated as a fix. Continuous cart attitude also needs independent native rotation channels or a better compound animation layout; per-segment pose changes are only a prototype.
