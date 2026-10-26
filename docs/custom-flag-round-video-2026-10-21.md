# Custom flag shapes: high-framerate ingame recording

Date: 21 October 2026. Report language: English.

The swallowtail, long cloth, and newly created round cloth are all visible and animated in Trackmania's editor playground. The recording first shows the three flags together, then moves the actual game camera closer to each flag in turn. This is a runtime-assisted rendering proof. Standalone operation after a full game restart remains unresolved at this milestone.

## Download and viewing sequence

[Download the MP4](../artifacts/flag-round-video-20261021/custom-flags-swallowtail-long-round.mp4)

| Approximate time | View |
| --- | --- |
| 0–7 s | All three flags: swallowtail on the left, long cloth in the middle, round cloth on the right |
| 7–19.7 s | Move closer to the swallowtail, hold the close-up, return |
| 19.7–32.5 s | Move closer to the long cloth, hold the close-up, return |
| 32.5–45.3 s | Move closer to the round cloth, hold the close-up, return |
| 45.3–48.5 s | Final overview |

The file is 1920 × 1080, H.264, 48.5 seconds, with 2,797 captured video frames. Capture requested 60 Hz; the measured average is 57.670 frames/s. The capture log reports zero duplicated output frames and 37 dropped frames. Variable timestamps are retained. No optical-flow interpolation, synthetic cloth motion, speed change, or conversion of a sparse screenshot sequence was used. The final encode preserves the raw capture's frame count. English labels and chapter metadata were added during encoding.

Three-second stationary close-up samples contain 173, 180, and 180 frames for the swallowtail, long, and round cloth respectively. Every decoded flag-region sample has a distinct hash. These measurements verify recorded motion and should not be interpreted as an engine-performance benchmark. Game/plugin HUD elements remain visible in the direct capture; they do not obscure the cloth.

## Models and live verification

The models were generated from the previously saved native vanilla-copy fixture, not by modifying the user's original XertroV file:

- `TMIS_Step07_Swallowtail.Item.Gbx`: width varies with cloth height to form the notch.
- `TMIS_Step06_Long250.Item.Gbx`: cloth X coordinates are scaled by 2.5.
- `TMIS_Step08_Round.Item.Gbx`: square-to-disc UV mapping adds a circular rest outline while retaining the original baked displacement across all 86 animation frames. Normals are recalculated for this new shape. Its circular outline bends and folds with the existing animation.

The generator verifies saved/reparsed vertex positions, normals, frame tables, index buffers, stream positions, and bounds for all five visuals. The native-copy baseline's far LOD differs from the original vanilla static far LOD, so this is not a claim of exact vanilla LOD, lighting, collision, or skin parity.

The game inventory found all three models. Placement changed an empty disposable map from zero to three items. A subsequent `GetItems` readback verifies their exact paths and positions. Fresh screenshots and the video show the distinct forms. The tested environment is Trackmania 2026.2.2.1751 / engine 2026-02-02 17:51:19, with Openplanet 1.29.14 Next/Public 6fd4200f.

## Runtime assistance and restart limitation

The bounded DEV fixture sets the previously proven transient `CPlugVisual` structured-buffer flag (`+0x24`, bit 27) on exactly the three named models before first rendering. The engine then creates and binds the animation's structured vertex buffer itself. There is no D3D binding-repair hook in this recording.

The helper is gated, fixed to these files, limited to 240 seconds, and restores only the bits it added on timeout, gate removal, disable, or destruction. Source and staged hashes are retained. LSP reports zero diagnostics on the exact staged bytes. The fresh game log contains a successful load followed by `PREPARED` entries for all three models and no attributable compile error. Unrelated MCP inventory warnings about the Vehicles collection are preserved in the log.

The preceding cold-restart test already showed that the embedded long and swallowtail models lose their cloth without runtime assistance. The transient bit is not serialized by the native visual archive. This recording therefore proves custom geometry and animated rendering, not standalone persistence. The next investigation must make the engine regenerate the structured-buffer state during normal file loading, or explicitly identify a required runtime dependency.

## Evidence

All new evidence is under `artifacts/flag-round-video-20261021/`:

- `placed-items.json`, `game-calls.jsonl`: verified game state and request/response history.
- `setup-fresh.log`, `log-boundary.json`: fresh successful plugin load and model preparation.
- `staged-identity.json`, `staged-lsp.json`: exact staged identity and complete diagnostics.
- `models/steps-manifest.json`, `models/*.Item.Gbx`: model artifacts and geometry statistics.
- `evidence/framed-*.webp`: inspected native viewport captures.
- `three-flags-60fps-raw.mp4`, `capture60.log`: original direct capture and recorder log.
- `capture-validation.json`, `final-video-metadata.json`: measured frame counts, timestamps, and cloth-motion samples.
- `video-timeline.json`, `video-filter.txt`, `chapters.ffmetadata`: camera/view timing and final presentation.

This milestone report was delivered with the video before resuming persistence research. Subsequent findings are recorded separately.
