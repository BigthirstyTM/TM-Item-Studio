# Changelog

All notable changes to TM Item Studio are documented in this file.

## [0.5.15] - 2026-11-01

- Route along a model: load a separate reference Item.Gbx or capture a selected mesh, search its parts, align its position and draw/drag points directly on its surface.
- Snap existing points to the reference with a maximum distance, or generate a shortest connected mesh-edge route between guide points. Adjustable approximation and loop closure; disconnected surfaces and paths exceeding 128 segments are refused without replacing the current points. This is geometric routing, not automatic rail-centreline recognition.
- Reference geometry is a viewport-only guide; generated paths use the normal editable preview and explicit Apply/export workflow.

## [0.5.14] - 2026-11-01

- Dock the scrubbable path preview timeline below the viewport canvas instead of in the inspector. Playback controls remain visible while browsing path settings.

- Automatically preview route edits, speed changes and smoothing controls, even after stopping or applying the preview. Live array inputs update while typing. Rebuilds retain the playback position and paused scrub state.

- Preserve array settings when switching to or regenerating a smooth path, including direct Apply smooth path.

- Fix arrays failing when a third or later follower splits a route segment below the native minimum duration or distance. Such offsets now snap to the nearest route point, consistently in preview and export, instead of rejecting the entire array. Maximum-detail paths also avoid adding a 129th segment. The panel explains the possible spacing adjustment.

## [0.5.13] - 2026-11-01

- Experimental collision-relay arrays retain separate physical colliders for each copy, including uniform scale variants, spatial spread and time offsets. The shared startup dwell is retained while copy offsets apply to the moving portion of the loop.
- Bound total expanded collision vertices and retain the existing per-channel timing limits. The timeline, draft history and custom-template storage also work with these physical arrays.
- Expose experimental physical translation, rotation and spin layers, plus optional sampled route tilt. Up to 128 samples move and orient both the visual body and collider; rotation steps at handoffs and layer clocks restart each lap. Warnings explicitly cover passenger launches, drops and loop seams.
- Allow animated visual parts with a rigid source collider in collision mode. Wings, wheels and other articulated parts do not receive independently animated collision. Sources still need useful collision geometry; arrays do not invent it.
- Native passenger tests cover multiple copies and scale variants. Sustained support on a tested segment is not a guaranteed complete ride; a later handoff lost the passenger in the longer second-copy test.

## [0.5.12] - 2026-11-01

- Array along a looping scenery path: up to 32 time-offset followers, deterministic XYZ spread, 1-4 shared size variants and alternating wing direction. Mesh geometry is shared across equal-size copies; animation data still adds to file size.
- Up to four ordered, independently enabled local animation layers for translation, rotation and spinning, composed after route orientation and before moving parts. Arrays and layers are retained in route drafts and custom templates.
- Scrubbable preview timeline, pause/resume, return to start and visible path-anchor/part-hinge axes. Route draft Undo/Redo retains 40 edits, including points, profiles, arrays and layers; applying remains explicit.
- Native gallery tests cover four following carts, eight birds and eight fish. Corrected multi-copy body bindings after an initial native test exposed visible parked branches. Arrays and additive layers are scenery-only; they do not add passenger collision or rigid train coupling.

## [0.5.11] - 2026-11-01

- Eight additional movement presets: lift/platform, cable car, drone/helicopter, fish, butterfly, robot, spinning ride and conveyor. Station presets stop smoothly and wait at route points; sway presets add geometry samples when needed.
- Save named custom movement templates locally, search by category/name, import/export JSON and adapt profiles by relative route distance. Optionally restore the complete route and explicitly reuse part mappings.
- Vertical scenery carriers support lift routes; vertical collision relay remains refused.
- Fourteen procedural demo models, browser exports, one embedded native showcase map and a 53-second in-game compilation. See `docs/template-library-showcase-2026-11-01.md`.

## [0.5.10] - 2026-11-01

- Movement templates for rollercoasters, birds, cars, boats and trains, with editable speeds, braking, corner banking and a marked coaster lift. Coaster speed carries momentum between segments, gains speed downhill and loses it uphill.
- Continuous scenery acceleration inside each segment through shallow linear/quadratic carriers, without spending more route points. Sampled ramp detail remains adjustable for collision relay.
- Separate existing visual meshes can animate as wings, wheels or propellers, with editable pivots, axes, periods, radius and mirrored direction. No automatic splitting of fused meshes; wheels use segment-average speed.
- Total loop time scales the movement profile while retaining fixed delays/pauses and relative speed differences.
- Locked top/front/side path views, exclusive point dragging with preserved depth, highlighted active draw/preview buttons, and an explicit preview-delay countdown.
- Drag speed-curve points vertically. Preview resumes the edited schedule; coordinate drags with an unchanged point count retain profile and part settings.

