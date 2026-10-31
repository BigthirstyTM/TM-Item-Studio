# 128-leg rollercoaster proof

The former eight-leg cap was a tested product limit, not a GBX format limit. A corrected browser export now follows a closed 128-point route on the user's `NewCoaster.Item.Gbx` in Trackmania 2020. The visible object is the user's red `BF2_ASSETS/Cube.Item.Gbx`, not a train. `Extras/Tourist_Wagon` and related train items were found but were not used in the proof.

## Limits

Studio 0.5.5 permits 128 total movement legs: 128 distinct points for a closed loop, 129 points for Reset, or 65 points for PingPong (64 outward plus 64 return legs). Each native channel still has at most four keys; each hidden carrier uses at most three. The proof contains 128 carriers and one visible body. The absolute engine limit has not been established. Do not treat the new product cap as a measured engine ceiling.

Other existing guards remain: finite points within 10,000 metres, speed 0.1–1000 m/s, at least 80 ms per leg, and a signed 32-bit millisecond cycle duration. More legs increase the number of bodies/constraints and preview/runtime work. Multi-direction paths are scenery: they do not carry usable vehicle collision. Heading, pitch and banking along the route are not implemented; the cube keeps its original orientation.

## Conversion defect discovered during native testing

The initial browser exports at 128, 64 and 63 legs failed native loading; some direct FID loads terminated Trackmania. A 32-leg test-generator export worked, and a 63-leg generator export loaded. This initially suggested a size ceiling, but **that interpretation was disproved**: preserving the static source document and replacing only `EntityModel` caused the browser export failure. Changing identity alone did not repair it. Installing the complete converted template document repaired the 63-leg export and then the 128-leg export.

Both Draw Motion Path's automatic conversion and the explicit static-to-kinematic conversion now install the complete conversion result. Known source collector metadata is copied by the converter. Path restore retains the original static document, including its original chunk versions, rather than trying to reconstruct it from the converted root. Unsupported graphs remain rejected. This is a conversion to the template format, not a byte-preserving edit of the old static archive.

Rejected files were moved out of the game's Items directory and retained locally under `artifacts/coaster-path/quarantined*`. No engine count limit can be inferred from those invalid-document tests.

## Reproduction and evidence

The coaster's top deck is a closed strip. Welded triangle adjacency gives an ordered traversal, cross-section edge midpoints supply the centreline, and chord simplification with 0.5 m tolerance reduces it to 123 legs. Splitting five long legs produces the committed 128-point route. The cube centre is 4.2 m above that line; the cube is 8 m tall. This is a polygonal approximation of the rails, not automatic spline extraction exposed in Studio.

Use the source cube, Scenery, Closed, Linear, Mesh centre, 20 m/s and zero pause, then paste the coordinates from `evidence/coaster-path-2026-10-31/route128.json`. Preview, Apply and export. Place the coaster and the exported cube at the same world position **(800,24,800)** with zero rotation. The cycle lasts **71.509 seconds**. The saved local map is `Documents/Trackmania/Maps/TMIS_Coaster_FollowPath.Map.Gbx`.

Evidence under `evidence/coaster-path-2026-10-31`:

- `TMIS_Achtbaan_Bewijs.Item.Gbx`: exact browser-export bytes used in game, internal ID `TMIS_Coaster_128Fixed`; SHA256 `C0047E5B7D4FDBE91398E21F1C3479866E18B7A61B45386F8AE56A0E7550F79B`.
- `Achtbaan-128punten-Ingame.mp4`: 76 seconds of real-time Trackmania window capture, 1280×720, 2,271 captured frames; more than one complete cycle. No animation script drives the item.
- `contact-sheet.png`: sampled native frames, inspected to confirm traversal across the track.
- `route128.json`: exact route used by the browser exporter.

Native evidence uses Trackmania 2026.2.2.1751 and Openplanet 1.29.14. Openplanet/MCP only prepared the map and camera. Native FID load, inventory lookup, placement counts, saved map, test mode, video and SHA256 were checked separately. The game was returned to the main menu and the default agent tag cleared.

## Regression checks

The motion suite checks the maximum closed loop, excessive closing/return legs and metadata preservation. `long-motion-path.cjs` checks a 128-point non-mutating draft, conversion and restoration of a static document. Existing eight-point preview/midpoint/drag and motion-path plane/export/reopen tests remain passing. The repository console suite and application build pass; existing build warnings remain.
