# Follow Path: preview, editable points and native proof

This follow-up addresses the report that the model remained beside the drawn route. In 0.5.3 drawing did not animate the draft, the default obstacle mode rejected more than two points, and a path described the body's internal origin rather than its visual centre. The earlier native square export does animate, but that did not make the authoring workflow clear or verify what the user saw before conversion. The supplied screenshot alone cannot identify the exact source item's graph.

## Authoring changes

- Scenery/closed-loop is the default for drawn paths. Straight collision-driven obstacles remain a separate choice.
- **Preview path** animates a temporary copy of the moving mesh without changing the authored graph or export. **Stop preview** restores the original animation.
- Choose **mesh centre** (default) or **body origin** as the point that follows the route. The compiler compensates the chosen local anchor using the final body rotation. Preview and exported animation use the same anchor.
- Drag existing points while drawing; clicking empty space adds another point. Coordinates and the path update as the point is dragged. The draft animation rebuilds after releasing the point or changing coordinates/settings.
- Closed routes show the closing line. Up to nine entered points and eight total movement legs are supported; a closed route with distinct endpoints typically uses at most eight points. Returns count toward the limit. This replaces the previous four-leg product cap after an eight-leg native test. The engine's four-keys-per-channel guard remains unchanged.
- Applying replaces the session's generated path. Clearing points does not delete applied motion. Opening a new document clears drawing and draft playback.

The preview is a visual draft, not an assertion that every imported item's graph can be converted. Unsupported nested/multi-body/driver structures still fail without rewriting the source. High-level route editing after reopening a generated carrier item is not implemented; retain the coordinates for the original source.

## Native workaround and proof

The exported item uses eight hidden carrier bodies. Each has a fixed orientation and a three-key-or-shorter hold/move/hold timeline. A final visible body attaches to the last carrier. Their translations add up to the route, while the visible body's orientation remains unchanged. Trackmania evaluates this in the Item.Gbx itself. Openplanet only set up the test map and camera; no script moves the object during playback.

Proof files are in [evidence/live-follow-path-2026-10-30](evidence/live-follow-path-2026-10-30):

- `TMIS_FollowPath_Proof.Item.Gbx`: the exact browser export loaded into the game.
- `FollowPath-Ingame.mp4`: 28 seconds of real-time Trackmania footage, over two cycles, 1280×720. It is a window recording, not a rendered browser preview.
- `manifest.json`: SHA256 hashes, placement, route coordinates and options.
- `preview-samples.json`: expected and observed visible mesh centres for every segment midpoint, both in the draft and the compiled viewer animation.
- `native-contact-sheet.png`: selected frames from the video for quick inspection.

The source is the approved native pusher fixture. Its orange stationary housing intentionally remains in place; the separate moving section follows the route. The route has eight points, diagonal legs and height changes, uses linear easing and 8 m/s, and closes back to its start. The saved local map is `Documents/Trackmania/Maps/TMIS_FollowPath_Proof.Map.Gbx`, with the item at (800,8,800). The original and staged item hashes match.

**This establishes visual path following, not a collision-correct moving platform.** Parent-driven mesh collision does not follow arbitrary polylines in the measured engine. Scenery mode removes the moving full-size collider and preserves source static parts. A first-ever claim would require independent research; no such claim is made here.

## Verification

`Tests/Browser/follow-path-preview.cjs` verifies eight-point default mode, every visible mesh midpoint before and after applying, draft graph isolation, actual pointer dragging without appending points, live coordinate edits, and proof export. `motion-path.cjs` covers the previous nine fixture combinations, all drawing planes, invalid inputs, replacement, restore, static conversion and reopen. Helper and existing workflow regressions also pass.

The motion suite adds rotated-anchor tests and extends the excessive-leg rejection fixture to the new eight-leg cap. The full repository suite is run for this change. Native evidence uses Trackmania executable 2026.2.2.1751 and Openplanet 1.29.14.

To reproduce: open the approved `ObstaclePusher4mLevel1.Item.Gbx`, enter the manifest points, choose Scenery, Closed, Linear, 8 m/s, zero pause and Mesh centre, then Preview path. Drag a point or change coordinates, inspect the draft, and Apply path before exporting. Place the exported item in Trackmania and start map test mode; editor rest positions alone do not show the animation.