## [0.5.9] - 2026-11-01

- Collapsible path tilt/rotation editor: follow direction, per-point yaw/pitch/bank and whole-route offsets, with matching draft preview and native export.
- Whole-route speed timeline with searchable point selection, endpoint speeds, range ramps, sampling detail and a height-based coaster preset. The segment overview shows effective timings while the curve is active. Draft JSON retains all settings.
- Speed curves use sampled linear movement within the existing 128-segment/80-ms limits. They require zero waypoint pauses. Rotation is visual; experimental relay collision does not continuously tilt with the mesh and passenger handoffs can still launch cars.
- Correct the experimental bird wing channels to supported QuadInOut easing. Record the full coaster lap with multiple camera positions and a separate close wing-animation proof.

## [0.5.7] - 2026-10-31

- Collapse the one-click Motion & Path Assistant by default.
- Choose Full path or selected segment in the viewport, with a yellow selection highlight. Select route lines to synchronize the segment editor; repeated clicks cycle overlapping outbound/return legs.
- Edit endpoint coordinates, speeds and durations directly in Complete route overview. Row and dropdown selection synchronize with the viewport, including after applying the route.
- Correct dynamic selected values in the path and native-constraint dropdowns. Clear route overlays when changing files and preserve camera dragging and drawing interactions.

## [0.5.6] - 2026-10-31

- Separate smooth scenery path tool with adjustable corner rounding, curve detail and live preview. The existing straight/polyline tool remains available.
- Generated route editor with searchable segment selection, full route overview, shared endpoint edits and per-segment speed/duration. Following channels are rescheduled together; straight obstacle return timing stays linked.
- Save/load route drafts as JSON for continued editing with the source item. Preview and apply remain separate.
- Advanced constraints now show one searchable selection at a time, with translation and rotation controls grouped beneath a collapsible heading.
- Native collision experiments verify repeated lift support and circular obstacle contact. Two world-bound translation constraints on one body did not produce the intended square. Arbitrary multi-direction moving collision and per-segment rotation are not advertised as supported.
- Native video evidence and reproducible experiment generator documented in `docs/smooth-path-collision-2026-10-31.md`.

## [0.5.0] - 2026-10-26

### Added

- **Flag items**: measure an item against the game's own flag and convert it into one
  (`Models/ItemFlagConversion.cs`, `Models/ItemFlagExport.cs`, the **Flag** tab in the item
  editor). The measured signature is a variant tagged `Type=Flag` in a variant list of version 1
  plus the only placement class in 8 802 items with `AlwaysUp=true` and `AlignToWorldDir=true`,
  together with its eight patch layouts. The conversion writes the classification and the
  placement and leaves pivots, geometry, materials and metadata alone; refusals (external placement
  file, opaque `2E020005`, newer variant-list version, no entity model) all run before the first
  write, so a rejected conversion leaves the bytes untouched. The export works on its own snapshot
  and reparses its bytes before downloading. Evidence and the measured settings are written up in
  `docs/item-flag-conversion.md`.
- `Tests/FlagConversion`, a flag conversion suite (measured shape, save/reparse round trip, export
  snapshot isolation, refused-write byte equality), registered in `Tests/run-all.sh`.
- `Tests/FlagProbe`, the evidence tool used to derive the flag settings: corpus tag/profile scans,
  a GBX dumper, `--lod` (per-solid LOD distances, masks, per-visual geometry and authored bounds),
  `--lodstats` (LOD distance/level consistency over a corpus), `--entries` (prefab entries with
  their transforms) and `--convert` (measure, convert and write, with cloth layout variants).

- **Macroblock & instances** tab: duplicate the loaded item into placed copies (add,
  duplicate, grid fill, per-copy position/rotation/scale) and export them as
  `.Macroblock.Gbx` (`CGameCtnMacroBlockInfo`). Every copy is an `ObjectSpawn` that
  references the item ident, so duplication costs about 15 bytes per instance instead of
  copying geometry. The export reparses its own bytes before downloading.
- Every copy is rendered in the 3D viewport: instance parts reuse the item's shared geometry
  and carry only a placement transform (`ItemMacroblock.Placement`), and each table edit
  redraws the scene.
- **Multi-instance item export**: the same copies can be written into one self-contained
  `.Item.Gbx` whose root prefab holds one entry per copy, all referencing the same embedded
  model node. GBX.NET stores a repeated node once, so twenty copies cost about 7 bytes each
  (35 076 B item -> 35 052 B with 20 copies) instead of a second copy of the mesh. The export
  takes its own snapshot of the document, gets a distinct id (`<id>_x20`), keeps every copy's
  position and rotation relative to the root, and reports skipped foreign copies and dropped
  scales. A macroblock cannot do this: its spawn stores `ident ItemModel` only.
- `Tests/MacroblockExport`, a round-trip suite covering spawn fidelity, macroblock class id
  `0x0310D000`, per-instance size, ident/instance validation, re-save stability, the load
  fallback, the viewport placement matrix and the multi-instance item export.

### Fixed

- Loading a `.Macroblock.Gbx` no longer fails with `Specified cast is not valid.`: macroblocks
  are dispatched by class id before the item parser runs, and a file that still reaches the
  item parser is retried as a macroblock (`ItemMacroblock.TryRead`) before the error reports
  the class id that was actually found.

## [0.4.3] - 2026-10-07

### Fixed

- Restored composite **group motion** to the last known stable 4-segment behavior.
- Reverted group motion timelines from 10 back to 4 segments across:
  - group motion UI controls,
  - group motion viewer payload serialization,
  - grouped merge/export motion keys (`GroupKinematicMotion` build path).

## [0.4.2] - 2026-10-07

### Added

- Constraint-level anchor editing now includes a whole-constraint offset control,
  allowing individual constraints to be translated by moving `Pos1` and `Pos2`
  together per axis.

### Changed

- Localized the full in-app Studio UI text to English, including motion/path
  assistant labels, tooltips, guidance text, and user-facing status/error messages.
- Motion timeline segment support now allows up to 10 keys across editor UI,
  preview validation, and grouped motion export payloads.

## [0.4.0] - 2026-10-06

### Added

- Composite Prefab Merging: merge multiple item prefabs into one assembly with
  world translation and rotation offsets directly manageable in the 3D viewport.
- Custom Pivot Point Editing: interactive pivot translation and rotation gizmos,
  personal pivot retention, and pivot connection across multiple selected meshes.
- Composite Group Motion: group selection with combined motion curves and
  hierarchical carrier-body constraint chaining (`World -> Carrier -> Members`),
  allowing group movements and individual member animations to play simultaneously
  without constraint collision.
- Carrier Body Loader Compatibility: minimal renderable visual geometry with
  shaded material mapping on kinematic carrier bodies, preventing Trackmania
  inventory drop and loader crashes.
- Experimental Dual-Body Collision Proxies: swept collision shapes for grouped
  moving members to preserve collision under Trackmania's Havok solver.
- Motion Path Assistant: multi-axis path generation, collision relay, and parked
  handover L-path presets.
- Trackmania PBR Texture Preview: local DDS texture loading for `_D`, `_N`,
  `_R`, and `_I` sets with sRGB base color and roughness/metallic channels.
- Multi-solid material index collision resolution when re-importing merged items.

### Changed

- Expanded viewport settings panel width and separated composite world transforms
  and group motion into dedicated collapsible controls.
- Rebased kinematic slot binding to safely handle multi-prefab assemblies.

## [0.3.0] - 2026-09-15
- Corrected browser contracts for shared geometry, typed variants, and
  asynchronous variant selection.

## [0.3.0] - 2026-09-15

### Added

- Native Trackmania/Openplanet bridge requests for pivot edits, with a bounded
  smoke test for native item loading.
- A model diagnostics report for detected, external, invalid, and unsupported
  item data.
- Authored kinematic-motion inspection and editing UI.
- `Tests/WriterCompatibility`, a controlled parse/save and existing-pivot
  compatibility probe for user-supplied items.

### Changed

- Preserve Trackmania 2020 collection `26` as its numeric value instead of
  rewriting it as the display string `Stadium2020`.
- Expand GmSurf/collision-surface parsing support.
- Preserve full imported variant metadata while improving variant navigation
  and preview performance.
- Standalone GBX exports that re-encode an imported archive now require an
  explicit compatibility acknowledgement instead of being rejected solely
  because they are not byte-identical.

### Verified compatibility

- A Blendermania pivot item exported through the standalone GBX.NET path was
  verified after a fresh Trackmania start: it appeared in the map-editor
  inventory, remained visible after placement, and retained edited pivots,
  kinematics, and lights.

### Notes

- Re-encoded items from other source families still require a fresh
  Trackmania inventory and placement check. Use the native bridge for
  unverified sources.

## [0.2.0] - 2026-09-11

### Added

- English user interface and startup/error messages.
- Embedded multi-variant item detection, viewport switching, and export.
- Compatibility handling for Editor++ item null-node references.
